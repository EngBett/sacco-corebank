using Sacco.Modules.Reporting.Application;
using Shouldly;

namespace Sacco.UnitTests.Reporting;

/// <summary>The digest PDF is rendered from an HTML string, so a tenant logo on the API's own origin must arrive inlined.</summary>
public class DigestLogoTests : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("wwwroot").FullName;

    public DigestLogoTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "tenant-assets", "demo"));
        File.WriteAllBytes(Path.Combine(_webRoot, "tenant-assets", "demo", "logo.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_webRoot)!, "secret.png"), "not yours");
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public void A_root_relative_logo_in_wwwroot_is_inlined_as_a_data_uri()
        => DigestLogo.Resolve("/tenant-assets/demo/logo.png", _webRoot).ShouldBe("data:image/png;base64,iVBORw==");

    [Fact]
    public void An_absolute_https_logo_is_left_for_chromium_to_fetch()
        => DigestLogo.Resolve("https://cdn.example/logo.png", _webRoot).ShouldBe("https://cdn.example/logo.png");

    [Fact]
    public void Without_a_web_root_only_absolute_logos_survive()
    {
        DigestLogo.Resolve("/tenant-assets/demo/logo.png", null).ShouldBeNull();
        DigestLogo.Resolve("https://cdn.example/logo.png", null).ShouldBe("https://cdn.example/logo.png");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/tenant-assets/demo/missing.png")]
    [InlineData("/../secret.png")]
    [InlineData("//evil.example/logo.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/tenant-assets/demo/logo.txt")]
    public void Anything_else_falls_back_to_the_initials_mark(string? logoUrl)
        => DigestLogo.Resolve(logoUrl, _webRoot).ShouldBeNull();
}
