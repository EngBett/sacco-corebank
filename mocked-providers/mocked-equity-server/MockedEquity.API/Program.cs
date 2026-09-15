using Microsoft.OpenApi.Models;
using MockedEquity.API.Filters;
using MockedEquity.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<JengaOptions>(builder.Configuration.GetSection(JengaOptions.SectionName));

builder.Services.AddControllers(options =>
{
    // Applied globally; authentication, health and the test-control endpoints opt out with
    // [AllowAnonymous], exactly as the real platform gates everything behind a bearer token.
    options.Filters.Add<BearerTokenFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Mock Equity (Finserve Jenga DFS) API",
        Version = "v1",
        Description =
            "Mock of the Equity Bank / Finserve Jenga DFS Partner REST APIs for local development "
            + "and integration tests. Reproduces the behaviours that matter: acknowledgements carry "
            + "no transaction id, results arrive asynchronously on the callback URL, success and "
            + "failure callbacks use different payload shapes, and PINs must be AES-256-GCM "
            + "encrypted per the spec. Test-only helpers live under /_test."
    });
});

builder.Services.AddSingleton<ITransactionStore, TransactionStore>();
builder.Services.AddSingleton<ITokenStore, TokenStore>();
builder.Services.AddSingleton<ICallbackService, CallbackService>();
builder.Services.AddScoped<IPaymentSimulator, PaymentSimulator>();

// Callbacks are posted back to the payments service; a short timeout keeps a hung receiver from
// pinning the delivery task forever.
builder.Services.AddHttpClient("callbacks", client => client.Timeout = TimeSpan.FromSeconds(15));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mock Equity Jenga DFS v1");
    c.RoutePrefix = string.Empty; // Swagger at root
});

app.UseRouting();
app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can host the mock with WebApplicationFactory.</summary>
public partial class Program;
