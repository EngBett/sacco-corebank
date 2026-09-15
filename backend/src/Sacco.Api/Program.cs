using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Sacco.Api.Infrastructure;
using Sacco.Modules.Identity;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Modules.Identity.Persistence;
using Sacco.Modules.Ledger;
using Sacco.Modules.Lending;
using Sacco.Modules.Lending.Persistence;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Modules.Members;
using Sacco.Modules.Notifications;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Modules.Payments;
using Sacco.Modules.Payments.Persistence;
using Wolverine;
using Sacco.Modules.Members.Persistence;
using Sacco.Modules.Platform;
using Sacco.Modules.Reporting;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Modules.Savings;
using Sacco.Modules.Savings.Persistence;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Sacco")
    ?? throw new InvalidOperationException("ConnectionStrings:Sacco is not configured.");

// ---- Cross-cutting ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "SACCO Platform API";
    doc.Info.Version = "v1";
    doc.Info.Description = "Core banking API for Kenyan Deposit-Taking SACCOs. Pass the tenant slug in the X-Tenant header when not resolving by host. Tokens are issued by the in-process OIDC server at /connect/*.";
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PlatformDbContext>("platform-db")
    .AddDbContextCheck<LedgerDbContext>("ledger-db")
    .AddDbContextCheck<IdentityDbContext>("identity-db")
    .AddDbContextCheck<MembersDbContext>("members-db")
    .AddDbContextCheck<SavingsDbContext>("savings-db")
    .AddDbContextCheck<LendingDbContext>("lending-db")
    .AddDbContextCheck<PaymentsDbContext>("payments-db")
    .AddDbContextCheck<ReportingDbContext>("reporting-db")
    .AddDbContextCheck<NotificationsDbContext>("notifications-db");

// The portal's browser code talks to the API directly for one thing only: the notifications hub (SignalR).
// Everything else goes through the BFF, so this is the only cross-origin surface.
const string PortalCorsPolicy = "portal";
var portalOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(PortalCorsPolicy, p => p.WithOrigins(portalOrigins).SetIsOriginAllowedToAllowWildcardSubdomains().AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Public, unauthenticated surface (membership applications): tight per-IP window.
    o.AddFixedWindowLimiter("public", w => { w.PermitLimit = 10; w.Window = TimeSpan.FromMinutes(1); w.QueueLimit = 0; });
});

// ---- Authentication: bearer tokens validated against the in-process Open.IdentityServer (ADR 0005) ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub", "name", "tenant" as issued
        o.TokenValidationParameters.NameClaimType = "name";
        o.TokenValidationParameters.RoleClaimType = "role";
    });
builder.Services.AddPermissionAuthorization();

// ---- Modules ----
builder.Services.AddPlatformModule(builder.Configuration, connectionString);
builder.Services.AddScoped<ITenantLookup, PlatformTenantLookup>();
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment, connectionString);
builder.Services.AddLedgerModule(connectionString);
builder.Services.AddMembersModule(builder.Configuration, connectionString);
builder.Services.AddSavingsModule(builder.Configuration, connectionString);
builder.Services.AddLendingModule(builder.Configuration, connectionString);
builder.Services.AddPaymentsModule(builder.Configuration, connectionString);
builder.Services.AddReportingModule(builder.Configuration, connectionString);
builder.Services.AddNotificationsModule(builder.Configuration, connectionString).AddNotificationsRealtime(PortalCorsPolicy, builder.Configuration["Notifications:Redis"]);
builder.Services.AddLendingScheduler(); // daily interest accrual + bureau retention purge (Lending:Maintenance)

// Messaging & sagas: Wolverine (MIT), scoped to payment-provider orchestration (ADR 0003).
builder.Host.UseWolverine(opts => PaymentsModule.ConfigureWolverine(opts, connectionString, builder.Environment));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false) && !PaymentsModule.IsGeneratingOpenApiDocument)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<MembersDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<SavingsDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<LendingDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    await Sacco.Shared.Persistence.RowLevelSecurity.ForceTenantIsolationAsync(scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.GetDbConnection());
}

app.MapOpenApi();
if (!app.Environment.IsProduction())
    app.MapScalarApiReference(o => o.WithTitle("SACCO Platform API"));

app.MapHealthChecks("/health");

if (app.Environment.IsProduction() && app.Configuration.GetSection("Payments:WebhookAllowedCidrs").Get<string[]>() is not { Length: > 0 })
    app.Logger.LogWarning("Payments:WebhookAllowedCidrs is empty: provider webhooks accept callbacks from any source IP. Restrict to the providers' published ranges before go-live.");

app.UseRateLimiter();
app.UseCors();
app.UseSaccoIdentityServer();   // /connect/*, /.well-known/*; also registers authentication middleware
app.UseAuthentication();
app.UseTenantResolution();      // after authentication so the token's tenant claim participates
app.UseAuthorization();

foreach (var module in app.Services.GetServices<IModuleEndpoints>())
    module.Map(app);

app.Run();

/// <summary>Exposed so integration tests can bootstrap the host via WebApplicationFactory.</summary>
public partial class Program;
