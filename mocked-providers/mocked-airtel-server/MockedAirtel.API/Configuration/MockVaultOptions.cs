namespace MockedAirtel.API.Configuration;

public class MockVaultOptions
{
    public const string SectionName = "Vault";

    public string VaultUri { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string MountPoint { get; set; } = "";
    public string GithubToken { get; set; } = "";
}
