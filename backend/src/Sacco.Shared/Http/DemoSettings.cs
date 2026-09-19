namespace Sacco.Shared.Http;

/// <summary>
/// Configuration section <c>Demo</c>. When enabled, the portal and the public site show a notice that the site is a
/// demonstration of the platform and can be bought — one switch for a sales/demo deployment, independent of the
/// per-integration sandbox modes (payments, SMS, bureau) and of the hosting environment.
/// </summary>
public sealed class DemoSettings
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Shown in the banner. A tenant-neutral default is used when this is left empty.</summary>
    public string? Message { get; set; }

    /// <summary>Where "find out more" points — a sales page, or a mailto: link.</summary>
    public string? PurchaseUrl { get; set; }

    public string? ContactEmail { get; set; }

    public DemoNotice? ToNotice() => Enabled
        ? new DemoNotice(
            string.IsNullOrWhiteSpace(Message) ? "This is a demonstration site with sample data. The platform is available to buy." : Message.Trim(),
            string.IsNullOrWhiteSpace(PurchaseUrl) ? null : PurchaseUrl.Trim(),
            string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail.Trim())
        : null;
}

/// <summary>Null unless this deployment is a demo, so both websites can simply check for its presence.</summary>
public sealed record DemoNotice(string Message, string? PurchaseUrl, string? ContactEmail);
