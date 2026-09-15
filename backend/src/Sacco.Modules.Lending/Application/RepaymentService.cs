using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

/// <summary>Repayments, interest accrual, closure and exposure queries. Implements the module's public contract.</summary>
public sealed class RepaymentService(LendingDbContext db, ILedgerService ledger, ISavingsService savings, IClock clock, IAuditLogger audit, IOptions<LendingSettings> options, ProvisioningService provisioning) : ILendingService
{
    private LendingSettings Settings => options.Value;

    public async Task<RepaymentResult> RepayAsync(RepaymentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reference)) throw new DomainRuleException("loans.repayment.reference_required", "A receipt/transaction reference is required.");
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.LoanNumber == cmd.LoanNumber, ct) ?? throw new NotFoundException("Loan", cmd.LoanNumber);
        var product = await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct);
        var paidOn = cmd.PaidOn ?? clock.Today;
        var (interest, principal, accruedPaid) = loan.AllocateRepayment(cmd.Amount, new DateTimeOffset(paidOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var unaccruedInterest = interest - accruedPaid;

        var (sourceGl, sourceSegment, sourceAccount) = cmd.Channel switch
        {
            RepaymentChannel.DepositsOffset => (Settings.BosaDepositsControlGl, Segment.Bosa, (await savings.GetMemberSummaryAsync(loan.MemberId, ct)).BosaDepositAccountNumber ?? throw new DomainRuleException("loans.repayment.no_deposits_account", "The member has no BOSA deposits account to offset against.")),
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

        var posted = await ledger.PostAsync(new PostingRequest($"LOAN-REPAY:{cmd.Reference}", narrative, paidOn, "Lending", cmd.ByUserId, lines), ct);

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

    public async Task<IReadOnlyList<MemberLoanSnapshot>> GetMemberLoansAsync(Guid memberId, CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking().Where(l => l.MemberId == memberId).OrderByDescending(l => l.AppliedAt).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var result = new List<MemberLoanSnapshot>();
        foreach (var l in loans)
        {
            var outstanding = l.LedgerAccountNumber is null ? 0m : (await ledger.FindAccountAsync(l.LedgerAccountNumber, ct))?.Balance ?? 0m;
            var next = l.Schedule.Where(i => i.Status != InstallmentStatus.Paid).OrderBy(i => i.DueDate).FirstOrDefault();
            var active = l.Status == LoanStatus.Active;
            result.Add(new MemberLoanSnapshot(l.Id, l.LoanNumber, l.ProductCode, l.Amount, l.Status, outstanding, active ? l.ArrearsAmount(clock.Today) : 0m, active ? l.DaysInArrears(clock.Today, products[l.ProductId].GracePeriodDays) : 0,
                next?.DueDate, next?.Outstanding ?? 0m, l.DisbursementDate, l.TermMonths, l.InterestRateBps, l.AppliedAt));
        }
        return result;
    }

    public async Task<ExitSettlementResult> SettleOnExitAsync(Guid memberId, Guid byUserId, CancellationToken ct)
    {
        var exposure = await GetMemberExposureAsync(memberId, ct);
        if (exposure.ActiveGuarantees > 0)
            throw new DomainRuleException("loans.exit.active_guarantees", $"The member still guarantees {exposure.ActiveGuarantees} active loan(s) (KES {exposure.ActiveGuaranteesAmount:N2}); those must be released or substituted first.");
        var loans = await db.Loans.Where(l => l.MemberId == memberId && l.Status == LoanStatus.Active).ToListAsync(ct);
        if (loans.Count == 0) return new ExitSettlementResult([], 0m);

        var summary = await savings.GetMemberSummaryAsync(memberId, ct);
        var depositsAccount = summary.BosaDepositAccountNumber ?? throw new DomainRuleException("loans.exit.no_deposits_account", "The member has no BOSA deposits account to settle from.");
        var today = clock.Today;
        var now = clock.UtcNow;

        // The borrower's own pledge is a hold on the very account we settle from: release it first so the funds count as available.
        foreach (var loan in loans.Where(l => l.PledgedDepositsAmount > 0 && l.PledgedDepositsAccountNumber is not null))
        {
            await ledger.ReleaseHoldAsync(loan.PledgedDepositsAccountNumber!, loan.PledgedDepositsAmount, $"Loan {loan.LoanNumber} exit settlement", ct);
            loan.ClearPledge();
        }
        var payoffs = new List<(Loan Loan, decimal Principal, decimal InterestDue)>();
        foreach (var loan in loans)
        {
            var principal = (await ledger.FindAccountAsync(loan.LedgerAccountNumber!, ct))?.Balance ?? 0m;
            var interestDue = loan.Schedule.Where(i => i.DueDate <= today).Sum(i => i.InterestDue - i.InterestPaid);
            payoffs.Add((loan, principal, Math.Max(0, interestDue)));
        }
        var total = payoffs.Sum(p => p.Principal + p.InterestDue);
        var available = (await ledger.FindAccountAsync(depositsAccount, ct))?.AvailableBalance ?? 0m;
        if (available < total)
            throw new DomainRuleException("loans.exit.insufficient_deposits", $"BOSA deposits available (KES {available:N2}) do not cover the payoff of KES {total:N2}; the member must repay the shortfall before exiting.");

        var settled = new List<ExitLoanSettlement>();
        foreach (var (loan, principal, _) in payoffs)
        {
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == loan.ProductId, ct);
            var (interest, principalPaid, accruedPaid) = loan.Payoff(today, now);
            var amount = interest + principalPaid;
            var narrative = $"Exit settlement of {loan.LoanNumber} from BOSA deposits";
            var lines = new List<PostingLine> { new(Settings.BosaDepositsControlGl, Segment.Bosa, EntryDirection.Debit, amount, depositsAccount, narrative) };
            lines.AddRange(SegmentBridge.Lines(Settings.DueFromFosaGl, Settings.DueToBosaGl, Segment.Bosa, product.Segment, amount, narrative));
            if (accruedPaid > 0) lines.Add(new PostingLine(product.InterestReceivableGlAccountCode, product.Segment, EntryDirection.Credit, accruedPaid, Narrative: "Interest (accrued)"));
            if (interest - accruedPaid > 0) lines.Add(new PostingLine(product.InterestIncomeGlAccountCode, product.Segment, EntryDirection.Credit, interest - accruedPaid, Narrative: "Interest"));
            if (principalPaid > 0) lines.Add(new PostingLine(product.ControlGlAccountCode, product.Segment, EntryDirection.Credit, principalPaid, loan.LedgerAccountNumber, "Principal"));
            var posted = await ledger.PostAsync(new PostingRequest($"LOAN-EXIT:{loan.LoanNumber}", narrative, today, "Lending", byUserId, lines), ct);
            await CloseAsync(loan, byUserId, ct);
            settled.Add(new ExitLoanSettlement(loan.LoanNumber, principalPaid, interest, posted.JournalEntryId));
            await audit.RecordAsync(new AuditEvent("loans.exit_settled", nameof(Loan), loan.Id.ToString(), byUserId, $$"""{"loan":"{{loan.LoanNumber}}","principal":{{principalPaid}},"interest":{{interest}},"from":"{{depositsAccount}}"}"""), ct);
        }
        await db.SaveChangesAsync(ct);
        return new ExitSettlementResult(settled, settled.Sum(s => s.Principal + s.Interest));
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
        // Any accepted guarantee on a loan that is not yet closed holds the guarantor's deposits, including loans still awaiting approval.
        var guarantees = await db.Loans.AsNoTracking().Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Approved || l.Status == LoanStatus.PendingApproval || l.Status == LoanStatus.Appraised || l.Status == LoanStatus.Applied)
            .SelectMany(l => l.Guarantors).Where(g => g.GuarantorMemberId == memberId && g.Status == GuarantorStatus.Accepted).ToListAsync(ct);
        return new MemberLoanExposure(memberId, outstanding, arrears, loans.Count, inArrears, guarantees.Sum(g => g.AmountGuaranteed), guarantees.Count);
    }
}
