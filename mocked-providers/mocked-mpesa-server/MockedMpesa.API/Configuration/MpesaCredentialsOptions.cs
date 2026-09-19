namespace MockedMpesa.API.Configuration;

public class MpesaCredentialsOptions
{
    public const string SectionName = "Credentials";

    public string ConsumerKey { get; set; } = string.Empty;
    public string ConsumerSecret { get; set; } = string.Empty;
    public string? BearerToken { get; set; }
}
