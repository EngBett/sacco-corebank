using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

/// <summary>
/// Write-off and restructuring, maker-checker. Approval of a write-off charges the outstanding principal
/// against the loan-loss provision and reverses interest recognised but never collected; approval of a
/// restructure rebuilds the schedule from the outstanding principal on the new terms.
/// </summary>
public sealed class LoanAdjustmentService(LendingDbContext db, ILedgerService ledger, ITenantContext tenant, IClock clock, IAuditLogger audit, INotifier notifier, IOptions<LendingSettings> options)
{
    private LendingSettings Settings => options.Value;

    public async Task<LoanAdjustment> RequestWriteOffAsync(Guid loanId, string reason, Guid byUser, CancellationToken ct)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == loanId, ct) ?? throw new NotFoundException("Loan", loanId);
        await EnsureNonePendingAsync(loanId, ct);
        var req = LoanAdjustment.RequestWriteOff(tenant.TenantId, loan, reason, byUser, clock.UtcNow);
        db.Adjustments.Add(req);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.writeoff.requested", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"loan":"{{loan.LoanNumber}}","reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("loans.writeoff.pending", $"Write-off of {loan.LoanNumber} awaits approval", reason, $"/loans/{loan.Id}", NotificationAudience.HoldersOf(Permissions.Loans.Approve), byUser), ct);
        return req;
    }

    public async Task<LoanAdjustment> RequestRestructureAsync(Guid loanId, int newTermMonths, int? newInterestRateBps, string reason, Guid byUser, CancellationToken ct)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == loanId, ct) ?? throw new NotFoundException("Loan", loanId);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == loan.ProductId, ct);
        await EnsureNonePendingAsync(loanId, ct);
        var req = LoanAdjustment.RequestRestructure(tenant.TenantId, loan, product, newTermMonths, newInterestRateBps, reason, byUser, clock.UtcNow);
        db.Adjustments.Add(req);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.restructure.requested", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"loan":"{{loan.LoanNumber}}","newTerm":{{newTermMonths}},"newRateBps":{{newInterestRateBps?.ToString() ?? "null"}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("loans.restructure.pending", $"Restructuring of {loan.LoanNumber} awaits approval", $"{newTermMonths} months · {reason}", $"/loans/{loan.Id}", NotificationAudience.HoldersOf(Permissions.Loans.Approve), byUser), ct);
        return req;
    }

    public async Task<IReadOnlyList<LoanAdjustment>> ForLoanAsync(Guid loanId, CancellationToken ct)
        => await db.Adjustments.AsNoTracking().Where(a => a.LoanId == loanId).OrderByDescending(a => a.RequestedAt).ToListAsync(ct);

    public async Task<LoanAdjustment> ApproveAsync(Guid adjustmentId, string? notes, Guid byUser, CancellationToken ct)
    {
        var req = await db.Adjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId, ct) ?? throw new NotFoundException("Loan adjustment", adjustmentId);
        var loan = await db.Loans.FirstAsync(l => l.Id == req.LoanId, ct);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == loan.ProductId, ct);
        req.Approve(byUser, notes, clock.UtcNow);

        if (req.Kind == LoanAdjustmentKind.WriteOff) await ApplyWriteOffAsync(req, loan, product, byUser, ct);
        else await ApplyRestructureAsync(req, loan, product, byUser, ct);

        await db.SaveChangesAsync(ct);
        var kind = req.Kind == LoanAdjustmentKind.WriteOff ? "writeoff" : "restructure";
        await audit.RecordAsync(new AuditEvent($"loans.{kind}.approved", nameof(Loan), loan.Id.ToString(), byUser,
            $$"""{"loan":"{{loan.LoanNumber}}","requestedBy":"{{req.RequestedByUserId}}","principal":{{req.PrincipalWrittenOff ?? 0}},"interestReversed":{{req.InterestReversed ?? 0}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest($"loans.{kind}.approved", $"{(req.Kind == LoanAdjustmentKind.WriteOff ? "Write-off" : "Restructuring")} of {loan.LoanNumber} approved",
            notes ?? req.Reason, $"/loans/{loan.Id}", NotificationAudience.User(req.RequestedByUserId), byUser), ct);
        return req;
    }

    public async Task<LoanAdjustment> RejectAsync(Guid adjustmentId, string reason, Guid byUser, CancellationToken ct)
    {
        var req = await db.Adjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId, ct) ?? throw new NotFoundException("Loan adjustment", adjustmentId);
        req.Reject(byUser, reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        var kind = req.Kind == LoanAdjustmentKind.WriteOff ? "writeoff" : "restructure";
        await audit.RecordAsync(new AuditEvent($"loans.{kind}.rejected", nameof(Loan), req.LoanId.ToString(), byUser, $$"""{"loan":"{{req.LoanNumber}}","reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest($"loans.{kind}.rejected", $"{(req.Kind == LoanAdjustmentKind.WriteOff ? "Write-off" : "Restructuring")} of {req.LoanNumber} rejected", reason, $"/loans/{req.LoanId}", NotificationAudience.User(req.RequestedByUserId), byUser), ct);
        return req;
    }

    private async Task EnsureNonePendingAsync(Guid loanId, CancellationToken ct)
    {
        if (await db.Adjustments.AnyAsync(a => a.LoanId == loanId && a.Status == LoanAdjustmentStatus.PendingApproval, ct))
            throw new ConflictException("loans.adjustment.pending", "An adjustment is already awaiting approval on this loan.");
    }

    /// <summary>Dr loan-loss provision / Cr loan control for the principal; accrued-but-unpaid interest is reversed out of income (Dr income / Cr receivable).</summary>
    private async Task ApplyWriteOffAsync(LoanAdjustment req, Loan loan, LoanProduct product, Guid byUser, CancellationToken ct)
    {
        var snapshot = await ledger.FindAccountAsync(loan.LedgerAccountNumber!, ct) ?? throw new NotFoundException("Ledger account", loan.LedgerAccountNumber!);
        var principal = snapshot.Balance;
        var accruedUnpaid = loan.Schedule.Where(i => i.InterestAccrued).Sum(i => i.InterestDue - i.InterestPaid);
        var lines = new List<PostingLine>();
        if (principal > 0)
        {
            lines.Add(new PostingLine(product.ProvisionGlAccountCode, product.Segment, EntryDirection.Debit, principal, Narrative: "Write-off against provision"));
            lines.Add(new PostingLine(product.ControlGlAccountCode, product.Segment, EntryDirection.Credit, principal, loan.LedgerAccountNumber, "Principal written off"));
        }
        if (accruedUnpaid > 0)
        {
            lines.Add(new PostingLine(product.InterestIncomeGlAccountCode, product.Segment, EntryDirection.Debit, accruedUnpaid, Narrative: "Interest reversed on write-off"));
            lines.Add(new PostingLine(product.InterestReceivableGlAccountCode, product.Segment, EntryDirection.Credit, accruedUnpaid, Narrative: "Interest reversed on write-off"));
        }
        Guid journalId = Guid.Empty;
        if (lines.Count > 0)
            journalId = (await ledger.PostAsync(new PostingRequest($"LOAN-WOFF:{loan.LoanNumber}", $"Write-off of {loan.LoanNumber}: {req.Reason}", clock.Today, "Lending", byUser, lines), ct)).JournalEntryId;

        // Guarantees and the borrower's pledge are released: recovery from guarantors is a separate, documented decision.
        foreach (var g in loan.Guarantors.Where(g => g.Status == GuarantorStatus.Accepted))
            await ledger.ReleaseHoldAsync(g.DepositsAccountNumber, g.AmountGuaranteed, $"Loan {loan.LoanNumber} written off", ct);
        if (loan.PledgedDepositsAmount > 0 && loan.PledgedDepositsAccountNumber is not null)
            await ledger.ReleaseHoldAsync(loan.PledgedDepositsAccountNumber, loan.PledgedDepositsAmount, $"Loan {loan.LoanNumber} written off", ct);
        loan.WriteOff(clock.UtcNow);
        await ledger.SetAccountStatusAsync(loan.LedgerAccountNumber!, LedgerAccountStatus.Closed, byUser, ct);
        req.RecordWriteOff(principal, accruedUnpaid, journalId);
    }

    /// <summary>New schedule over the outstanding principal on the new term (and rate, if given), starting today. Interest already due but unpaid is carried into the first instalment.</summary>
    private async Task ApplyRestructureAsync(LoanAdjustment req, Loan loan, LoanProduct product, Guid byUser, CancellationToken ct)
    {
        var snapshot = await ledger.FindAccountAsync(loan.LedgerAccountNumber!, ct) ?? throw new NotFoundException("Ledger account", loan.LedgerAccountNumber!);
        var carried = loan.Schedule.Where(i => i.DueDate <= clock.Today).Sum(i => i.InterestDue - i.InterestPaid);
        db.RemoveRange(loan.Schedule);
        var fresh = loan.Restructure(snapshot.Balance, req.NewTermMonths!.Value, req.NewInterestRateBps ?? loan.InterestRateBps, Math.Max(0, carried), clock.Today, clock.UtcNow);
        db.AddRange(fresh);
        _ = product; _ = byUser;
    }
}
