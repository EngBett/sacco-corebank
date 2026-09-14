using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sacco.Shared.Auth;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Application;

public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";
    /// <summary>Header carrying the tenant slug (used by the BFF and by tests). Host-based resolution is tried first.</summary>
    public string HeaderName { get; set; } = "X-Tenant";
    /// <summary>Slug to assume when nothing else resolves. Only honoured outside Production.</summary>
    public string? DefaultTenantSlug { get; set; }
    /// <summary>Route prefixes after which the next path segment is the tenant slug, e.g. "/api/payments/webhooks/" for provider callbacks.</summary>
    public List<string> PathTenantPrefixes { get; set; } = ["/api/payments/webhooks/"];
}

/// <summary>
/// Resolves the tenant for the request, in order: custom domain / subdomain, X-Tenant header,
/// then the configured default (non-production only). Requests to tenant-agnostic paths
/// (health, OpenAPI docs) pass through without a tenant.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<TenancyOptions> options, IHostEnvironment env)
{
    private static readonly string[] TenantAgnosticPrefixes = ["/health", "/openapi", "/scalar", "/.well-known", "/connect", "/account"];

    public async Task InvokeAsync(HttpContext context, ITenantDirectory directory, ITenantContext tenantContext)
    {
        var path = context.Request.Path;
        if (TenantAgnosticPrefixes.Any(p => path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        var opts = options.Value;
        TenantInfo? tenant = null;

        foreach (var prefix in opts.PathTenantPrefixes)
        {
            if (!path.HasValue || !path.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var slug = path.Value[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            tenant = string.IsNullOrEmpty(slug) ? null : await directory.FindBySlugAsync(slug, context.RequestAborted);
            if (tenant is null)
            {
                await Reject(context, StatusCodes.Status404NotFound, "tenant.unknown", "Unknown tenant in callback path");
                return;
            }
            break;
        }

        var host = context.Request.Host.Host;
        if (tenant is null && !string.IsNullOrEmpty(host) && host != "localhost" && !IsIpAddress(host))
            tenant = await directory.FindByHostAsync(host, context.RequestAborted);

        if (tenant is null && context.Request.Headers.TryGetValue(opts.HeaderName, out var headerSlug) && !string.IsNullOrWhiteSpace(headerSlug))
        {
            tenant = await directory.FindBySlugAsync(headerSlug.ToString(), context.RequestAborted);
            if (tenant is null)
            {
                await Reject(context, StatusCodes.Status400BadRequest, "tenant.unknown", $"Tenant '{headerSlug}' does not exist");
                return;
            }
        }

        // Hub connections carry a hub ticket instead of a bearer token, and browsers cannot add headers to a
        // WebSocket upgrade — so on hub paths the ticket (which names the tenant) is the source of truth.
        if (context.User.Identity?.IsAuthenticated != true && path.StartsWithSegments(HubTicketAuth.PathPrefix))
        {
            var result = await context.AuthenticateAsync(HubTicketAuth.Scheme);
            if (result.Succeeded) context.User = result.Principal;
        }

        // An authenticated token is bound to exactly one tenant; it must agree with whatever the request claims.
        var claimSlug = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst("tenant")?.Value : null;
        if (!string.IsNullOrWhiteSpace(claimSlug))
        {
            if (tenant is null) tenant = await directory.FindBySlugAsync(claimSlug, context.RequestAborted);
            else if (!string.Equals(tenant.Slug, claimSlug, StringComparison.OrdinalIgnoreCase))
            {
                await Reject(context, StatusCodes.Status403Forbidden, "tenant.mismatch", "Token is issued for a different tenant");
                return;
            }
        }

        if (tenant is null && !env.IsProduction() && !string.IsNullOrWhiteSpace(opts.DefaultTenantSlug))
            tenant = await directory.FindBySlugAsync(opts.DefaultTenantSlug, context.RequestAborted);

        if (tenant is null || !tenant.IsActive)
        {
            await Reject(context, StatusCodes.Status400BadRequest, "tenant.unresolved", "Tenant could not be resolved");
            return;
        }

        ((TenantContext)tenantContext).Set(tenant.Id, tenant.Slug);
        context.Items["Tenant"] = tenant;
        await next(context);
    }

    private static async Task Reject(HttpContext context, int status, string type, string title)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { type, title, status });
    }

    private static bool IsIpAddress(string host) => System.Net.IPAddress.TryParse(host, out _);
}
