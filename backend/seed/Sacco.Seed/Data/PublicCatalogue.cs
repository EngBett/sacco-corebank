using Sacco.Modules.Lending.Domain;
using Sacco.Shared.Domain;

namespace Sacco.Seed.Data;

/// <summary>
/// How the demo SACCO presents its products and services on the public website (ADR 0017). Modelled on the way Kenyan
/// deposit-taking SACCOs describe their savings accounts, FOSA, BOSA and MSME loans, with generic names and figures —
/// every SACCO edits this content from the portal.
/// </summary>
public static class PublicCatalogue
{
    private static PublicListing L(int order, string[] features, string[] requirements, string? amountNote = null) =>
        PublicListing.Create(true, order, features, requirements, amountNote, applicationFormUrl: null);

    public static readonly IReadOnlyDictionary<string, PublicListing> Savings = new Dictionary<string, PublicListing>
    {
        ["FOSA-CUR"] = L(10,
            ["Deposit and withdraw at any time at the counter, by M-Pesa, Airtel Money or bank transfer",
             "Have your salary or farm payments paid in and qualify for salary advances",
             "Loan repayments and standing orders run straight from the account",
             "SMS alerts for every transaction, plus mobile and online banking"],
            ["Copy of your national ID or passport", "One passport-size photo", "KRA PIN certificate"]),
        ["BOSA-DEP"] = L(20,
            ["Monthly deposits that grow your borrowing power — borrow up to 3 times your deposits",
             "Earn interest on deposits every year, declared at the annual general meeting",
             "Pay by check-off, standing order, M-Pesa or at the counter",
             "Refunded when you leave the SACCO, after 60 days' notice"],
            ["Be a registered member", "Contribute at least KES 1,000 every month"]),
        ["SHARES"] = L(30,
            ["Your stake in the SACCO: share capital earns an annual dividend",
             "One member, one vote at general meetings",
             "Can be transferred to another member through the SACCO"],
            ["Build up at least KES 10,000 in share capital — pay it all at once or in instalments"]),
        ["FD-6M"] = L(40,
            ["Fixed interest rate agreed when you invest", "Interest paid together with your principal at maturity",
             "Choose to roll over or pay out to your FOSA account at maturity"],
            ["An active FOSA account to fund the deposit", "At least KES 20,000"]),
        ["FD-12M"] = L(41,
            ["Our best fixed rate, for a 12-month term", "Interest paid together with your principal at maturity",
             "Rates for larger amounts can be negotiated with the branch"],
            ["An active FOSA account to fund the deposit", "At least KES 20,000"]),
    };

    public sealed record LoanDef(string Code, string Name, string Description, LoanCategory Category, Segment Segment, int RateBps, InterestMethod Method,
        decimal MinAmount, decimal MaxAmount, int MinTerm, int MaxTerm, decimal Multiplier, int MembershipMonths, int FeeBps, bool Guarantors, int MinGuarantors, PublicListing Listing);

    /// <summary>Listings for the loan products the lending seeder already creates (DEV-LOAN, EMG-LOAN, SAL-ADV).</summary>
    public static readonly IReadOnlyDictionary<string, (LoanCategory Category, PublicListing Listing)> ExistingLoans = new Dictionary<string, (LoanCategory, PublicListing)>
    {
        ["SAL-ADV"] = (LoanCategory.Fosa, L(10,
            ["For urgent needs between paydays — rent, school fees, medical bills", "No guarantors needed",
             "Recovered from your next salary", "Processed within one working day"],
            ["Salary paid through your FOSA account", "Latest payslip"],
            "Up to 90% of your net salary")),
        ["DEV-LOAN"] = (LoanCategory.Bosa, L(10,
            ["For development projects — building, land, business or household needs", "Interest charged on the reducing balance",
             "Repay over up to 48 months by check-off or standing order"],
            ["At least 6 months of membership with regular deposits", "Two guarantors who are members", "Latest payslip or proof of income"])),
        ["EMG-LOAN"] = (LoanCategory.Bosa, L(30,
            ["For emergencies — hospital bills, funerals, natural calamities", "Quick processing",
             "Repay over up to 12 months"],
            ["At least 3 months of membership", "Other loans up to date", "One guarantor who is a member"])),
    };

