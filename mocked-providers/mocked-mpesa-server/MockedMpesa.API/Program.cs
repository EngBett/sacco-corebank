using MassTransit;
using MongoDB.Driver;
using MockedMpesa.API;
using MockedMpesa.API.Consumers;
using MockedMpesa.API.Data;
using MockedMpesa.API.Models;
using MockedMpesa.API.Services;
using MockedMpesa.API.Configuration;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VaultSharp.Extensions.Configuration;
using VaultSharp.V1.AuthMethods.GitHub;
using VaultSharp.V1.AuthMethods.UserPass;

var builder = WebApplication.CreateBuilder(args);

// Base configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Read our own Vault settings (Configuration/MockVaultOptions.cs) — distinct from VaultSharp's own
// VaultOptions type used below to actually talk to Vault.
var mockVaultOptions =
    builder.Configuration
        .GetSection(MockVaultOptions.SectionName)
        .Get<MockVaultOptions>()
    ?? new MockVaultOptions();

// Conditionally add Vault
if (builder.Environment.EnvironmentName != "Testing" &&
    !mockVaultOptions.VaultUri.StartsWith("#") &&
    (!mockVaultOptions.UserName.StartsWith("#") ||
     !string.IsNullOrEmpty(mockVaultOptions.GithubToken)))
{
    VaultOptions vaultOptions;

    if (!string.IsNullOrEmpty(mockVaultOptions.GithubToken))
    {
        vaultOptions = new VaultOptions(
            mockVaultOptions.VaultUri,
            new GitHubAuthMethodInfo(mockVaultOptions.GithubToken),
            reloadOnChange: true,
            reloadCheckIntervalSeconds: 180);
    }
    else
    {
        vaultOptions = new VaultOptions(
            mockVaultOptions.VaultUri,
            new UserPassAuthMethodInfo(
                mockVaultOptions.UserName,
                mockVaultOptions.Password),
            reloadOnChange: true,
            reloadCheckIntervalSeconds: 180);
    }

    builder.Configuration.AddVaultConfiguration(
        () => vaultOptions,
        $"appsettings.{builder.Environment.EnvironmentName}",
        mockVaultOptions.MountPoint);

    builder.Services.AddHostedService<VaultChangeWatcher>();
}

// Register strongly-typed configuration options
builder.Services.Configure<MockVaultOptions>(builder.Configuration.GetSection(MockVaultOptions.SectionName));
builder.Services.Configure<MpesaCredentialsOptions>(builder.Configuration.GetSection(MpesaCredentialsOptions.SectionName));

// Add OpenTelemetry
var otlpLogsEndpoint = builder.Configuration["OpenTelemetry:Exporters:Otlp:LogsEndpoint"];
var otlpTracesEndpoint = builder.Configuration["OpenTelemetry:Exporters:Otlp:TracesEndpoint"];
var otlpMetricsEndpoint = builder.Configuration["OpenTelemetry:Exporters:Otlp:MetricsEndpoint"];
var otlpApiKey = builder.Configuration["OpenTelemetry:Exporters:Otlp:ApiKey"];
var otlpMetricsAuth = builder.Configuration["OpenTelemetry:Exporters:Otlp:MetricsBasicAuth"];

// OTLP auth headers (Loki/Tempo: X-API-Key; Prometheus: X-API-Key + Basic auth)
var signalHeaders = !string.IsNullOrEmpty(otlpApiKey) ? $"X-API-Key={otlpApiKey}" : null;
var metricsHeaders = signalHeaders;
if (!string.IsNullOrEmpty(otlpMetricsAuth))
    metricsHeaders = string.IsNullOrEmpty(metricsHeaders)
        ? $"Authorization={otlpMetricsAuth}"
        : $"{metricsHeaders},Authorization={otlpMetricsAuth}";

