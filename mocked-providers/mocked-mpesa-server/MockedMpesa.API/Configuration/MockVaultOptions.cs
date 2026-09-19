namespace MockedMpesa.API.Configuration;

public class MockVaultOptions
{
    public const string SectionName = "Vault";

    public string VaultUri { get; set; } = "#vault";
    public string MountPoint { get; set; } = "secret";
    public string UserName { get; set; } = "#userpass";
    public string Password { get; set; } = "";
    public string GithubToken { get; set; } = "";
}
