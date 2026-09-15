using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sacco.Modules.Payments.Application;
using Sacco.Modules.Payments.Endpoints;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Payments.Providers;
using Sacco.Modules.Payments.Sagas;
using Sacco.Shared.Http;
using Sacco.Shared.Persistence;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
using Wolverine.RuntimeCompilation;

namespace Sacco.Modules.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<PaymentsDbContext>(connectionString, PaymentsDbContext.SchemaName);
        services.Configure<PaymentsSettings>(configuration.GetSection(PaymentsSettings.SectionName));
        services.AddHttpClient(nameof(DarajaMpesaProvider));
        services.AddHttpClient(AirtelMoneyProvider.HttpClientName);
        // Sandbox providers are singletons so their in-memory request store survives across requests (and sagas).
        services.AddSingleton<SandboxMpesaProvider>();
        services.AddSingleton<SandboxAirtelMoneyProvider>();
        services.AddSingleton<SandboxBankProvider>();
        services.AddSingleton<DarajaMpesaProvider>();
        services.AddSingleton<AirtelMoneyProvider>();
        services.AddHttpClient(EquityJengaProvider.HttpClientName);
        services.AddHttpClient(NcbaProvider.HttpClientName);
        services.AddSingleton<EquityJengaProvider>();
        services.AddSingleton<NcbaProvider>();
        services.AddSingleton<PaymentProviderRegistry>();
        services.AddScoped<PaymentFinalizer>();
        services.AddScoped<PaymentService>();
        services.AddScoped<WebhookProcessor>();
        services.AddSingleton<WebhookSourceGuard>();
        services.AddSingleton<IModuleEndpoints, PaymentEndpoints>();
        return services;
    }

    /// <summary>Wolverine: durable Postgres message store (schema "wolverine"), lightweight saga storage, in-process queues only.</summary>
    public static void ConfigureWolverine(WolverineOptions opts, string connectionString, IHostEnvironment env)
    {
        opts.ServiceName = "sacco-api";
        opts.Discovery.IncludeAssembly(typeof(PaymentsModule).Assembly);
        opts.UseRuntimeCompilation();
        // Module DbContexts are registered through factory lambdas (tenant interceptor), which Wolverine's codegen can only reach via service location.
        opts.ServiceLocationPolicy = JasperFx.CodeGeneration.Model.ServiceLocationPolicy.AlwaysAllowed;

        // The build-time OpenAPI generator (dotnet-getdocument) starts the host without a database: run as a plain mediator then.
        if (IsGeneratingOpenApiDocument)
        {
            opts.Durability.Mode = DurabilityMode.MediatorOnly;
            return;
        }

        opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
        opts.AddSagaType<CollectionSaga>("collection_sagas");
        opts.AddSagaType<DisbursementSaga>("disbursement_sagas");
        opts.Policies.AutoApplyTransactions();
        opts.Durability.Mode = env.IsProduction() ? DurabilityMode.Balanced : DurabilityMode.Solo;
    }

    public static bool IsGeneratingOpenApiDocument =>
        Environment.GetCommandLineArgs().Any(a => a.Contains("getdocument", StringComparison.OrdinalIgnoreCase));
}
