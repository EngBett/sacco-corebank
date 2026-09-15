using Microsoft.OpenApi.Models;
using MockedNcba.API.Filters;
using MockedNcba.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiKeyValidationFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "Mock NCBA Gateway API",
        Version = "v1",
        Description =
            "Mock NCBA Gateway for local development and testing. " +
            "Simulates the unified POST /api/v1/payments/transfer endpoint. " +
            "Auth: API-Key and API-User headers required on all requests."
    });
});

// In-memory transaction store (thread-safe, keyed by request Reference for duplicate detection)
builder.Services.AddSingleton<ITransactionStore, TransactionStore>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mock NCBA Gateway v1");
    c.RoutePrefix = string.Empty; // Swagger at root
});

app.UseRouting();
app.MapControllers();

app.Run();
