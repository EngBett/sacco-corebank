using Sacco.Shared.Domain;

namespace Sacco.Seed.Data;

/// <summary>Stable identifiers for the Icodeio SACCO tenant. Every seeder and test refers to these, never to literals.</summary>
public static class DemoTenant
{
    public const string Slug = "demo";
    public const string Name = "Icodeio SACCO Society Ltd";
    public const string ShortName = "Icodeio SACCO";
    /// <summary>The platform's default theme colour. The logo does not set the theme.</summary>
    public const string PrimaryColor = "#0f766e";
    /// <summary>Root-relative: the file lives in <c>Sacco.Api/wwwroot</c>.</summary>
    public const string LogoUrl = "/tenant-assets/demo/logo.png";
    public const string FaviconUrl = "/tenant-assets/demo/favicon.ico";
    /// <summary>Public site logos: the dark logo on a transparent background in light mode, the white logo in dark mode.</summary>
    public const string LightModeLogoUrl = "/tenant-assets/demo/logo-light-mode.png";
    public const string DarkModeLogoUrl = "/tenant-assets/demo/logo-dark-mode.jpg";
    public static readonly Guid Id = Ids.Deterministic("tenant:demo");

    /// <summary>Demo staff user ids — the Identity seeder (Phase 2) creates matching users with these ids.</summary>
    public static class Users
    {
        public static readonly Guid System = Ids.Deterministic("user:system");
        public static readonly Guid Admin = Ids.Deterministic("user:admin");
        public static readonly Guid Teller = Ids.Deterministic("user:teller");
        public static readonly Guid LoanOfficer = Ids.Deterministic("user:loan-officer");
        public static readonly Guid CreditCommittee1 = Ids.Deterministic("user:credit-committee-1");
        public static readonly Guid CreditCommittee2 = Ids.Deterministic("user:credit-committee-2");
        public static readonly Guid BranchManager = Ids.Deterministic("user:branch-manager");
        public static readonly Guid Accountant = Ids.Deterministic("user:accountant");
        public static readonly Guid ComplianceOfficer = Ids.Deterministic("user:compliance-officer");
    }

    /// <summary>Demo offices (ADR 0018). The head office is first; staff and members are spread across all three.</summary>
    public static readonly (string Code, string Name, bool HeadOffice, string County, string Town, string Address, string Phone)[] Branches =
    [
        ("HQ", "Head Office", true, "Nakuru", "Nakuru", "Kenyatta Avenue, Nakuru", "254700100000"),
        ("NKR", "Nakuru Town Branch", false, "Nakuru", "Nakuru", "Moi Road, Nakuru", "254700100001"),
        ("ELD", "Eldoret Branch", false, "Uasin Gishu", "Eldoret", "Oginga Odinga Street, Eldoret", "254700100002"),
    ];

    public static Guid BranchId(string code) => Ids.Deterministic($"branch:{Slug}:{code}");

    public static Guid MemberId(string memberNumber) => Ids.Deterministic($"member:{memberNumber}");
}
