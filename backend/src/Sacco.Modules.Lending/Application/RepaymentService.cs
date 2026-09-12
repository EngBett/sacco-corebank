using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

/// <summary>Repayments, interest accrual, closure and exposure queries. Implements the module's public contract.</summary>
public sealed class RepaymentService(LendingDbContext db, ILedgerService ledger, IClock clock, IAuditLogger audit, IOptions<LendingSettings> options, ProvisioningService provisioning) : ILendingService
{
    private LendingSettings Settings => options.Value;

    public async Task<RepaymentResult> RepayAsync(RepaymentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reference)) throw new DomainRuleException("loans.repayment.reference_required", "A receipt/transaction reference is required.");
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.LoanNumber == cmd.LoanNumber, ct) ?? throw new NotFoundException("Loan", cmd.LoanNumber);
        var product = await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct);
        var (interest, principal, accruedPaid) = loan.AllocateRepayment(cmd.Amount);
        var unaccruedInterest = interest - accruedPaid;

        var (sourceGl, sourceSegment, sourceAccount) = cmd.Channel switch
        {
            RepaymentChannel.FosaAccount => (Settings.FosaSavingsControlGl, Segment.Fosa, loan.DisbursementAccountNumber),
            RepaymentChannel.Cash => (Settings.TellerCashGl, Segment.Fosa, (string?)null),
            RepaymentChannel.MPesa => (Settings.MpesaSettlementGl, Segment.Fosa, null),
            RepaymentChannel.AirtelMoney => (Settings.AirtelSettlementGl, Segment.Fosa, null),
            RepaymentChannel.BankTransfer => (Settings.FosaBankGl, Segment.Fosa, null),
            RepaymentChannel.CheckOff => (Settings.BosaBankGl, Segment.Bosa, null),
            _ => throw new DomainRuleException("loans.repayment.channel", $"Channel {cmd.Channel} is not supported."),
        };

        var narrative = cmd.Narrative ?? $"Loan repayment {loan.LoanNumber}";
        var lines = new List<PostingLine> { new(sourceGl, sourceSegment, EntryDirection.Debit, cmd.Amount, sourceAccount, narrative) };
        lines.AddRange(SegmentBridge.Lines(Settings.DueFromFosaGl, Settings.DueToBosaGl, sourceSegment, product.Segment, cmd.Amount, narrative));
        if (accruedPaid > 0) lines.Add(new PostingLine(product.InterestReceivableGlAccountCode, product.Segment, EntryDirection.Credit, accruedPaid, Narrative: "Interest (accrued)"));
        if (unaccruedInterest > 0) lines.Add(new PostingLine(product.InterestIncomeGlAccountCode, product.Segment, EntryDirection.Credit, unaccruedInterest, Narrative: "Interest"));
        if (principal > 0) lines.Add(new PostingLine(product.ControlGlAccountCode, product.Segment, EntryDirection.Credit, principal, loan.LedgerAccountNumber, "Principal"));

        var posted = await ledger.PostAsync(new PostingRequest($"LOAN-REPAY:{cmd.Reference}", narrative, clock.Today, "Lending", cmd.ByUserId, lines), ct);

        var closed = false;
        var snapshot = await ledger.FindAccountAsync(loan.LedgerAccountNumber!, ct);
        if (loan.IsFullyRepaid && snapshot!.Balance == 0)
        {
            await CloseAsync(loan, cmd.ByUserId, ct);
            closed = true;
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.repayment", nameof(Loan), loan.Id.ToString(), cmd.ByUserId, $$"""{"loan":"{{loan.LoanNumber}}","amount":{{cmd.Amount}},"principal":{{principal}},"interest":{{interest}},"channel":"{{cmd.Channel}}"}"""), ct);
        return new RepaymentResult(posted.JournalEntryId, loan.LoanNumber, principal, interest, snapshot!.Balance, closed);
    }

    private async Task CloseAsync(Loan loan, Guid byUser, CancellationToken ct)
    {
        foreach (var g in loan.Guarantors.Where(g => g.Status == GuarantorStatus.Accepted))
            await ledger.ReleaseHoldAsync(g.DepositsAccountNumber, g.AmountGuaranteed, $"Loan {loan.LoanNumber} repaid", ct);
        if (loan.PledgedDepositsAmount > 0 && loan.PledgedDepositsAccountNumber is not null)
            await ledger.ReleaseHoldAsync(loan.PledgedDepositsAccountNumber, loan.PledgedDepositsAmount, $"Loan {loan.LoanNumber} repaid", ct);
        loan.Close(clock.UtcNow);
        await ledger.SetAccountStatusAsync(loan.LedgerAccountNumber!, LedgerAccountStatus.Closed, byUser, ct);
        await audit.RecordAsync(new AuditEvent("loans.closed", nameof(Loan), loan.Id.ToString(), byUser), ct);
    }

    /// <summary>Recognises interest due on or before <paramref name="asOf"/> (Dr interest receivable / Cr interest income), once per instalment. Idempotent.</summary>
    public async Task<int> AccrueInterestAsync(DateOnly asOf, Guid byUser, CancellationToken ct)
    {
        var loans = await db.Loans.Where(l => l.Status == LoanStatus.Active).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var count = 0;
        foreach (var loan in loans)
        {
            var product = products[loan.ProductId];
            foreach (var inst in loan.Schedule.Where(i => !i.InterestAccrued && i.DueDate <= asOf && i.InterestDue - i.InterestPaid > 0).OrderBy(i => i.Number))
            {
                var amount = inst.InterestDue - inst.InterestPaid;
                await ledger.PostAsync(new PostingRequest($"LOAN-ACCR:{loan.LoanNumber}:{inst.Number}", $"Interest due on {loan.LoanNumber} instalment {inst.Number}", inst.DueDate, "Lending", byUser,
                [
                    new PostingLine(product.InterestReceivableGlAccountCode, product.Segment, EntryDirection.Debit, amount, Narrative: "Interest accrual"),
                    new PostingLine(product.InterestIncomeGlAccountCode, product.Segment, EntryDirection.Credit, amount, Narrative: "Interest accrual"),
                ]), ct);
                inst.MarkAccrued();
                count++;
            }
            // Fully-paid-in-advance instalments carry no receivable; mark them accrued so they are never revisited.
            foreach (var inst in loan.Schedule.Where(i => !i.InterestAccrued && i.DueDate <= asOf && i.InterestDue - i.InterestPaid <= 0)) inst.MarkAccrued();
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    public async Task<PortfolioQualitySnapshot> GetPortfolioQualityAsync(DateOnly asOf, CancellationToken ct)
    {
        var report = await provisioning.AgingAsync(asOf, ct);
        var config = await provisioning.GetConfigAsync(ct);
        var buckets = report.Buckets.Select(b => new AgingBucketSnapshot(b.Bucket, config.Buckets.First(x => x.Name == b.Bucket).MinDaysInArrears, b.ProvisionRateBps, b.Loans, b.Outstanding, b.ProvisionRequired)).ToList();
        var byMember = report.Loans.GroupBy(l => l.MemberId).Select(g => new MemberOutstandingSnapshot(g.Key, g.Count(), g.Sum(l => l.OutstandingPrincipal))).OrderByDescending(m => m.Outstanding).ToList();
        return new PortfolioQualitySnapshot(asOf, report.TotalOutstanding, report.NonPerformingOutstanding, report.TotalProvisionRequired, buckets, byMember, config.Source);
    }

    public async Task<MemberLoanExposure> GetMemberExposureAsync(Guid memberId, CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking().Where(l => l.MemberId == memberId && l.Status == LoanStatus.Active).ToListAsync(ct);
        decimal outstanding = 0, arrears = 0; var inArrears = 0;
        foreach (var l in loans)
        {
            outstanding += (await ledger.FindAccountAsync(l.LedgerAccountNumber!, ct))?.Balance ?? 0;
            var a = l.ArrearsAmount(clock.Today);
            if (a > 0) { arrears += a; inArrears++; }
        }
        var guarantees = await db.Loans.AsNoTracking().Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Approved)
            .SelectMany(l => l.Guarantors).Where(g => g.GuarantorMemberId == memberId && g.Status == GuarantorStatus.Accepted).ToListAsync(ct);
        return new MemberLoanExposure(memberId, outstanding, arrears, loans.Count, inArrears, guarantees.Sum(g => g.AmountGuaranteed), guarantees.Count);
    }
}
