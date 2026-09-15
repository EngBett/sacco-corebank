namespace MockedEquity.API.Services;

/// <summary>Mock configuration — the credentials the mock will accept, and its timing behaviour.</summary>
public class JengaOptions
{
    public const string SectionName = "Jenga";

    /// <summary>Expected Api-Key header on authentication, and the PBKDF2 password for PIN checks.</summary>
    public string ApiKey { get; set; } = "local-api-key";

    public string MerchantCode { get; set; } = "0582910862";

    public string ConsumerSecret { get; set; } = "local-consumer-secret";

    public string ShortCode { get; set; } = "800800";

    /// <summary>Plaintext PIN the decrypted value must match.</summary>
    public string Pin { get; set; } = "2580";

    public string OrganizationUsername { get; set; } = "TESTAPIUSER";

    public string OrganizationPassword { get; set; } = "local-org-password";

    public int TokenLifetimeMinutes { get; set; } = 15;

    public int CallbackDelaySecondsMin { get; set; } = 2;

    public int CallbackDelaySecondsMax { get; set; } = 4;

    /// <summary>
    /// When true (the default) the mock decrypts and compares the PIN/password, so a client that
    /// implements the AES-GCM envelope incorrectly is rejected here rather than in UAT. Turn it off
    /// to exercise the endpoints without a correct cipher implementation.
    /// </summary>
    public bool ValidateEncryptedCredentials { get; set; } = true;
}
