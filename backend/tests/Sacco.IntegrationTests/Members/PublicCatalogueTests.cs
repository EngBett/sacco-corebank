using System.Net;
using Sacco.IntegrationTests.Infrastructure;
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
        branding.ShouldContain("Demo SACCO");

        var noTenant = _factory.CreateClient();
        (await noTenant.GetAsync("/api/public/products/loans")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
