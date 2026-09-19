using Sacco.Shared.Domain;

namespace Sacco.Shared.Http;

/// <summary>How a product appears on the public website (ADR 0017). Shared by savings and loan products.</summary>
public sealed record ProductListing(bool ShowOnPublicSite, int DisplayOrder, IReadOnlyList<string> Features, IReadOnlyList<string> Requirements, string? AmountNote, string? ApplicationFormUrl)
{
    public static ProductListing From(PublicListing l) => new(l.ShowOnPublicSite, l.DisplayOrder, l.Features, l.Requirements, l.AmountNote, l.ApplicationFormUrl);

    public PublicListing ToDomain() => PublicListing.Create(ShowOnPublicSite, DisplayOrder, Features, Requirements, AmountNote, ApplicationFormUrl);
}
