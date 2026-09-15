using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Sacco.Modules.Payments.Application;
using Sacco.Modules.Payments.Domain;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Payments.Providers;
using Sacco.Shared.Auth;
using Sacco.Shared.Ledger;
using Sacco.Shared.Members;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Payments;

namespace Sacco.Modules.Payments.Endpoints;

public sealed record InitiateCollectionRequest(string Provider, string PhoneNumber, decimal Amount, PaymentPurposeType Purpose, string? AccountNumber, string? LoanNumber, string? Narrative);
public sealed record InitiateDisbursementRequest(Guid WithdrawalId, string? Provider);
public sealed record SelfTopUpRequest(string Provider, decimal Amount, string AccountNumber, string? LoanNumber);
public sealed record PaymentTransactionResponse(Guid Id, PaymentKind Kind, string Provider, PaymentStatus Status, decimal Amount, string Counterparty, Guid? MemberId, PaymentPurposeType PurposeType, string? AccountNumber, string? LoanNumber, Guid? WithdrawalId,
    string OurReference, string? ProviderRequestId, string? ProviderTransactionReference, string? FailureReason, Guid? LedgerJournalEntryId, Guid InitiatedByUserId, DateTimeOffset InitiatedAt, DateTimeOffset? CompletedAt, int CallbackCount, string? Narrative);
public sealed record ProviderInfo(string Name, bool IsSandbox);

