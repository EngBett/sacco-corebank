using MockedAirtel.API.Services;
using MockedAirtel.API.Configuration;
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
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        mockVaultOptions.MountPoint);

    builder.Services.AddHostedService<VaultChangeWatcher>();
}

// Register strongly-typed configuration options
builder.Services.Configure<MockVaultOptions>(builder.Configuration.GetSection(MockVaultOptions.SectionName));

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Mock Airtel Money API",
        Version = "v1",
        Description = "Mock implementation of Airtel Money API for local development and testing"
    });
});

// Register HttpClient for callback service
builder.Services.AddHttpClient();

// Register callback service
builder.Services.AddSingleton<ICallbackService, CallbackService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mock Airtel Money API v1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at root
    });
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
