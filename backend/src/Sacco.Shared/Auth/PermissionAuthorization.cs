using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sacco.Shared.Tenancy;

namespace Sacco.Shared.Auth;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Satisfies a <see cref="PermissionRequirement"/> by resolving the user's effective permissions
/// server-side for the current tenant. Never inspects role names.
/// </summary>
public sealed class PermissionAuthorizationHandler(
    ICurrentUser currentUser,
    ITenantContext tenant,
    IPermissionResolver resolver) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!currentUser.IsAuthenticated || !tenant.HasTenant) return;

        var permissions = await resolver.GetPermissionsAsync(tenant.TenantId, currentUser.UserId, CancellationToken.None);
        if (permissions.Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}

/// <summary>
/// Any policy name that is a known permission string resolves to a policy requiring that
/// permission, so endpoints can write <c>.RequirePermission(Permissions.Loans.Approve)</c>
/// without registering every policy by hand.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (Permissions.IsKnown(policyName))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
        }
        return await _fallback.GetPolicyAsync(policyName);
    }
}

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }

    /// <summary>Requires the caller to hold the given granular permission in the current tenant.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!Permissions.IsKnown(permission))
            throw new ArgumentException($"'{permission}' is not a registered permission. Add it to Permissions.All.", nameof(permission));
        return builder.RequireAuthorization(permission);
    }
}