if (!string.IsNullOrEmpty(otlpLogsEndpoint))
{
    builder.Logging.AddOpenTelemetry(options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    });
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("MockedMpesaService", "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName
        }))
    .WithLogging(logging =>
    {
        if (!string.IsNullOrEmpty(otlpLogsEndpoint))
        {
            logging.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpLogsEndpoint);
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                if (signalHeaders != null) options.Headers = signalHeaders;
            });
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("MassTransit");

        if (!string.IsNullOrEmpty(otlpTracesEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpTracesEndpoint);
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                if (signalHeaders != null) options.Headers = signalHeaders;
            });
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter();

        if (!string.IsNullOrEmpty(otlpMetricsEndpoint))
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpMetricsEndpoint);
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                if (metricsHeaders != null) options.Headers = metricsHeaders;
            });
        }
    });

// Configure Kestrel for high throughput
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxConcurrentConnections = 1000;
    serverOptions.Limits.MaxConcurrentUpgradedConnections = 1000;
    serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
    serverOptions.Limits.MinRequestBodyDataRate = null;
    serverOptions.Limits.MinResponseDataRate = null;
});

// Add services to the container
builder.Services.AddControllers();

// Add authentication
builder.Services.AddAuthentication("MpesaAuth")
    .AddCookie("MpesaAuth", options =>
    {
        options.Cookie.Name = "MockMpesaAuth";
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor(); // Required for authentication in Blazor

// Add Blazor Server (modern .NET 8+ approach)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAntiforgery();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Mock M-Pesa API",
        Version = "v1",
        Description = "Mock implementation of Safaricom M-Pesa Daraja API for local development and testing"
    });
});

// Add MongoDB
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDB") 
    ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") 
    ?? "MockedMpesaDb";

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoConnectionString);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    // Performance optimizations for high throughput
    settings.MaxConnectionPoolSize = 500;
    settings.MinConnectionPoolSize = 10;
    settings.ConnectTimeout = TimeSpan.FromSeconds(30);
    settings.SocketTimeout = TimeSpan.FromSeconds(30);
    return new MongoClient(settings);
});

builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    return client.GetDatabase(mongoDatabaseName);
});

builder.Services.AddScoped<MockMpesaDbContext>();

// Register HttpClient for callback service
builder.Services.AddHttpClient();

// Register callback service
builder.Services.AddTransient<ICallbackService,CallbackService>();

// Register transaction cleanup background worker
builder.Services.AddHostedService<TransactionCleanupWorker>();

// Configure MassTransit with in-memory transport
builder.Services.AddMassTransit(x =>
{
    // Register consumers
    x.AddConsumer<SendStkPushCallbackConsumer>();
    x.AddConsumer<SendB2CCallbackConsumer>();
    x.AddConsumer<SendReversalCallbackConsumer>();
    x.AddConsumer<SendTransactionStatusCallbackConsumer>();
    x.AddConsumer<SendAccountBalanceCallbackConsumer>();

    // Use in-memory transport (non-commercial) with optimizations
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        // Configure retry policy for transient failures
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 3,
            minInterval: TimeSpan.FromMilliseconds(50),
            maxInterval: TimeSpan.FromSeconds(5),
            intervalDelta: TimeSpan.FromMilliseconds(100)
        ));

        // Increase concurrency for high throughput
        cfg.UseConcurrencyLimit(50);
        
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

// Seed default simulation settings on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MockMpesaDbContext>();
    
    // Seed default simulation settings if none exist
    var settingsCount = await dbContext.SimulationSettings.CountDocumentsAsync(FilterDefinition<SimulationSettings>.Empty);
    if (settingsCount == 0)
    {
        await dbContext.SimulationSettings.InsertOneAsync(new SimulationSettings
        {
            GlobalSimulateFailure = false,
            GlobalSkipCallback = false,
            MinDelayMs = 2000,
            MaxDelayMs = 5000,
            FailureResultCode = 1,
            FailureResultDesc = "Insufficient funds in the account",
            UpdatedAt = DateTime.UtcNow
        });
    }
}

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mock M-Pesa API v1");
    c.RoutePrefix = "swagger"; // Set Swagger UI at /swagger
});

app.UseHttpsRedirection();

app.UseStaticFiles(); // Required for Blazor
app.UseRouting(); // Required for Blazor

app.UseAuthentication(); // Add authentication middleware
app.UseAuthorization(); // Add authorization middleware

app.UseAntiforgery(); // Required for Blazor Server

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

app.Run();
