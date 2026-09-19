namespace Sacco.Modules.Reporting.Application;

/// <summary>
/// Makes a tenant logo usable inside the digest PDF. Chromium renders the HTML from a string with no base URL, so a
/// root-relative logo (<c>/tenant-assets/demo/logo.png</c>, served from the API's wwwroot) would never load: it is
/// inlined as a data URI instead. Absolute http(s) URLs are left for Chromium to fetch; anything else is dropped so the
/// header falls back to the initials mark.
/// </summary>
public static class DigestLogo
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
    };

    public static string? Resolve(string? logoUrl, string? webRootPath)
    {
        if (string.IsNullOrWhiteSpace(logoUrl) || logoUrl.StartsWith("//")) return null;
        // Checked before Uri.TryCreate: on Linux/macOS "/tenant-assets/x.png" parses as an absolute file:// URI.
        if (!logoUrl.StartsWith('/'))
            return Uri.TryCreate(logoUrl, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" ? logoUrl : null;

        if (webRootPath is null) return null;
        var root = Path.GetFullPath(webRootPath);
        var file = Path.GetFullPath(Path.Combine(root, logoUrl.TrimStart('/')));
        // No escaping wwwroot through "..".
        if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        if (!MediaTypes.TryGetValue(Path.GetExtension(file), out var mediaType) || !File.Exists(file)) return null;
        return $"data:{mediaType};base64,{Convert.ToBase64String(File.ReadAllBytes(file))}";
    }
}
