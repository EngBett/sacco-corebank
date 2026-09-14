namespace Sacco.Modules.Lending.Application;

/// <summary>GL and policy defaults for the Lending module. Defaults match the demo chart; tenants override in configuration.</summary>
public sealed class LendingSettings
{
    public const string SectionName = "Lending";
    public string TellerCashGl { get; set; } = "1010";        // FOSA
    public string FosaBankGl { get; set; } = "1020";          // FOSA
    public string MpesaSettlementGl { get; set; } = "1030";   // FOSA
    public string AirtelSettlementGl { get; set; } = "1040";  // FOSA
    public string BosaBankGl { get; set; } = "1000";          // BOSA (check-off remittances)
    public string FosaSavingsControlGl { get; set; } = "2200";
    public string BosaDepositsControlGl { get; set; } = "2000";
    public string DueFromFosaGl { get; set; } = "1800";
    public string DueToBosaGl { get; set; } = "2800";
    /// <summary>A guarantor may not have more than this many active guarantees.</summary>
    public int MaxActiveGuaranteesPerMember { get; set; } = 5;
    /// <summary>Total active guarantees may not exceed this multiple of the guarantor's BOSA deposits (1.0 = 100%).</summary>
    public decimal MaxGuaranteeToDepositsRatio { get; set; } = 1.0m;
    public CreditBureauSettings CreditBureau { get; set; } = new();
    public MaintenanceSettings Maintenance { get; set; } = new();
}

public sealed class MaintenanceSettings
{
    /// <summary>Daily interest accrual and bureau-retention purge. Disabled in tools (seed) and tests; the API host enables it.</summary>
    public bool Enabled { get; set; } = true;
    public TimeOnly RunAtUtc { get; set; } = new(2, 0);
}

/// <summary>"Sandbox" (deterministic, no credentials) or "Live" (a real bureau provider; none is written yet, so Live reports every lookup as unavailable).</summary>
public sealed class CreditBureauSettings
{
    public string Mode { get; set; } = "Sandbox";
    /// <summary>Applications must carry the member's consent to a bureau check (Credit Reference Bureau Regulations).</summary>
    public bool RequireConsent { get; set; } = true;
    public string ConsentText { get; set; } = "I authorise the SACCO to obtain my credit report from a licensed credit reference bureau and to share my repayment history with it, for the purpose of assessing this application.";
    /// <summary>Bureau narratives/references are purged after this many days; the score and status remain.</summary>
    public int RetentionDays { get; set; } = 365;
}
