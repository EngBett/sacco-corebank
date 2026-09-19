namespace Sacco.Shared.Domain;

/// <summary>
/// How a product is presented on the tenant's public website, set by the SACCO rather than derived from the product's
/// financial rules. Features and requirements are short, plain lines ("Deposits and withdrawals at any time", "Copy of
/// your national ID"); <see cref="AmountNote"/> replaces the computed "up to …" line when the real limit is expressed some
/// other way ("Up to 90% of your net salary"). Owned by savings and loan products alike.
/// </summary>
public sealed class PublicListing
{
    public const int MaxLines = 12;
    public const int MaxLineLength = 200;

    public bool ShowOnPublicSite { get; private set; } = true;
    public int DisplayOrder { get; private set; }
    public List<string> Features { get; private set; } = [];
    public List<string> Requirements { get; private set; } = [];
    public string? AmountNote { get; private set; }
    public string? ApplicationFormUrl { get; private set; }

    public static PublicListing Create(bool showOnPublicSite, int displayOrder, IEnumerable<string>? features, IEnumerable<string>? requirements, string? amountNote, string? applicationFormUrl)
    {
        var listing = new PublicListing
        {
            ShowOnPublicSite = showOnPublicSite,
            DisplayOrder = displayOrder,
            Features = Lines(features, "features"),
            Requirements = Lines(requirements, "requirements"),
            AmountNote = Text(amountNote, "amount note"),
            ApplicationFormUrl = Url(applicationFormUrl),
        };
        if (displayOrder is < 0 or > 10_000)
            throw new DomainRuleException("product.listing.order_invalid", "Display order must be between 0 and 10000.");
        return listing;
    }

    private static List<string> Lines(IEnumerable<string>? lines, string what)
    {
        var clean = (lines ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (clean.Count > MaxLines)
            throw new DomainRuleException("product.listing.too_many_lines", $"Keep {what} to {MaxLines} lines or fewer.");
        if (clean.Any(l => l.Length > MaxLineLength))
            throw new DomainRuleException("product.listing.line_too_long", $"Each line in {what} must be {MaxLineLength} characters or fewer.");
        return clean;
    }

    private static string? Text(string? value, string what)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean is { Length: > MaxLineLength })
            throw new DomainRuleException("product.listing.line_too_long", $"The {what} must be {MaxLineLength} characters or fewer.");
        return clean;
    }

    /// <summary>An absolute http(s) URL or a root-relative path (e.g. a form served from the API's tenant assets).</summary>
    private static string? Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        var ok = (clean.StartsWith('/') && !clean.StartsWith("//"))
            || (Uri.TryCreate(clean, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
        if (!ok || clean.Length > 500)
            throw new DomainRuleException("product.listing.form_url_invalid", "The application form link must be an http(s) address or a path starting with '/'.");
        return clean;
    }
}
