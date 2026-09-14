using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Notifications;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Members;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

public sealed record GuarantorExposure(Guid MemberId, decimal BosaDeposits, decimal ActiveGuarantees, int ActiveGuaranteeCount, decimal Capacity, decimal AvailableDeposits);

/// <summary>Origination → guarantors → appraisal → N-of-M approval → disbursement. Eligibility reads BOSA balances through the Savings/Ledger contracts only.</summary>
public sealed class LoanService(LendingDbContext db, ILedgerService ledger, ISavingsService savings, IMemberDirectory members, ITenantContext tenant, IClock clock, IAuditLogger audit, IOptions<LendingSettings> options, INotifier notifier, CreditScoringService scoring)
{
    private LendingSettings Settings => options.Value;

    public async Task<LoanProduct> GetProductAsync(string code, CancellationToken ct)
        => await db.Products.FirstOrDefaultAsync(p => p.Code == code.ToUpperInvariant(), ct) ?? throw new NotFoundException("Loan product", code);

    public async Task<Loan> GetAsync(Guid id, CancellationToken ct)
        => await db.Loans.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Loan", id);

    public async Task<string> NextLoanNumberAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = """
                INSERT INTO lending.loan_number_sequences (tenant_id, next_value) VALUES (@tenant, 2)
                ON CONFLICT (tenant_id) DO UPDATE SET next_value = lending.loan_number_sequences.next_value + 1
                RETURNING next_value - 1
                """;
            var p = cmd.CreateParameter(); p.ParameterName = "tenant"; p.Value = tenant.TenantId; cmd.Parameters.Add(p);
            return $"LN-{Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)):D6}";
        }
        finally { if (wasClosed) await db.Database.CloseConnectionAsync(); }
    }

    public async Task<EligibilitySnapshot> EvaluateEligibilityAsync(Guid memberId, LoanProduct product, CancellationToken ct)
    {
        var member = await members.FindAsync(memberId, ct) ?? throw new NotFoundException("Member", memberId);
        if (member.KycStatus != KycStatus.Verified)
            throw new DomainRuleException("loans.member_not_in_good_standing", $"Member {member.MemberNumber} is {member.KycStatus}.");
        var summary = await savings.GetMemberSummaryAsync(memberId, ct);
        var outstanding = await ActiveOutstandingAsync(memberId, ct);
        var today = clock.Today;
        var membershipMonths = (today.Year - member.JoinedAt.Year) * 12 + today.Month - member.JoinedAt.Month;
        return new EligibilitySnapshot
        {
            BosaDeposits = summary.BosaDeposits,
            Shares = summary.Shares,
            DepositMultiplier = product.DepositMultiplier,
            MaxEligibleAmount = product.DepositMultiplier > 0 ? Math.Min(product.MaxAmount, decimal.Round(summary.BosaDeposits * product.DepositMultiplier, 2)) : product.MaxAmount,
            MembershipMonths = membershipMonths,
            ContributionMonths = summary.MonthsWithContributions,
            ExistingOutstanding = outstanding,
        };
    }

    private async Task<decimal> ActiveOutstandingAsync(Guid memberId, CancellationToken ct)
    {
        var numbers = await db.Loans.AsNoTracking().Where(l => l.MemberId == memberId && l.Status == LoanStatus.Active && l.LedgerAccountNumber != null).Select(l => l.LedgerAccountNumber!).ToListAsync(ct);
        decimal total = 0;
        foreach (var n in numbers) total += (await ledger.FindAccountAsync(n, ct))?.Balance ?? 0;
        return total;
    }

    public async Task<Loan> ApplyAsync(Guid memberId, string productCode, decimal amount, int termMonths, string purpose, string? disbursementAccount, Guid byUser, CancellationToken ct, bool bureauConsent = true)
    {
        if (Settings.CreditBureau.RequireConsent && !bureauConsent)
            throw new DomainRuleException("loans.bureau_consent_required", "The member's consent to a credit bureau check is required before an application can be captured.");
        var product = await GetProductAsync(productCode, ct);
        var eligibility = await EvaluateEligibilityAsync(memberId, product, ct);
        if (await db.Loans.AnyAsync(l => l.MemberId == memberId && l.ProductId == product.Id && (l.Status == LoanStatus.Applied || l.Status == LoanStatus.Appraised || l.Status == LoanStatus.PendingApproval || l.Status == LoanStatus.Approved), ct))
            throw new ConflictException("loans.application_in_progress", $"The member already has a {product.Code} application in progress.");

        var arrears = await db.Loans.AsNoTracking().Where(l => l.MemberId == memberId && l.Status == LoanStatus.Active).ToListAsync(ct);
        if (arrears.Any(l => l.DaysInArrears(clock.Today, 0) > 30))
            throw new DomainRuleException("loans.existing_arrears", "The member has a loan more than 30 days in arrears.");

        var summary = await savings.GetMemberSummaryAsync(memberId, ct);
        var account = disbursementAccount ?? summary.FosaAccountNumber ?? throw new DomainRuleException("loans.no_fosa_account", "The member has no FOSA account to disburse into.");

        var loan = Loan.Apply(Ids.New(), tenant.TenantId, await NextLoanNumberAsync(ct), memberId, product, amount, termMonths, purpose, account, eligibility, byUser, clock.UtcNow);
        if (bureauConsent) loan.RecordBureauConsent(clock.UtcNow, Settings.CreditBureau.ConsentText);
        db.Loans.Add(loan);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.applied", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"loan":"{{loan.LoanNumber}}","amount":{{amount}},"product":"{{product.Code}}"}"""), ct);
        await scoring.ScoreLoanAsync(loan.Id, ScoreStage.Application, byUser, ct);
        await notifier.NotifyAsync(new NotificationRequest("loans.applied", $"Loan {loan.LoanNumber} awaits appraisal",
            $"{product.Code} · KES {amount:N2} over {termMonths} months · {purpose}", $"/loans/{loan.Id}", NotificationAudience.HoldersOf(Permissions.Loans.Appraise), byUser), ct);
        return loan;
    }

    // ---- Guarantors ----

    public async Task<GuarantorExposure> GetGuarantorExposureAsync(Guid memberId, CancellationToken ct)
    {
        var summary = await savings.GetMemberSummaryAsync(memberId, ct);
        var active = await db.Loans.AsNoTracking()
            .Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Approved || l.Status == LoanStatus.PendingApproval)
            .SelectMany(l => l.Guarantors)
            .Where(g => g.GuarantorMemberId == memberId && g.Status == GuarantorStatus.Accepted)
            .ToListAsync(ct);
        var total = active.Sum(g => g.AmountGuaranteed);
        var capacity = decimal.Round(summary.BosaDeposits * Settings.MaxGuaranteeToDepositsRatio, 2);
        var deposits = summary.BosaDepositAccountNumber is null ? null : await ledger.FindAccountAsync(summary.BosaDepositAccountNumber, ct);
        return new GuarantorExposure(memberId, summary.BosaDeposits, total, active.Count, capacity, deposits?.AvailableBalance ?? 0);
    }

    public async Task<Loan> AddGuarantorAsync(Guid loanId, Guid guarantorMemberId, decimal amount, Guid byUser, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        var guarantor = await members.FindAsync(guarantorMemberId, ct) ?? throw new NotFoundException("Member", guarantorMemberId);
        if (guarantor.KycStatus != KycStatus.Verified) throw new DomainRuleException("loans.guarantor.not_in_good_standing", $"Guarantor {guarantor.MemberNumber} is {guarantor.KycStatus}.");
        var summary = await savings.GetMemberSummaryAsync(guarantorMemberId, ct);
        if (summary.BosaDepositAccountNumber is null) throw new DomainRuleException("loans.guarantor.no_deposits", "The guarantor has no BOSA deposits account.");

        var exposure = await GetGuarantorExposureAsync(guarantorMemberId, ct);
        if (exposure.ActiveGuaranteeCount >= Settings.MaxActiveGuaranteesPerMember)
            throw new DomainRuleException("loans.guarantor.too_many", $"Guarantor {guarantor.MemberNumber} already has {exposure.ActiveGuaranteeCount} active guarantees (cap {Settings.MaxActiveGuaranteesPerMember}).");
        if (exposure.ActiveGuarantees + amount > exposure.Capacity)
            throw new DomainRuleException("loans.guarantor.exposure_cap", $"Guarantor {guarantor.MemberNumber} would exceed the exposure cap: {exposure.ActiveGuarantees + amount:N2} guaranteed vs capacity {exposure.Capacity:N2}.");

        var g = loan.AddGuarantor(guarantorMemberId, summary.BosaDepositAccountNumber, amount, clock.UtcNow);
        db.Add(g);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.guarantor.added", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"guarantor":"{{guarantor.MemberNumber}}","amount":{{amount}}}"""), ct);
        return loan;
    }

    /// <summary>Records the guarantor's consent and ring-fences the guaranteed amount on their BOSA deposits (a ledger hold).</summary>
    public async Task<Loan> AcceptGuaranteeAsync(Guid loanId, Guid guarantorId, Guid byUser, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        var g = loan.Guarantors.FirstOrDefault(x => x.Id == guarantorId) ?? throw new NotFoundException("Guarantor", guarantorId);
        await ledger.PlaceHoldAsync(g.DepositsAccountNumber, g.AmountGuaranteed, $"Guarantee for {loan.LoanNumber}", ct); // throws InsufficientFunds if the deposits are already committed
        g.Accept(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.guarantor.accepted", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"guarantorMemberId":"{{g.GuarantorMemberId}}","amount":{{g.AmountGuaranteed}}}"""), ct);
        return loan;
    }

    public async Task<Loan> DeclineGuaranteeAsync(Guid loanId, Guid guarantorId, Guid byUser, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        var g = loan.Guarantors.FirstOrDefault(x => x.Id == guarantorId) ?? throw new NotFoundException("Guarantor", guarantorId);
        g.Decline();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.guarantor.declined", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"guarantorMemberId":"{{g.GuarantorMemberId}}","amount":{{g.AmountGuaranteed}}}"""), ct);
        return loan;
    }

    // ---- Appraisal / approval ----

    public async Task<Loan> AppraiseAsync(Guid loanId, string notes, Guid byUser, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        loan.Appraise(byUser, notes, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.appraised", nameof(Loan), loan.Id.ToString(), byUser), ct);
        await scoring.ScoreLoanAsync(loan.Id, ScoreStage.Appraisal, byUser, ct); // guarantees are in by now
        await notifier.NotifyAsync(new NotificationRequest("loans.pending_approval", $"Loan {loan.LoanNumber} awaits committee approval",
            $"KES {loan.Amount:N2} · appraised: {notes}", $"/loans/{loan.Id}", NotificationAudience.HoldersOf(Permissions.Loans.Approve), byUser), ct);
        return loan;
    }

    public async Task<Loan> ApproveAsync(Guid loanId, string? notes, Guid byUser, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        var product = await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct);
        var summary = await savings.GetMemberSummaryAsync(loan.MemberId, ct);
        var deposits = summary.BosaDepositAccountNumber is null ? null : await ledger.FindAccountAsync(summary.BosaDepositAccountNumber, ct);

        var approval = LoanApproval_Add(loan, byUser, notes, product, deposits?.AvailableBalance ?? 0, out var nowApproved);
        db.Add(approval);
        if (nowApproved && deposits is not null)
        {
            // Pledge the borrower's own deposits (up to the loan amount) for the life of the loan.
            var pledge = Math.Min(deposits.AvailableBalance, loan.Amount);
            if (pledge > 0)
            {
                await ledger.PlaceHoldAsync(deposits.AccountNumber, pledge, $"Pledged for {loan.LoanNumber}", ct);
                loan.PledgeDeposits(deposits.AccountNumber, pledge);
            }
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent(nowApproved ? "loans.approved" : "loans.approval_recorded", nameof(Loan), loan.Id.ToString(), byUser,
            $$"""{"loan":"{{loan.LoanNumber}}","approvals":{{loan.Approvals.Count(a => a.Decision == ApprovalDecision.Approve)}},"required":{{product.ApprovalsRequiredFor(loan.Amount)}}}"""), ct);
        if (nowApproved)
        {
            await notifier.NotifyAsync(new NotificationRequest("loans.approved", $"Loan {loan.LoanNumber} approved",
                $"KES {loan.Amount:N2} is ready for disbursement to {loan.DisbursementAccountNumber}", $"/loans/{loan.Id}", NotificationAudience.User(loan.AppliedByUserId), byUser), ct);
            await notifier.NotifyAsync(new NotificationRequest("loans.ready_to_disburse", $"Loan {loan.LoanNumber} is ready to disburse",
                $"KES {loan.Amount:N2} · {product.Code}", $"/loans/{loan.Id}", NotificationAudience.HoldersOf(Permissions.Loans.Disburse), byUser), ct);
        }
        return loan;
    }

    private static LoanApproval LoanApproval_Add(Loan loan, Guid byUser, string? notes, LoanProduct product, decimal depositsAvailable, out bool nowApproved)
    {
        var before = loan.Approvals.Count;
        nowApproved = loan.Approve(byUser, notes, product, depositsAvailable, DateTimeOffset.UtcNow);
        return loan.Approvals[before];
    }

    public async Task<Loan> RejectAsync(Guid loanId, string reason, Guid byUser, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        var before = loan.Approvals.Count;
        loan.Reject(byUser, reason, clock.UtcNow);
        db.Add(loan.Approvals[before]);
        foreach (var g in loan.Guarantors.Where(g => g.Status == GuarantorStatus.Released && g.ReleasedAt >= clock.UtcNow.AddMinutes(-1)))
            await ledger.ReleaseHoldAsync(g.DepositsAccountNumber, g.AmountGuaranteed, $"Loan {loan.LoanNumber} rejected", ct);
        if (loan.PledgedDepositsAmount > 0 && loan.PledgedDepositsAccountNumber is not null)
            await ledger.ReleaseHoldAsync(loan.PledgedDepositsAccountNumber, loan.PledgedDepositsAmount, $"Loan {loan.LoanNumber} rejected", ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.rejected", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("loans.rejected", $"Loan {loan.LoanNumber} rejected", reason, $"/loans/{loan.Id}", NotificationAudience.User(loan.AppliedByUserId), byUser), ct);
        return loan;
    }

    // ---- Disbursement ----

    /// <summary>Opens the loan sub-ledger account, credits the member's FOSA account net of the processing fee, and generates the schedule. <paramref name="valueDate"/> is for seeding/migration only.</summary>
    public async Task<Loan> DisburseAsync(Guid loanId, Guid byUser, DateOnly? valueDate, CancellationToken ct)
    {
        var loan = await GetAsync(loanId, ct);
        var product = await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct);
        var date = valueDate ?? clock.Today;
        var ledgerNumber = loan.LoanNumber;

        loan.Disburse(byUser, ledgerNumber, date, clock.UtcNow);
        db.AddRange(loan.Schedule);

        await ledger.OpenAccountAsync(new OpenLedgerAccountRequest(ledgerNumber, loan.MemberId, product.ControlGlAccountCode, product.Segment, LedgerAccountKind.Loan, product.Code, byUser), ct);

        var net = loan.Amount - loan.ProcessingFee;
        var lines = new List<PostingLine> { new(product.ControlGlAccountCode, product.Segment, EntryDirection.Debit, loan.Amount, ledgerNumber, "Loan disbursement") };
        if (loan.ProcessingFee > 0) lines.Add(new PostingLine(product.FeeIncomeGlAccountCode, product.Segment, EntryDirection.Credit, loan.ProcessingFee, Narrative: "Processing fee"));
        lines.AddRange(SegmentBridge.Lines(Settings.DueFromFosaGl, Settings.DueToBosaGl, product.Segment, Segment.Fosa, net, "Disbursement to FOSA account"));
        lines.Add(new PostingLine(Settings.FosaSavingsControlGl, Segment.Fosa, EntryDirection.Credit, net, loan.DisbursementAccountNumber, $"Loan {loan.LoanNumber} disbursed"));
        await ledger.PostAsync(new PostingRequest($"LOAN-DISB:{loan.LoanNumber}", $"Disbursement of {loan.LoanNumber} ({product.Code})", date, "Lending", byUser, lines), ct);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.disbursed", nameof(Loan), loan.Id.ToString(), byUser, $$"""{"loan":"{{loan.LoanNumber}}","amount":{{loan.Amount}},"fee":{{loan.ProcessingFee}},"account":"{{loan.DisbursementAccountNumber}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("loans.disbursed", $"Loan {loan.LoanNumber} disbursed",
            $"KES {net:N2} credited to {loan.DisbursementAccountNumber} (fee KES {loan.ProcessingFee:N2})", $"/loans/{loan.Id}", NotificationAudience.User(loan.AppliedByUserId), byUser), ct);
        return loan;
    }
}