public sealed class PaymentEndpoints(IHostEnvironment env) : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/payments").WithTags("Payments");

        g.MapGet("/providers", (PaymentProviderRegistry registry) => TypedResults.Ok(registry.Known.Select(n => { var p = registry.Resolve(n); return new ProviderInfo(p.ProviderName, p.IsSandbox); }).ToList()))
            .RequirePermission(Permissions.Payments.View).WithName("ListPaymentProviders");

        g.MapPost("/collections", async (InitiateCollectionRequest r, PaymentService payments, ICurrentUser user, CancellationToken ct) =>
        {
            var tx = await payments.InitiateCollectionAsync(r.Provider, r.PhoneNumber, r.Amount, r.Purpose, r.AccountNumber, r.LoanNumber, r.Narrative, user.UserId, ct);
            return TypedResults.Created($"/api/payments/transactions/{tx.Id}", ToResponse(tx));
        }).RequirePermission(Permissions.Payments.Initiate).WithName("InitiateCollection");

        // Member self-service: a mobile-money push to the member's own phone, into the member's own account or loan.
        app.MapPost("/api/self/payments/topup", async (SelfTopUpRequest r, PaymentService payments, ILedgerService ledger, IMemberDirectory members, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var member = await members.FindAsync(memberId, ct) ?? throw new NotFoundException("Member", memberId);
            var account = await ledger.FindAccountAsync(r.AccountNumber, ct);
            if (account is null || account.MemberId != memberId) throw new NotFoundException("Account", r.AccountNumber);
            var purpose = r.LoanNumber is null ? PaymentPurposeType.SavingsDeposit : PaymentPurposeType.LoanRepayment;
            var tx = await payments.InitiateCollectionAsync(r.Provider, member.PhoneNumber, r.Amount, purpose, r.AccountNumber, r.LoanNumber, "Self-service top-up", user.UserId, ct);
            return TypedResults.Created($"/api/payments/transactions/{tx.Id}", ToResponse(tx));
        }).RequirePermission(Permissions.Self.PaymentsInitiate).WithTags("Self-service").WithName("SelfServiceTopUp");
        app.MapGet("/api/self/payments", async (PaymentsDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            return TypedResults.Ok((await db.Transactions.AsNoTracking().Where(t => t.MemberId == memberId).OrderByDescending(t => t.InitiatedAt).Take(50).ToListAsync(ct)).Select(ToResponse).ToList());
        }).RequirePermission(Permissions.Self.AccountsView).WithTags("Self-service").WithName("GetMyPayments");

        g.MapPost("/disbursements", async (InitiateDisbursementRequest r, PaymentService payments, ICurrentUser user, CancellationToken ct) =>
        {
            var tx = await payments.InitiateWithdrawalPayoutAsync(r.WithdrawalId, r.Provider, user.UserId, ct);
            return TypedResults.Created($"/api/payments/transactions/{tx.Id}", ToResponse(tx));
        }).RequirePermission(Permissions.Payments.Initiate).WithName("InitiateWithdrawalPayout");

        g.MapGet("/transactions", async (PaymentsDbContext db, PaymentStatus? status, PaymentKind? kind, string? provider, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.Transactions.AsNoTracking();
            if (status is PaymentStatus s) q = q.Where(t => t.Status == s);
            if (kind is PaymentKind k) q = q.Where(t => t.Kind == k);
            if (!string.IsNullOrEmpty(provider)) q = q.Where(t => t.Provider == provider);
            var total = await q.CountAsync(ct);
            var items = (await q.OrderByDescending(t => t.InitiatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(ToResponse).ToList();
            return TypedResults.Ok(new PagedResult<PaymentTransactionResponse>(items, page, pageSize, total));
        }).RequirePermission(Permissions.Payments.View).WithName("ListPaymentTransactions");

        g.MapGet("/transactions/{id:guid}", async Task<Results<Ok<PaymentTransactionResponse>, NotFound>> (Guid id, PaymentsDbContext db, CancellationToken ct) =>
        {
            var t = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return t is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(t));
        }).RequirePermission(Permissions.Payments.View).WithName("GetPaymentTransaction");

        g.MapPost("/transactions/{id:guid}/reconcile", async (Guid id, PaymentService payments, PaymentFinalizer finalizer, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await payments.ReconcileAsync(id, finalizer, ct))))
            .RequirePermission(Permissions.Payments.Reconcile).WithName("ReconcilePaymentTransaction");

        // Provider callbacks. Tenant comes from the path (registered with the provider per SACCO); no bearer token —
        // authenticity is the provider implementation's job (signature/IP) and processing is idempotent.
        app.MapPost("/api/payments/webhooks/{tenant}/{provider}", async (string tenant, string provider, HttpContext http, WebhookProcessor processor, WebhookSourceGuard guard, CancellationToken ct) =>
        {
            if (!guard.IsAllowed(http.Connection.RemoteIpAddress)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            using var reader = new StreamReader(http.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var headers = http.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            foreach (var q in http.Request.Query) headers[$"query:{q.Key}"] = q.Value.ToString(); // providers that cannot set headers put a shared secret in the callback URL
            var result = await processor.ProcessAsync(NormaliseProvider(provider), new WebhookPayload(body, headers, http.Connection.RemoteIpAddress?.ToString()), ct);
            return result.Accepted ? Results.Ok(new { result.Message, result.TransactionId }) : Results.BadRequest(new { result.Message });
        }).RequireRateLimiting("public").WithTags("Provider webhooks").WithName("ProviderWebhook");

        // Sandbox convenience: deliver the callback the sandbox provider would send for an in-flight transaction.
        g.MapPost("/sandbox/transactions/{id:guid}/callback", async (Guid id, PaymentsDbContext db, PaymentProviderRegistry registry, WebhookProcessor processor, bool? success, CancellationToken ct) =>
        {
            if (env.IsProduction()) throw new ForbiddenException("Sandbox callbacks are disabled in production.");
            var tx = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Payment transaction", id);
            if (registry.Resolve(tx.Provider) is not SandboxProviderBase sandbox) throw new DomainRuleException("payments.not_sandbox", $"{tx.Provider} is not running in sandbox mode.");
            if (tx.ProviderRequestId is null) throw new DomainRuleException("payments.not_initiated", "The provider never accepted this request.");
            var callback = sandbox.BuildCallback(tx.ProviderRequestId, success) ?? throw new DomainRuleException("payments.sandbox_unknown_request", "The sandbox has no record of this request (was the API restarted?).");
            var result = await processor.ProcessSandboxCallbackAsync(tx.Provider, callback, ct);
            return TypedResults.Ok(new { result.Message, Callback = callback });
        }).RequirePermission(Permissions.Payments.Initiate).WithName("SimulateSandboxCallback");
    }

    private static string NormaliseProvider(string routeValue) => routeValue.ToLowerInvariant() switch
    {
        "mpesa" => ProviderNames.MPesa,
        "airtelmoney" or "airtel" => ProviderNames.AirtelMoney,
        var b when b.StartsWith("bank") => ProviderNames.BankPrefix + routeValue[(routeValue.IndexOf(':') is var i && i >= 0 ? i + 1 : 4)..].ToUpperInvariant(),
        _ => routeValue,
    };

    private static PaymentTransactionResponse ToResponse(PaymentTransaction t) => new(t.Id, t.Kind, t.Provider, t.Status, t.Amount, t.Counterparty, t.MemberId, t.Purpose.Type, t.Purpose.AccountNumber, t.Purpose.LoanNumber, t.Purpose.WithdrawalId,
        t.OurReference, t.ProviderRequestId, t.ProviderTransactionReference, t.FailureReason, t.LedgerJournalEntryId, t.InitiatedByUserId, t.InitiatedAt, t.CompletedAt, t.CallbackCount, t.Narrative);
}
