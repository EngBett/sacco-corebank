using Sacco.Modules.Reporting.Application;
using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Shouldly;

namespace Sacco.UnitTests.Reporting;

/// <summary>The nightly digest's HTML — branding, key figures and the compliant/non-compliant pill all have to land in the markup, since this is what Chromium ends up rendering to PDF.</summary>
public class DailyDigestHtmlTests
{
    private static readonly ReportSection Empty = new("x", [], 0m);
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    private static FinancialPosition Position(decimal assets, decimal le) => new(null, AsOf, Empty, Empty, Empty, 0m, 0m, assets, le, assets == le);
    private static Ratio R(string name, decimal value, int min) => new(name, 0, 0, value, min, value >= min, "basis");
    private static CapitalAdequacy Capital() => new(AsOf, 0, 0, 4_688_067.93m, 4_688_067.93m, 2_918_800m,
        [R("Core capital / total assets", 6144m, 1000), R("Core capital / total deposits", 8049m, 800)], "source");
    private static LiquidityPosition Liquidity(bool compliant) => new(AsOf, 100, 100, 100, R("Liquid assets / (deposits + short-term liabilities)", compliant ? 2000m : 500m, 1500), "source");
    private static PortfolioQualitySnapshot Portfolio() => new(AsOf, 475_876.27m, 253_522.33m, 137_620.73m, [], [], "config source");

    [Fact]
    public void Renders_tenant_branding_and_key_figures()
    {
        var branding = new TenantBrandingInfo("Icodeio SACCO Society Ltd", "Icodeio SACCO", "#0f766e", "#f59e0b", "https://cdn.example/logo.png", "support@icodeio.example.co.ke");
        var html = DailyDigestHtml.Build(branding, AsOf, Position(4_688_067.93m, 4_688_067.93m), Capital(), Liquidity(true), Portfolio());

        html.ShouldContain("Icodeio SACCO Society Ltd");
        html.ShouldContain("https://cdn.example/logo.png");
        html.ShouldContain("#0f766e");
        html.ShouldContain("support@icodeio.example.co.ke");
        html.ShouldContain("KES 4,688,067.93");
        html.ShouldContain("KES 253,522.33"); // non-performing outstanding
        html.ShouldContain("61.44%"); // core capital / total assets
        html.ShouldContain("Within threshold");
        html.ShouldStartWith("<!doctype html>");
    }

    [Fact]
    public void A_breached_ratio_gets_the_non_compliant_pill_and_a_generic_mark_stands_in_for_a_missing_logo()
    {
        var branding = new TenantBrandingInfo("Icodeio SACCO Society Ltd", "Demo", "#0f766e", "#f59e0b", null, "");
        var html = DailyDigestHtml.Build(branding, AsOf, Position(100, 100), Capital(), Liquidity(false), Portfolio());

        html.ShouldContain("Needs attention");
        html.ShouldContain("brand-mark"); // no logo -> the initials mark, not an <img>
        html.ShouldNotContain("<img");
    }

    [Fact]
    public void User_supplied_branding_text_is_html_encoded()
    {
        var branding = new TenantBrandingInfo("Demo & <SACCO>", "Demo", "#0f766e", "#f59e0b", null, "");
        var html = DailyDigestHtml.Build(branding, AsOf, Position(0, 0), Capital(), Liquidity(true), Portfolio());
        html.ShouldContain("Demo &amp; &lt;SACCO&gt;");
        html.ShouldNotContain("<SACCO>");
    }
}
