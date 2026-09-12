namespace Sacco.Modules.Reporting.Application;

/// <summary>
/// Maps the chart of accounts onto SASRA return lines and holds the prudential thresholds.
/// Thresholds are from the Sacco Societies (Deposit-Taking Sacco Business) Regulations as understood
/// at build time — confirm against the current SASRA circular before the first live submission
/// (docs/compliance/sasra-mapping.md, open items). Defaults match the demo chart of accounts.
/// </summary>
public sealed class ReportingSettings
{
    public const string SectionName = "Reporting";

    public List<string> LiquidAssetGls { get; set; } = ["1000", "1010", "1020", "1030", "1040"];
    public List<string> LoanControlGls { get; set; } = ["1100", "1150"];
    public List<string> LoanProvisionGls { get; set; } = ["1900", "1950"];
    public List<string> MemberDepositGls { get; set; } = ["2000", "2100", "2200"];
    public List<string> ShortTermLiabilityGls { get; set; } = ["2300", "2400", "2500", "2600", "2700"];
    public List<string> ShareCapitalGls { get; set; } = ["3000"];
    public List<string> InstitutionalCapitalGls { get; set; } = ["3100", "3200", "3300"];
    /// <summary>Inter-segment clearing pair: shown per segment, netted to zero on consolidation.</summary>
    public List<string> InterSegmentClearingGls { get; set; } = ["1800", "2800"];

    public int CoreCapitalToAssetsMinBps { get; set; } = 1000;          // ≥ 10% of total assets
    public int InstitutionalCapitalToAssetsMinBps { get; set; } = 800;  // ≥ 8% of total assets
    public int CoreCapitalToDepositsMinBps { get; set; } = 800;         // ≥ 8% of total deposits
    public int LiquidityMinBps { get; set; } = 1500;                    // ≥ 15% of deposits + short-term liabilities
    /// <summary>A member whose total loans exceed this share of core capital is a large exposure. Confirm the current SASRA limit.</summary>
    public int LargeExposureThresholdBps { get; set; } = 2500;
    /// <summary>SDGF contribution rate on deposits; null until the current basis is confirmed with SASRA.</summary>
    public int? SdgfContributionBps { get; set; } = null;
    public string ThresholdSource { get; set; } = "Sacco Societies (DT-Sacco Business) Regulations — verify against current SASRA circular before submission";
}