    /// <summary>Additional demo loan products, created by the lending seeder when missing.</summary>
    public static readonly IReadOnlyList<LoanDef> NewLoans =
    [
        new("LT-ADV", "Long-Term Salary Advance", "Salary-backed FOSA advance for commitments bigger than one month's pay.", LoanCategory.Fosa, Segment.Fosa,
            1400, InterestMethod.Flat, 5_000m, 400_000m, 2, 6, 0m, 3, 0, false, 0,
            L(20, ["For obligations bigger than one month's salary", "No guarantors needed", "Repay over up to 6 months from your salary"],
                ["Salary paid through your FOSA account for at least 3 months", "Latest payslip"], "Up to 4 times your net salary")),
        new("SAL-LOAN", "Salary Personal Loan", "Longer-term FOSA loan for salaried members, repaid from salary.", LoanCategory.Fosa, Segment.Fosa,
            1400, InterestMethod.ReducingBalance, 10_000m, 1_000_000m, 3, 36, 3m, 6, 100, true, 1,
            L(30, ["Borrow for bigger plans and repay over up to 36 months", "Interest on the reducing balance", "Top up once a third of the loan is repaid"],
                ["Salary paid through your FOSA account", "At least 6 months of membership", "One guarantor who is a member"])),
        new("EDU-LOAN", "School Fees Loan", "BOSA loan for secondary school, college and university fees.", LoanCategory.Bosa, Segment.Bosa,
            1200, InterestMethod.ReducingBalance, 5_000m, 500_000m, 1, 20, 3m, 6, 100, true, 1,
            L(20, ["Paid straight to the school or institution", "Up to 3 times your deposits", "Repay over up to 20 months"],
                ["Fee structure or invoice from the institution", "At least 6 months of membership", "One guarantor who is a member"])),
        new("GRP-LOAN", "Group (Chama) Loan", "For registered self-help groups, chamas and associations saving with the SACCO.", LoanCategory.Msme, Segment.Fosa,
            1400, InterestMethod.Flat, 20_000m, 2_000_000m, 3, 24, 5m, 2, 100, false, 0,
            L(10, ["Grow the group's projects with a loan based on its savings", "Group members guarantee one another", "Financial literacy training for group officials"],
                ["Group registration certificate and by-laws", "A group FOSA account with at least 2 months of regular savings", "IDs and passport photos of the group officials"],
                "Up to 5 times the group's savings")),
        new("BIZ-LOAN", "Business Loan", "For owners of small and medium businesses to finance stock, equipment or expansion.", LoanCategory.Msme, Segment.Fosa,
            1400, InterestMethod.ReducingBalance, 20_000m, 3_000_000m, 12, 48, 4m, 6, 100, true, 2,
            L(20, ["Working capital, stock or equipment for your business", "Repay over 12 to 48 months", "Business advisory support from our credit officers"],
                ["A registered business or trading licence", "At least 6 months of savings with the SACCO", "Business records or bank statements for the last 6 months", "Two guarantors or acceptable collateral"],
                "Up to 4 times your deposits")),
        new("AGRI-LOAN", "Farm Input Loan", "Seasonal loan for seeds, fertiliser and farm inputs, repaid from produce payments.", LoanCategory.Msme, Segment.Fosa,
            1200, InterestMethod.ReducingBalance, 5_000m, 300_000m, 3, 12, 4m, 4, 100, true, 1,
            L(30, ["Inputs when you need them, before harvest", "Repay from produce payments channelled through the SACCO", "Repay over up to 12 months"],
                ["An active FOSA account", "Produce payments channelled through the SACCO for at least 4 months", "One guarantor who is a member"])),
        new("ASSET-FIN", "Asset Finance", "Own a vehicle, tractor, motorcycle or equipment and repay over up to 48 months.", LoanCategory.Msme, Segment.Fosa,
            1400, InterestMethod.ReducingBalance, 50_000m, 5_000_000m, 6, 48, 0m, 6, 150, true, 1,
            L(40, ["Finance vehicles, tractors, motorcycles and business equipment", "The asset stays insured and logged in the SACCO's name until paid off", "Repay over up to 48 months"],
                ["Save at least 25% of the asset's price with the SACCO", "A proforma invoice from the dealer", "At least 6 months of membership"],
                "Up to 75% of the asset's price")),
    ];

    public static readonly IReadOnlyList<(string Name, string Description, string Icon)> Services =
    [
        ("Mobile banking", "Check balances, deposit, withdraw and apply for loans from your phone.", "mobile"),
        ("M-Pesa and Airtel Money", "Send money to and from your SACCO accounts at any time.", "mpesa"),
        ("SMS alerts", "An SMS for every deposit, withdrawal and loan repayment.", "sms"),
        ("Salary processing", "Have your salary paid through the SACCO and access it the same day.", "salary"),
        ("Standing orders", "Automate monthly deposits, loan repayments and bills.", "standing-order"),
        ("Banker's cheques", "Secure payment for school fees, land and other large purchases.", "cheque"),
        ("ATM card", "Withdraw from your FOSA account at partner ATMs countrywide.", "atm"),
        ("Safe custody", "Keep title deeds and important documents safe with us.", "safe-custody"),
        ("24/7 call centre", "Talk to us any time about your accounts and loans.", "support"),
    ];
}
