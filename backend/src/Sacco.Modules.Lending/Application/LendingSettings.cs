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
    public string DueFromFosaGl { get; set; } = "1800";
    public string DueToBosaGl { get; set; } = "2800";
    /// <summary>A guarantor may not have more than this many active guarantees.</summary>
    public int MaxActiveGuaranteesPerMember { get; set; } = 5;
    /// <summary>Total active guarantees may not exceed this multiple of the guarantor's BOSA deposits (1.0 = 100%).</summary>
    public decimal MaxGuaranteeToDepositsRatio { get; set; } = 1.0m;
}
