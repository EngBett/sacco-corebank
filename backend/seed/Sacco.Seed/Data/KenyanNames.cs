namespace Sacco.Seed.Data;

/// <summary>
/// Realistic (but fictional) Kenyan demo people. National ID numbers are in the 90xxxxxx range,
/// which is not an issued range, and phone numbers use the 2547 00 xxx xxx block, so nothing here
/// can collide with a real person. See docs/testing/seed-data-strategy.md.
/// </summary>
public sealed record DemoPerson(string MemberNumber, string FirstName, string LastName, string Gender, string NationalId, string Phone, string Email, DateOnly DateOfBirth, string County, string Occupation, string Employer, string Kyc = "Verified")
{
    /// <summary>Members with ledger accounts and transaction history: verified or suspended (suspension happens after joining).</summary>
    public bool HasAccounts => Kyc is "Verified" or "Suspended";
}

public static class KenyanNames
{
    public static readonly IReadOnlyList<DemoPerson> Members =
    [
        new("M00001", "Wanjiru", "Kamau",      "F", "90123401", "254700100001", "wanjiru.kamau@example.co.ke",  new(1985, 3, 14), "Kiambu",    "Teacher",              "Teachers Service Commission"),
        new("M00002", "Otieno",  "Odhiambo",   "M", "90123402", "254700100002", "otieno.odhiambo@example.co.ke", new(1979, 11, 2), "Kisumu",    "Nurse",                "Kisumu County Government"),
        new("M00003", "Akinyi",  "Owino",      "F", "90123403", "254700100003", "akinyi.owino@example.co.ke",   new(1990, 7, 21), "Nairobi",   "Accountant",           "Kenya Power"),
        new("M00004", "Kipchoge","Rotich",     "M", "90123404", "254700100004", "kipchoge.rotich@example.co.ke", new(1982, 1, 30), "Uasin Gishu","Farmer",              "Self-employed"),
        new("M00005", "Njeri",   "Mwangi",     "F", "90123405", "254700100005", "njeri.mwangi@example.co.ke",   new(1993, 9, 9),  "Nyeri",     "Shop owner",           "Self-employed"),
        new("M00006", "Mutua",   "Musyoka",    "M", "90123406", "254700100006", "mutua.musyoka@example.co.ke",  new(1975, 5, 5),  "Machakos",  "Civil servant",        "Ministry of Interior"),
        new("M00007", "Chebet",  "Kiprop",     "F", "90123407", "254700100007", "chebet.kiprop@example.co.ke",  new(1988, 12, 12),"Nandi",     "Police officer",       "National Police Service"),
        new("M00008", "Omondi",  "Ouma",       "M", "90123408", "254700100008", "omondi.ouma@example.co.ke",    new(1996, 4, 18), "Siaya",     "Boda boda operator",   "Self-employed"),
        new("M00009", "Wambui",  "Ndungu",     "F", "90123409", "254700100009", "wambui.ndungu@example.co.ke",  new(1983, 8, 27), "Murang'a",  "Lecturer",             "Murang'a University"),
        new("M00010", "Barasa",  "Wekesa",     "M", "90123410", "254700100010", "barasa.wekesa@example.co.ke",  new(1970, 2, 3),  "Bungoma",   "Retired teacher",      "Retired"),
        new("M00011", "Achieng", "Onyango",    "F", "90123411", "254700100011", "achieng.onyango@example.co.ke", new(1999, 6, 15), "Homa Bay",  "Student",              "Maseno University"),
        new("M00012", "Kimani",  "Githinji",   "M", "90123412", "254700100012", "kimani.githinji@example.co.ke", new(1986, 10, 8), "Nakuru",    "Driver",               "Nakuru Water Company"),
        new("M00013", "Nekesa",  "Simiyu",     "F", "90123413", "254700100013", "nekesa.simiyu@example.co.ke",  new(1991, 3, 3),  "Trans Nzoia","Clinical officer",    "Kitale County Hospital"),
        new("M00014", "Kariuki", "Waweru",     "M", "90123414", "254700100014", "kariuki.waweru@example.co.ke", new(1968, 7, 7),  "Kirinyaga", "Tea farmer",           "Self-employed"),
        new("M00015", "Auma",    "Ochieng",    "F", "90123415", "254700100015", "auma.ochieng@example.co.ke",   new(1994, 11, 25),"Nairobi",   "Software developer",   "Safaricom PLC"),
        new("M00016", "Lemayian","Ole Sankale","M", "90123416", "254700100016", "lemayian.sankale@example.co.ke", new(1980, 9, 19), "Kajiado",  "Livestock trader",     "Self-employed"),
        new("M00017", "Zawadi",  "Mwakio",     "F", "90123417", "254700100017", "zawadi.mwakio@example.co.ke",  new(1987, 1, 1),  "Taita Taveta","Hotel manager",      "Sarova Hotels", Kyc: "PendingVerification"),
        new("M00018", "Hassan",  "Abdi",       "M", "90123418", "254700100018", "hassan.abdi@example.co.ke",    new(1992, 5, 23), "Garissa",   "Trader",               "Self-employed", Kyc: "PendingVerification"),
        new("M00019", "Wairimu", "Gitau",      "F", "90123419", "254700100019", "wairimu.gitau@example.co.ke",  new(1977, 4, 4),  "Kiambu",    "Pharmacist",           "Goodlife Pharmacy", Kyc: "Suspended"),
        new("M00020", "Ochieng", "Otieno",     "M", "90123420", "254700100020", "ochieng.otieno@example.co.ke", new(1998, 8, 8),  "Kisumu",    "Intern",               "Kisumu County Government", Kyc: "Rejected"),
    ];

    /// <summary>A prospective member who applied through the public site and is awaiting staff review.</summary>
    public static readonly DemoPerson PendingApplicant = new("", "Faith", "Chepkorir", "F", "90123499", "254700100099", "faith.chepkorir@example.co.ke", new(1995, 2, 14), "Kericho", "Agronomist", "Kenya Tea Development Agency", Kyc: "Applicant");
}
