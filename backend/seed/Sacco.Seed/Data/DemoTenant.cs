using Sacco.Shared.Domain;

namespace Sacco.Seed.Data;

/// <summary>Stable identifiers for the Demo SACCO tenant. Every seeder and test refers to these, never to literals.</summary>
public static class DemoTenant
{
    public const string Slug = "demo";
    public const string Name = "Demo SACCO Society Ltd";
    public const string ShortName = "Demo SACCO";
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

    public static Guid MemberId(string memberNumber) => Ids.Deterministic($"member:{memberNumber}");
}
