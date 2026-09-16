using System.Globalization;
using System.Net;
using System.Text;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Reporting.Application;

/// <summary>
/// Builds the nightly digest's HTML (ADR 0013) — one self-contained document, inline CSS only (no external
/// stylesheet or font fetch, so rendering never depends on the tenant's own network reachability), branded
/// with the tenant's logo/name/colour. Kept as plain string-building, matching this module's existing CSV
/// export (<see cref="StatutoryReturnCsv"/>) rather than pulling in a template engine for one document.
/// </summary>
public static class DailyDigestHtml
{
    private static string Money(decimal v) => "KES " + v.ToString("N2", CultureInfo.InvariantCulture);
    private static string Pct(decimal bps) => (bps / 100m).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Build(TenantBrandingInfo branding, DateOnly asOf, FinancialPosition position, CapitalAdequacy capital, LiquidityPosition liquidity, PortfolioQualitySnapshot portfolio)
    {
        var accent = string.IsNullOrWhiteSpace(branding.PrimaryColor) ? "#0f766e" : branding.PrimaryColor;
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
              * { box-sizing: border-box; }
              body { font-family: -apple-system, "Segoe UI", Helvetica, Arial, sans-serif; color: #1a1a1a; margin: 0; font-size: 12px; line-height: 1.45; }
              .header { display: flex; align-items: center; justify-content: space-between; border-bottom: 3px solid {{accent}}; padding-bottom: 14px; margin-bottom: 22px; }
              .brand { display: flex; align-items: center; gap: 12px; }
              .brand img { height: 40px; max-width: 160px; object-fit: contain; }
              .brand-mark { height: 40px; width: 40px; border-radius: 8px; background: {{accent}}; color: #fff; display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 16px; }
              .brand-name { font-size: 17px; font-weight: 700; color: #111; }
              .doc-title { font-size: 12px; color: #666; margin-top: 2px; }
              .as-of { text-align: right; font-size: 12px; color: #666; }
              .as-of strong { display: block; font-size: 14px; color: #111; }
              h2 { font-size: 13px; text-transform: uppercase; letter-spacing: 0.04em; color: {{accent}}; margin: 26px 0 10px; border-bottom: 1px solid #e5e5e5; padding-bottom: 6px; }
              .kpis { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }
              .kpi { border: 1px solid #e5e5e5; border-radius: 8px; padding: 12px; }
              .kpi .label { font-size: 10px; color: #777; text-transform: uppercase; letter-spacing: 0.03em; }
              .kpi .value { font-size: 19px; font-weight: 700; margin-top: 4px; color: #111; }
              .kpi .hint { font-size: 10px; color: #888; margin-top: 3px; }
              .pill { display: inline-block; border-radius: 10px; padding: 1px 8px; font-size: 10px; font-weight: 600; margin-top: 6px; }
              .pill.ok { background: #e6f4ea; color: #1a7f37; }
              .pill.bad { background: #fdeaea; color: #c92a2a; }
              table { width: 100%; border-collapse: collapse; margin-top: 4px; }
              th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #eee; }
              th { font-size: 10px; text-transform: uppercase; color: #777; font-weight: 600; }
              td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
              tr.total td { font-weight: 700; border-top: 2px solid #ccc; border-bottom: none; }
              .footer { margin-top: 30px; padding-top: 12px; border-top: 1px solid #e5e5e5; font-size: 10px; color: #999; display: flex; justify-content: space-between; }
            </style>
            </head>
            <body>
            """);

        sb.Append($$"""
            <div class="header">
              <div class="brand">
            """);
        sb.Append(string.IsNullOrWhiteSpace(branding.LogoUrl)
            ? $"""<div class="brand-mark">{E(branding.ShortName.Length > 0 ? branding.ShortName[..Math.Min(2, branding.ShortName.Length)].ToUpperInvariant() : "SA")}</div>"""
            : $"""<img src="{E(branding.LogoUrl)}" alt="{E(branding.ShortName)} logo">""");
        sb.Append($$"""
                <div>
                  <div class="brand-name">{{E(branding.Name)}}</div>
                  <div class="doc-title">Daily prudential &amp; portfolio snapshot</div>
                </div>
              </div>
              <div class="as-of"><strong>{{asOf:dddd, d MMMM yyyy}}</strong>Generated {{DateTime.UtcNow:HH:mm}} UTC</div>
            </div>
            """);

        sb.Append("<h2>Capital adequacy &amp; liquidity</h2><div class=\"kpis\">");
        foreach (var r in capital.Ratios) sb.Append(Kpi(r.Name, Pct(r.ValueBps), $"minimum {Pct(r.MinimumBps)}", r.Compliant));
        sb.Append(Kpi(liquidity.Ratio.Name, Pct(liquidity.Ratio.ValueBps), $"minimum {Pct(liquidity.Ratio.MinimumBps)}", liquidity.Ratio.Compliant));
        sb.Append("</div>");

        sb.Append($$"""
            <h2>Financial position (consolidated, as of {{asOf:d MMM yyyy}})</h2>
            <table>
              <tr><th>Assets</th><th class="num">Liabilities &amp; equity</th></tr>
              <tr>
                <td class="num" style="text-align:left">{{Money(position.TotalAssets)}}</td>
                <td class="num">{{Money(position.TotalLiabilitiesAndEquity)}}</td>
              </tr>
            </table>
            """);

        sb.Append($$"""
            <h2>Loan portfolio quality (as of {{asOf:d MMM yyyy}})</h2>
            <div class="kpis">
            """);
        var nplShare = portfolio.TotalOutstanding == 0 ? 0m : Math.Round(portfolio.NonPerformingOutstanding / portfolio.TotalOutstanding * 100m, 2);
        sb.Append(Kpi("Outstanding portfolio", Money(portfolio.TotalOutstanding), null, true));
        sb.Append(Kpi("Non-performing", Money(portfolio.NonPerformingOutstanding), $"{nplShare.ToString(CultureInfo.InvariantCulture)}% of book", nplShare <= 5m));
        sb.Append(Kpi("Provision required", Money(portfolio.ProvisionRequired), portfolio.ConfigSource, true));
        sb.Append("</div>");

        var supportLine = string.IsNullOrWhiteSpace(branding.SupportEmail) ? "" : $" · {E(branding.SupportEmail)}";
        sb.Append($$"""
            <div class="footer">
              <span>Confidential — for the SACCO's internal use only.</span>
              <span>{{E(branding.Name)}}{{supportLine}}</span>
            </div>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    private static string Kpi(string label, string value, string? hint, bool ok) => $"""
        <div class="kpi">
          <div class="label">{E(label)}</div>
          <div class="value">{E(value)}</div>
          {(hint is null ? "" : $"""<div class="hint">{E(hint)}</div>""")}
          <span class="pill {(ok ? "ok" : "bad")}">{(ok ? "Within threshold" : "Needs attention")}</span>
        </div>
        """;
}
