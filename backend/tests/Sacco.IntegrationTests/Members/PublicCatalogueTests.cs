using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Endpoints;
using Sacco.Modules.Platform.Endpoints;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Seed.Data;
using Shouldly;

namespace Sacco.IntegrationTests.Members;

[Collection(DatabaseCollection.Name)]
public sealed class PublicCatalogueTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Public_product_catalogues_are_anonymous_and_tenant_scoped()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        var savings = await client.GetStringAsync("/api/public/products/savings");
        savings.ShouldContain("FOSA-CUR");
        savings.Contains("controlGlAccountCode").ShouldBeFalse("internal GL mapping is not public");
        var loans = await client.GetStringAsync("/api/public/products/loans");
        loans.ShouldContain("DEV-LOAN");
        loans.ShouldContain("depositMultiplier");
        var branding = await client.GetStringAsync("/api/public/tenant/branding");
        branding.ShouldContain("Icodeio SACCO");
        branding.ShouldContain(DemoTenant.LogoUrl);

        // The branding logo is a root-relative file on the API origin, served without a tenant header or token.
        var logo = await _factory.CreateClient().GetAsync(DemoTenant.LogoUrl);
        logo.StatusCode.ShouldBe(HttpStatusCode.OK);
        logo.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        branding.ShouldContain(DemoTenant.FaviconUrl);
        (await _factory.CreateClient().GetAsync(DemoTenant.FaviconUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var modeLogo in new[] { DemoTenant.LightModeLogoUrl, DemoTenant.DarkModeLogoUrl })
        {
            branding.ShouldContain(modeLogo);
            (await _factory.CreateClient().GetAsync(modeLogo)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var noTenant = _factory.CreateClient();
        (await noTenant.GetAsync("/api/public/products/loans")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Each_sacco_controls_what_its_website_lists_and_how()
    {
        var site = _factory.CreateClient();
        site.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);

        // Seeded listings: FOSA, BOSA and MSME loans with features, requirements and amount notes; services in order.
        var loans = await (await site.GetAsync("/api/public/products/loans")).ReadAs<List<PublicLoanProduct>>();
        loans.Select(l => l.Category).Distinct().ShouldBe([LoanCategory.Fosa, LoanCategory.Bosa, LoanCategory.Msme], ignoreOrder: true);
        var advance = loans.Single(l => l.Code == "SAL-ADV");
        advance.AmountNote.ShouldBe("Up to 90% of your net salary");
        advance.Requirements.ShouldContain("Salary paid through your FOSA account");
        loans.Where(l => l.Category == LoanCategory.Msme).Select(l => l.DisplayOrder).ShouldBeInOrder();

        var services = await (await site.GetAsync("/api/public/services")).ReadAs<List<PublicServiceResponse>>();
        services.ShouldNotBeEmpty();
        services.Select(s => s.DisplayOrder).ShouldBeInOrder();

        // A product manager rewrites a listing and moves it; the website shows exactly that.
        var products = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.ProductsManage, Permissions.Savings.ProductsManage, Permissions.Savings.View);
        var updated = await (await products.PutAsJsonAsync("/api/loans/products/EDU-LOAN/listing", new LoanProductListingRequest(LoanCategory.Msme,
            new ProductListing(true, 5, ["Fees paid straight to the school"], ["Fee structure"], "Up to KES 500,000", "/tenant-assets/demo/forms/school-fees.pdf")), HttpExtensions.JsonOptions))
            .ReadAs<LoanProductResponse>();
        updated.Category.ShouldBe(LoanCategory.Msme);
        var edu = (await (await site.GetAsync("/api/public/products/loans")).ReadAs<List<PublicLoanProduct>>()).Single(l => l.Code == "EDU-LOAN");
        edu.Category.ShouldBe(LoanCategory.Msme);
        edu.Features.ShouldBe(["Fees paid straight to the school"]);
        edu.ApplicationFormUrl.ShouldBe("/tenant-assets/demo/forms/school-fees.pdf");

        // Hiding a savings product removes it from the website without deactivating it.
        var fd = await (await products.GetAsync("/api/savings/products")).ReadAs<List<ProductResponse>>();
        var fd6 = fd.Single(p => p.Code == "FD-6M");
        await products.PutAsJsonAsync("/api/savings/products/FD-6M/listing", fd6.Listing with { ShowOnPublicSite = false }, HttpExtensions.JsonOptions);
        (await (await site.GetAsync("/api/public/products/savings")).ReadAs<List<PublicSavingsProduct>>()).ShouldNotContain(p => p.Code == "FD-6M");
        (await (await products.GetAsync("/api/savings/products")).ReadAs<List<ProductResponse>>()).Single(p => p.Code == "FD-6M").IsActive.ShouldBeTrue();
        await products.PutAsJsonAsync("/api/savings/products/FD-6M/listing", fd6.Listing, HttpExtensions.JsonOptions);

        // Bad content is refused with the rule's code; managing listings needs the product permission.
        var bad = await products.PutAsJsonAsync("/api/loans/products/EDU-LOAN/listing", new LoanProductListingRequest(LoanCategory.Bosa,
            new ProductListing(true, 5, [], [], null, "javascript:alert(1)")), HttpExtensions.JsonOptions);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadAsStringAsync()).ShouldContain("product.listing.form_url_invalid");
        (await _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Loans.View).PutAsJsonAsync("/api/loans/products/EDU-LOAN/listing",
            new LoanProductListingRequest(LoanCategory.Bosa, new ProductListing(true, 5, [], [], null, null)), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Services are managed by tenant administrators; inactive ones stay off the website.
        var admin = _factory.ClientAs(DemoTenant.Users.Admin, Permissions.Admin.TenantManage);
        var created = await (await admin.PostAsJsonAsync("/api/admin/public-services", new SavePublicServiceRequest("Diaspora banking", "Save and invest from abroad.", "diaspora", 95, IsActive: false), HttpExtensions.JsonOptions))
            .ReadAs<PublicServiceResponse>();
        (await (await site.GetAsync("/api/public/services")).ReadAs<List<PublicServiceResponse>>()).ShouldNotContain(s => s.Id == created.Id);
        await admin.PutAsJsonAsync($"/api/admin/public-services/{created.Id}", new SavePublicServiceRequest("Diaspora banking", "Save and invest from abroad.", "diaspora", 95, IsActive: true), HttpExtensions.JsonOptions);
        (await (await site.GetAsync("/api/public/services")).ReadAs<List<PublicServiceResponse>>()).ShouldContain(s => s.Name == "Diaspora banking");
        (await admin.DeleteAsync($"/api/admin/public-services/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
