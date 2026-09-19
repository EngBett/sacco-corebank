using Sacco.Modules.Platform.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Shared;

public class PublicListingTests
{
    [Fact]
    public void Trims_lines_and_drops_blank_ones()
    {
        var listing = PublicListing.Create(true, 10, ["  No guarantors needed ", "", "   "], ["Copy of your ID"], "  Up to 90% of your net salary ", null);
        listing.Features.ShouldBe(["No guarantors needed"]);
        listing.AmountNote.ShouldBe("Up to 90% of your net salary");
    }

    [Theory]
    [InlineData("https://sacco.example/forms/loan.pdf", true)]
    [InlineData("/tenant-assets/demo/forms/loan.pdf", true)]
    [InlineData("//evil.example/form.pdf", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ftp://sacco.example/form.pdf", false)]
    public void Application_form_link_must_be_http_or_site_relative(string url, bool ok)
    {
        if (ok) PublicListing.Create(true, 0, [], [], null, url).ApplicationFormUrl.ShouldBe(url);
        else Should.Throw<DomainRuleException>(() => PublicListing.Create(true, 0, [], [], null, url)).Code.ShouldBe("product.listing.form_url_invalid");
    }

    [Fact]
    public void Keeps_lists_short_enough_to_read()
    {
        Should.Throw<DomainRuleException>(() => PublicListing.Create(true, 0, Enumerable.Repeat("x", PublicListing.MaxLines + 1), [], null, null))
            .Code.ShouldBe("product.listing.too_many_lines");
        Should.Throw<DomainRuleException>(() => PublicListing.Create(true, 0, [new string('x', PublicListing.MaxLineLength + 1)], [], null, null))
            .Code.ShouldBe("product.listing.line_too_long");
    }

    [Fact]
    public void A_public_service_needs_a_known_icon()
    {
        Should.Throw<DomainRuleException>(() => PublicService.Create(Guid.NewGuid(), Guid.NewGuid(), "Mobile banking", null, "rocket", 10))
            .Code.ShouldBe("platform.service.icon_invalid");
        PublicService.Create(Guid.NewGuid(), Guid.NewGuid(), " Mobile banking ", " ", "mobile", 10).Description.ShouldBeNull();
    }
}
