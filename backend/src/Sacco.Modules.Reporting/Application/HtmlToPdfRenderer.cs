using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Sacco.Modules.Reporting.Application;

/// <summary>
/// Renders an HTML string to PDF bytes via headless Chromium (ADR 0013). One browser process per call —
/// simpler and safer than pooling one across a whole nightly run (no shared-state risk between tenants) —
/// which is a fine trade for something that runs once per tenant per night, not per request.
/// In production point <c>Reporting:DailyDigest:ChromiumExecutablePath</c> at the system Chromium installed
/// in the Docker image (see backend/Dockerfile); left unset, PuppeteerSharp downloads its own revision on
/// first use, which is convenient for local dev but not something to rely on in a locked-down environment.
/// </summary>
public sealed class HtmlToPdfRenderer(IOptions<ReportingSettings> options, ILogger<HtmlToPdfRenderer> logger)
{
    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static string? cachedExecutablePath;

    private async Task<string> ExecutablePathAsync(CancellationToken ct)
    {
        var configured = options.Value.DailyDigest.ChromiumExecutablePath;
        if (!string.IsNullOrEmpty(configured)) return configured;
        if (cachedExecutablePath is not null) return cachedExecutablePath;
        await FetchLock.WaitAsync(ct);
        try
        {
            if (cachedExecutablePath is not null) return cachedExecutablePath;
            logger.LogInformation("Reporting:DailyDigest:ChromiumExecutablePath is not set; downloading a Chromium revision for local rendering (do this in the Docker image for production instead).");
            var fetcher = new BrowserFetcher();
            var revision = await fetcher.DownloadAsync();
            cachedExecutablePath = revision.GetExecutablePath();
            return cachedExecutablePath;
        }
        finally { FetchLock.Release(); }
    }

    public async Task<byte[]> RenderAsync(string html, CancellationToken ct)
    {
        var execPath = await ExecutablePathAsync(ct);
        var launchOptions = new LaunchOptions
        {
            Headless = true,
            ExecutablePath = execPath,
            // --no-sandbox: the container runs as a non-root user with no working Chromium sandbox; safe here
            // because the HTML is always ours (built from our own data, never third-party/untrusted content).
            Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu", "--disable-dev-shm-usage"],
        };
        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0], Timeout = 30_000 });
        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions { Top = "14mm", Bottom = "14mm", Left = "12mm", Right = "12mm" },
        });
    }
}
