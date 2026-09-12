namespace Sacco.Modules.Platform.Domain;

/// <summary>A licensed SACCO. Branding is data, not a deployment (ADR 0006).</summary>
public class Tenant
{
    private Tenant() { }

    public Guid Id { get; private set; }
    /// <summary>URL-safe identifier used as the subdomain, e.g. "demo" → demo.sacco.example.</summary>
    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string ShortName { get; private set; } = string.Empty;
    /// <summary>SASRA licence number, if licensed.</summary>
    public string? SasraLicenceNumber { get; private set; }
    public string? CustomDomain { get; private set; }
    public TenantBranding Branding { get; private set; } = new();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant Create(Guid id, string slug, string name, string shortName, TenantBranding branding, string? sasraLicenceNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("Slug must be letters, digits, or hyphens.", nameof(slug));
        return new Tenant
        {
            Id = id,
            Slug = slug.ToLowerInvariant(),
            Name = name,
            ShortName = shortName,
            Branding = branding,
            SasraLicenceNumber = sasraLicenceNumber,
            IsActive = true,
            CreatedAt = now,
        };
    }

    public void UpdateBranding(TenantBranding branding) => Branding = branding;
    public void UpdateDetails(string name, string shortName, string? sasraLicenceNumber, string? customDomain)
    {
        Name = name; ShortName = shortName; SasraLicenceNumber = sasraLicenceNumber; CustomDomain = customDomain;
    }
    public void Deactivate() => IsActive = false;
}

/// <summary>Owned value object — persisted as columns on the tenant row.</summary>
public class TenantBranding
{
    public string PrimaryColor { get; set; } = "#0f766e";
    public string SecondaryColor { get; set; } = "#f59e0b";
    public string AccentColor { get; set; } = "#1d4ed8";
    public string? LogoUrl { get; set; }
    public string Tagline { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
}
