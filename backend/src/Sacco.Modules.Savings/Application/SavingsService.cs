using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Notifications;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Members;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Savings.Application;

public sealed class SavingsService(SavingsDbContext db, ILedgerService ledger, IMemberDirectory members, ITenantContext tenant, IClock clock, IAuditLogger audit, IOptions<SavingsSettings> options, INotifier notifier, FeeMatrixService fees) : ISavingsService
{
    private SavingsSettings Settings => options.Value;

    // ---------- Accounts ----------

    public async Task<SavingsProduct> GetProductAsync(string code, CancellationToken ct)
        => await db.Products.FirstOrDefaultAsync(p => p.Code == code.ToUpperInvariant(), ct) ?? throw new NotFoundException("Savings product", code);

    public async Task<SavingsAccount> GetAccountAsync(string accountNumber, CancellationToken ct)
        => await db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, ct) ?? throw new NotFoundException("Savings account", accountNumber);

    public async Task<SavingsAccount> OpenAccountAsync(Guid memberId, string productCode, Guid byUser, CancellationToken ct)
    {
        var product = await GetProductAsync(productCode, ct);
        if (product.Kind == ProductKind.FixedDeposit)
            throw new DomainRuleException("savings.fd.use_fixed_deposit_endpoint", "Fixed deposits are opened with a principal and a funding account.");
        if (!product.IsActive) throw new DomainRuleException("savings.product.inactive", $"{product.Code} is not active.");
        var member = await RequireGoodStanding(memberId, ct);
        if (await db.Accounts.AnyAsync(a => a.MemberId == memberId && a.ProductId == product.Id && a.Status == SavingsAccountStatus.Active, ct))
            throw new ConflictException("savings.account.exists", $"{member.MemberNumber} already has an active {product.Code} account.");

        var number = $"{member.MemberNumber}-{product.AccountSuffix}";
        var account = SavingsAccount.Open(Ids.New(), tenant.TenantId, number, memberId, product, byUser, clock.UtcNow);
        db.Accounts.Add(account);
        await ledger.OpenAccountAsync(new OpenLedgerAccountRequest(number, memberId, product.ControlGlAccountCode, product.Segment, ToLedgerKind(product.Kind), product.Code, byUser), ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.account.opened", nameof(SavingsAccount), account.Id.ToString(), byUser, $$"""{"account":"{{number}}","product":"{{product.Code}}"}"""), ct);
        return account;
    }

    public async Task<SavingsAccount> OpenFixedDepositAsync(Guid memberId, string productCode, decimal principal, string fromAccountNumber, Guid byUser, CancellationToken ct)
    {
        var product = await GetProductAsync(productCode, ct);
        var member = await RequireGoodStanding(memberId, ct);
        var source = await GetAccountAsync(fromAccountNumber, ct);
        if (source.MemberId != memberId) throw new DomainRuleException("savings.fd.source_not_owned", "The funding account belongs to a different member.");
        if (source.Kind != ProductKind.FosaCurrent) throw new DomainRuleException("savings.fd.source_not_fosa", "Fixed deposits are funded from the member's FOSA account.");
        var sourceProduct = await db.Products.FirstAsync(p => p.Id == source.ProductId, ct);

        var count = await db.Accounts.CountAsync(a => a.MemberId == memberId && a.Kind == ProductKind.FixedDeposit, ct);
        var number = $"{member.MemberNumber}-{product.AccountSuffix}{count + 1}";
        var fd = SavingsAccount.OpenFixedDeposit(Ids.New(), tenant.TenantId, number, memberId, product, principal, fromAccountNumber, byUser, clock.UtcNow, clock.Today);
        db.Accounts.Add(fd);
        await ledger.OpenAccountAsync(new OpenLedgerAccountRequest(number, memberId, product.ControlGlAccountCode, product.Segment, LedgerAccountKind.FixedDeposit, product.Code, byUser), ct);

        // FOSA → BOSA transfer through the clearing pair.
        var lines = new List<PostingLine> { new(sourceProduct.ControlGlAccountCode, source.Segment, EntryDirection.Debit, principal, source.AccountNumber, "Fixed deposit placement") };
        lines.AddRange(PostingBuilder.Bridge(Settings, source.Segment, product.Segment, principal, "Fixed deposit placement"));
        lines.Add(new PostingLine(product.ControlGlAccountCode, product.Segment, EntryDirection.Credit, principal, number, "Fixed deposit placement"));
        await ledger.PostAsync(new PostingRequest($"FD-OPEN:{number}", $"Fixed deposit {number} placed from {fromAccountNumber}", clock.Today, "Savings", byUser, lines), ct);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.fd.opened", nameof(SavingsAccount), fd.Id.ToString(), byUser, $$"""{"account":"{{number}}","principal":{{principal}},"maturity":"{{fd.MaturityDate:yyyy-MM-dd}}"}"""), ct);
        return fd;
    }

    /// <summary>Pays principal + interest into the payout account and closes the FD. <paramref name="force"/> ignores the maturity date (demo/testing only; the host gates it).</summary>
    public async Task<SavingsAccount> MatureFixedDepositAsync(string accountNumber, Guid byUser, bool force, CancellationToken ct)
    {
        var fd = await GetAccountAsync(accountNumber, ct);
        var product = await db.Products.FirstAsync(p => p.Id == fd.ProductId, ct);
        fd.Mature(force ? (fd.MaturityDate ?? clock.Today) : clock.Today);
        var payout = await GetAccountAsync(fd.PayoutAccountNumber!, ct);
        var payoutProduct = await db.Products.FirstAsync(p => p.Id == payout.ProductId, ct);
        var interest = fd.MaturityInterest();
        var principal = fd.Principal!.Value;

        var lines = new List<PostingLine>();
        if (interest > 0)
        {
            lines.Add(new PostingLine(product.InterestExpenseGlAccountCode!, product.Segment, EntryDirection.Debit, interest, Narrative: "Fixed deposit interest"));
            lines.Add(new PostingLine(product.ControlGlAccountCode, product.Segment, EntryDirection.Credit, interest, accountNumber, "Fixed deposit interest"));
        }
        var total = principal + interest;
        lines.Add(new PostingLine(product.ControlGlAccountCode, product.Segment, EntryDirection.Debit, total, accountNumber, "Fixed deposit maturity"));
        lines.AddRange(PostingBuilder.Bridge(Settings, product.Segment, payout.Segment, total, "Fixed deposit maturity"));
        lines.Add(new PostingLine(payoutProduct.ControlGlAccountCode, payout.Segment, EntryDirection.Credit, total, payout.AccountNumber, "Fixed deposit maturity"));
        await ledger.PostAsync(new PostingRequest($"FD-MATURE:{accountNumber}", $"Fixed deposit {accountNumber} matured: principal {principal:N2} + interest {interest:N2}", clock.Today, "Savings", byUser, lines), ct);

        await ledger.SetAccountStatusAsync(accountNumber, LedgerAccountStatus.Closed, byUser, ct);
        fd.Close(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.fd.matured", nameof(SavingsAccount), fd.Id.ToString(), byUser, $$"""{"account":"{{accountNumber}}","interest":{{interest}},"forced":{{force.ToString().ToLower()}}}"""), ct);
        return fd;
    }

    // ---------- Deposits ----------

    public async Task<DepositResult> DepositAsync(DepositCommand cmd, CancellationToken ct)
    {
        if (cmd.Amount <= 0 || decimal.Round(cmd.Amount, 2) != cmd.Amount)
            throw new DomainRuleException("savings.deposit.amount_invalid", "Deposit amount must be positive with at most two decimals.");
        if (string.IsNullOrWhiteSpace(cmd.Reference))
            throw new DomainRuleException("savings.deposit.reference_required", "A receipt/transaction reference is required (it is the idempotency key).");
        var account = await GetAccountAsync(cmd.AccountNumber, ct);
        if (account.Status != SavingsAccountStatus.Active) throw new DomainRuleException("savings.account.not_active", $"{account.AccountNumber} is {account.Status}.");
        var product = await db.Products.FirstAsync(p => p.Id == account.ProductId, ct);
        if (product.Kind == ProductKind.FixedDeposit) throw new DomainRuleException("savings.fd.no_top_up", "Fixed deposits cannot be topped up.");

        var (settlementGl, settlementSegment) = Settings.SettlementFor(cmd.Channel);
        var narrative = cmd.Narrative ?? $"{cmd.Channel} deposit";
        var lines = PostingBuilder.Inflow(Settings, settlementGl, settlementSegment, product.ControlGlAccountCode, account.AccountNumber, account.Segment, cmd.Amount, narrative);

        // Deposit fee (fee matrix, ADR 0015): the full amount lands, then the fee comes off in the same journal, so the
        // statement shows both. Internal transfers (loan disbursements, dividends) are never charged.
        var fee = FeeMatrixService.ChannelFor(cmd.Channel) is { } feeChannel
            ? await fees.QuoteAsync(FeeTransactionType.Deposit, feeChannel, product, cmd.Amount, ct)
            : FeeCharge.None(account.Segment);
        if (fee.Amount >= cmd.Amount)
            throw new DomainRuleException("savings.fees.exceeds_deposit", $"The deposit fee ({fee.Amount:N2}) would consume the whole deposit.");
        if (fee.Amount > 0)
        {
            lines.Add(new PostingLine(product.ControlGlAccountCode, account.Segment, EntryDirection.Debit, fee.Amount, account.AccountNumber, "Deposit fee"));
            lines.AddRange(PostingBuilder.Fee(Settings, account.Segment, fee.Amount, fee.GlAccountCode!, fee.Segment, "Deposit fee"));
        }

        var reference = $"DEP:{cmd.Reference}";
        var posted = await ledger.PostAsync(new PostingRequest(reference, $"{narrative} — {account.AccountNumber}", clock.Today, "Savings", cmd.ByUserId, lines), ct);
        var snapshot = await ledger.FindAccountAsync(account.AccountNumber, ct);
        await audit.RecordAsync(new AuditEvent("savings.deposit", nameof(SavingsAccount), account.AccountNumber, cmd.ByUserId,
            AuditDetails.New().With("amount", cmd.Amount).With("channel", cmd.Channel.ToString()).With("reference", cmd.Reference).With("fee", fee.Amount).With("journal", posted.JournalEntryId).ToJson()), ct);
        return new DepositResult(posted.JournalEntryId, cmd.Reference, account.AccountNumber, cmd.Amount, snapshot!.Balance, fee.Amount);
    }

    // ---------- Withdrawals ----------

    public async Task<WithdrawalRequest> RequestWithdrawalAsync(string accountNumber, decimal amount, PayoutChannel channel, string? destination, string? narrative, Guid byUser, CancellationToken ct)
    {
        var account = await GetAccountAsync(accountNumber, ct);
        var product = await db.Products.FirstAsync(p => p.Id == account.ProductId, ct);
        if (product.Kind == ProductKind.Shares)
            throw new DomainRuleException("savings.shares.no_direct_withdrawal", "Share capital cannot be withdrawn directly — transfer or sell the shares to another member instead.");
        var fee = await fees.QuoteAsync(FeeTransactionType.Withdrawal, FeeMatrixService.ChannelFor(channel), product, amount, ct);
        var request = WithdrawalRequest.Create(Ids.New(), tenant.TenantId, account, product, amount, fee, channel, destination, narrative, byUser, clock.UtcNow, clock.Today);

        var snapshot = await ledger.FindAccountAsync(accountNumber, ct) ?? throw new NotFoundException("Ledger account", accountNumber);
        if (snapshot.AvailableBalance - request.TotalDebit < product.MinimumBalance)
            throw new InsufficientFundsException(accountNumber, request.TotalDebit);

        if (request.QualifiesForTellerPayout(product))
        {
            request.ApproveByPolicy(clock.UtcNow);
            db.Withdrawals.Add(request);
            await PostPayoutAsync(request, account, product, byUser, ct);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("savings.withdrawal.paid_by_teller", nameof(WithdrawalRequest), request.Id.ToString(), byUser, $$"""{"account":"{{accountNumber}}","amount":{{amount}}}"""), ct);
            return request;
        }

        // Ring-fence the funds for the life of the request.
        await ledger.PlaceHoldAsync(accountNumber, request.TotalDebit, $"Withdrawal request {request.Id}", ct);
        db.Withdrawals.Add(request);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.withdrawal.requested", nameof(WithdrawalRequest), request.Id.ToString(), byUser, $$"""{"account":"{{accountNumber}}","amount":{{amount}},"channel":"{{channel}}","noticeExpires":"{{request.NoticeExpiresOn:yyyy-MM-dd}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.withdrawal.pending", $"Withdrawal of KES {amount:N2} from {accountNumber} awaits approval",
            $"{channel} payout · notice period ends {request.NoticeExpiresOn:d MMM yyyy}", "/savings/withdrawals", NotificationAudience.HoldersOf(Permissions.Savings.WithdrawalApprove), byUser), ct);
        return request;
    }

    public async Task<WithdrawalRequest> ApproveWithdrawalAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var request = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Withdrawal", id);
        request.Approve(byUser, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.withdrawal.approved", nameof(WithdrawalRequest), id.ToString(), byUser, $$"""{"requestedBy":"{{request.RequestedByUserId}}","amount":{{request.Amount}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.withdrawal.approved", $"Withdrawal from {request.AccountNumber} approved",
            $"KES {request.Amount:N2} via {request.Channel}; payable from {request.NoticeExpiresOn:d MMM yyyy}", "/savings/withdrawals", NotificationAudience.User(request.RequestedByUserId), byUser), ct);
        return request;
    }

    public async Task<WithdrawalRequest> RejectWithdrawalAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var request = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Withdrawal", id);
        request.Reject(byUser, reason);
        await ledger.ReleaseHoldAsync(request.AccountNumber, request.TotalDebit, $"Withdrawal {id} rejected", ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.withdrawal.rejected", nameof(WithdrawalRequest), id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.withdrawal.rejected", $"Withdrawal from {request.AccountNumber} rejected",
            reason, "/savings/withdrawals", NotificationAudience.User(request.RequestedByUserId), byUser), ct);
        return request;
    }

    /// <summary>Teller pays an approved cash/bank withdrawal once any notice period has passed. Mobile-money payouts are dispatched by the Payments module.</summary>
    public async Task<WithdrawalRequest> PayWithdrawalAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var request = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Withdrawal", id);
        if (request.Channel is PayoutChannel.MPesa or PayoutChannel.AirtelMoney)
            throw new DomainRuleException("savings.withdrawal.mobile_money_via_payments", "Mobile money payouts are executed by the payments module, not paid at the counter.");
        var account = await GetAccountAsync(request.AccountNumber, ct);
        var product = await db.Products.FirstAsync(p => p.Id == account.ProductId, ct);

        // Validate state before touching the ledger (MarkPaid runs the notice check).
        var probe = request.Status == WithdrawalStatus.Approved && clock.Today >= request.NoticeExpiresOn;
        if (!probe) request.MarkPaid(byUser, "probe", clock.UtcNow, clock.Today); // throws with the right code

        await ledger.ReleaseHoldAsync(request.AccountNumber, request.TotalDebit, $"Withdrawal {id} payout", ct);
        try
        {
            await PostPayoutAsync(request, account, product, byUser, ct);
        }
        catch
        {
            await ledger.PlaceHoldAsync(request.AccountNumber, request.TotalDebit, $"Withdrawal {id} payout failed; hold restored", ct);
            throw;
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.withdrawal.paid", nameof(WithdrawalRequest), id.ToString(), byUser, $$"""{"account":"{{request.AccountNumber}}","amount":{{request.Amount}},"channel":"{{request.Channel}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.withdrawal.paid", $"Withdrawal from {request.AccountNumber} paid",
            $"KES {request.Amount:N2} paid out via {request.Channel}", "/savings/withdrawals", NotificationAudience.User(request.RequestedByUserId), byUser), ct);
        return request;
    }

    private async Task PostPayoutAsync(WithdrawalRequest request, SavingsAccount account, SavingsProduct product, Guid byUser, CancellationToken ct)
    {
        var (settlementGl, settlementSegment) = request.Channel switch
        {
            PayoutChannel.Cash => (Settings.TellerCashGl, Segment.Fosa),
            PayoutChannel.BankTransfer => (Settings.FosaBankGl, Segment.Fosa),
            PayoutChannel.MPesa => (Settings.MpesaSettlementGl, Segment.Fosa),
            PayoutChannel.AirtelMoney => (Settings.AirtelSettlementGl, Segment.Fosa),
            _ => throw new DomainRuleException("savings.channel_unsupported", $"Channel {request.Channel} is not supported."),
        };
        var narrative = request.Narrative ?? $"{request.Channel} withdrawal";
        var lines = PostingBuilder.Outflow(Settings, settlementGl, settlementSegment, product.ControlGlAccountCode, account.AccountNumber, account.Segment, request.Amount, request.Fee, request.FeeGlAccountCode ?? product.FeeIncomeGlAccountCode, request.FeeSegment ?? account.Segment, narrative);
        var reference = $"WDR:{request.Id:N}";
        await ledger.PostAsync(new PostingRequest(reference, $"{narrative} — {account.AccountNumber}", clock.Today, "Savings", byUser, lines), ct);
        request.MarkPaid(byUser, reference, clock.UtcNow, clock.Today);
    }

    // ---------- Provider payouts (Payments module) ----------

    public async Task<WithdrawalPayoutInfo?> GetWithdrawalForPayoutAsync(Guid withdrawalId, CancellationToken ct)
    {
        var w = await db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == withdrawalId, ct);
        return w is null ? null : new WithdrawalPayoutInfo(w.Id, w.AccountNumber, w.MemberId, w.Amount, w.Fee, w.Channel.ToString(), w.PayoutDestination, w.Status == WithdrawalStatus.Approved, w.Status == WithdrawalStatus.Paid);
    }

    public async Task CompleteWithdrawalPayoutAsync(Guid withdrawalId, string providerReference, Guid byUserId, CancellationToken ct)
    {
        var request = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct) ?? throw new NotFoundException("Withdrawal", withdrawalId);
        if (request.Status == WithdrawalStatus.Paid) return; // idempotent replay
        if (request.Channel is not (PayoutChannel.MPesa or PayoutChannel.AirtelMoney or PayoutChannel.BankTransfer))
            throw new DomainRuleException("savings.withdrawal.not_provider_channel", "Only mobile money / bank withdrawals are paid through a provider.");
        var account = await GetAccountAsync(request.AccountNumber, ct);
        var product = await db.Products.FirstAsync(p => p.Id == account.ProductId, ct);
        if (request.Status != WithdrawalStatus.Approved || clock.Today < request.NoticeExpiresOn)
            request.MarkPaid(byUserId, "probe", clock.UtcNow, clock.Today); // throws the precise rule
        await ledger.ReleaseHoldAsync(request.AccountNumber, request.TotalDebit, $"Withdrawal {withdrawalId} provider payout", ct);
        try
        {
            await PostPayoutAsync(request, account, product, byUserId, ct);
        }
        catch
        {
            await ledger.PlaceHoldAsync(request.AccountNumber, request.TotalDebit, $"Withdrawal {withdrawalId} payout failed; hold restored", ct);
            throw;
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.withdrawal.paid_by_provider", nameof(WithdrawalRequest), withdrawalId.ToString(), byUserId, $$"""{"channel":"{{request.Channel}}","providerReference":"{{providerReference}}","amount":{{request.Amount}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.withdrawal.paid", $"Withdrawal from {request.AccountNumber} paid by {request.Channel}",
            $"KES {request.Amount:N2} · provider reference {providerReference}", "/savings/withdrawals", NotificationAudience.Users(request.RequestedByUserId, request.ApprovedByUserId ?? Guid.Empty), byUserId), ct);
    }

    public async Task FailWithdrawalPayoutAsync(Guid withdrawalId, string reason, CancellationToken ct)
    {
        var request = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct) ?? throw new NotFoundException("Withdrawal", withdrawalId);
        await audit.RecordAsync(new AuditEvent("savings.withdrawal.payout_failed", nameof(WithdrawalRequest), withdrawalId.ToString(), request.ApprovedByUserId ?? request.RequestedByUserId, "{\"reason\":\"" + reason.Replace("\"", "'") + "\",\"status\":\"" + request.Status + "\"}"), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.withdrawal.payout_failed", $"Payout for withdrawal from {request.AccountNumber} failed",
            reason, "/savings/withdrawals", NotificationAudience.Users(request.RequestedByUserId, request.ApprovedByUserId ?? Guid.Empty), SystemActors.System), ct);
    }

    // ---------- Member exit ----------

    public async Task<ExitPayoutResult> CloseAccountsOnExitAsync(Guid memberId, ExitPayoutChannel channel, Guid byUserId, CancellationToken ct)
    {
        var (settlementGl, settlementSegment) = channel == ExitPayoutChannel.Cash ? (Settings.TellerCashGl, Segment.Fosa) : (Settings.FosaBankGl, Segment.Fosa);
        var accounts = await db.Accounts.Where(a => a.MemberId == memberId && a.Status != SavingsAccountStatus.Closed).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var payouts = new List<ExitAccountPayout>();
        foreach (var account in accounts)
        {
            var snapshot = await ledger.FindAccountAsync(account.AccountNumber, ct);
            if (snapshot is not null && snapshot.HeldAmount > 0)
                throw new DomainRuleException("savings.exit.holds_outstanding", $"{account.AccountNumber} still has KES {snapshot.HeldAmount:N2} on hold (guarantees or pending withdrawals); release them first.");
            string? reference = null;
            var balance = snapshot?.Balance ?? 0m;
            if (balance > 0)
            {
                var product = products[account.ProductId];
                reference = $"EXIT:{account.AccountNumber}";
                var lines = PostingBuilder.Outflow(Settings, settlementGl, settlementSegment, product.ControlGlAccountCode, account.AccountNumber, account.Segment, balance, 0m, null, account.Segment, $"Exit payout {account.AccountNumber}");
                await ledger.PostAsync(new PostingRequest(reference, $"Member exit payout — {account.AccountNumber}", clock.Today, "Savings", byUserId, lines), ct);
            }
            if (snapshot is not null && snapshot.Status != LedgerAccountStatus.Closed)
                await ledger.SetAccountStatusAsync(account.AccountNumber, LedgerAccountStatus.Closed, byUserId, ct);
            account.Close(clock.UtcNow);
            payouts.Add(new ExitAccountPayout(account.AccountNumber, account.Kind, balance, reference));
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.accounts.closed_on_exit", nameof(SavingsAccount), memberId.ToString(), byUserId, $$"""{"accounts":{{payouts.Count}},"paid":{{payouts.Sum(p => p.Amount)}},"channel":"{{channel}}"}"""), ct);
        return new ExitPayoutResult(payouts, payouts.Sum(p => p.Amount));
    }

    // ---------- Queries for other modules ----------

    public async Task<MemberSavingsSummary> GetMemberSummaryAsync(Guid memberId, CancellationToken ct)
    {
        var accounts = await ledger.GetMemberAccountsAsync(memberId, ct);
        var deposits = accounts.Where(a => a.Kind == LedgerAccountKind.Savings && a.Status != LedgerAccountStatus.Closed).ToList();
        var contributionMonths = 0;
        DateOnly? first = null;
        // Use the ledger statement of the primary BOSA deposit account to count contribution months.
        var primary = deposits.FirstOrDefault();
        if (primary is not null)
        {
            var (months, firstDate) = await ContributionHistoryAsync(primary.AccountNumber, ct);
            contributionMonths = months; first = firstDate;
        }
        return new MemberSavingsSummary(memberId,
            deposits.Sum(a => a.Balance),
            accounts.Where(a => a.Kind == LedgerAccountKind.Shares).Sum(a => a.Balance),
            accounts.Where(a => a.Kind == LedgerAccountKind.Current).Sum(a => a.Balance),
            accounts.Where(a => a.Kind == LedgerAccountKind.FixedDeposit).Sum(a => a.Balance),
            contributionMonths, first,
            accounts.FirstOrDefault(a => a.Kind == LedgerAccountKind.Current && a.Status == LedgerAccountStatus.Active)?.AccountNumber,
            primary?.AccountNumber,
            accounts.FirstOrDefault(a => a.Kind == LedgerAccountKind.Shares)?.AccountNumber);
    }

    private async Task<(int Months, DateOnly? First)> ContributionHistoryAsync(string accountNumber, CancellationToken ct)
    {
        var statement = await ledger.GetStatementAsync(accountNumber, new DateOnly(2000, 1, 1), clock.Today, ct);
        if (statement is null) return (0, null);
        var credits = statement.Lines.Where(l => l.Direction == EntryDirection.Credit).ToList();
        return (credits.Select(l => (l.ValueDate.Year, l.ValueDate.Month)).Distinct().Count(), credits.Count == 0 ? null : credits.Min(l => l.ValueDate));
    }

    private async Task<MemberSummary> RequireGoodStanding(Guid memberId, CancellationToken ct)
    {
        var member = await members.FindAsync(memberId, ct) ?? throw new NotFoundException("Member", memberId);
        if (member.KycStatus != KycStatus.Verified)
            throw new DomainRuleException("savings.member_not_in_good_standing", $"Member {member.MemberNumber} is {member.KycStatus}; only KYC-verified members in good standing can hold accounts.");
        return member;
    }

    private static LedgerAccountKind ToLedgerKind(ProductKind kind) => kind switch
    {
        ProductKind.FosaCurrent => LedgerAccountKind.Current,
        ProductKind.BosaDeposit => LedgerAccountKind.Savings,
        ProductKind.Shares => LedgerAccountKind.Shares,
        ProductKind.FixedDeposit => LedgerAccountKind.FixedDeposit,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
