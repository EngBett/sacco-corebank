using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sacco.Shared.Tenancy;

namespace Sacco.Shared.Auth;

/// <summary>Satisfied when the caller holds any one of <see cref="Permissions"/> (usually just one).</summary>
public sealed class PermissionRequirement(params string[] permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
    public string Permission => Permissions[0];
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
        if (requirement.Permissions.Any(permissions.Contains))
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

    /// <summary>Separates alternatives in a policy name built by <c>RequireAnyPermission</c>.</summary>
    public const char AnySeparator = '|';

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var names = policyName.Split(AnySeparator);
        if (names.All(Permissions.IsKnown))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(names))
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

    /// <summary>Requires the caller to hold at least one of the given permissions in the current tenant.</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        foreach (var permission in permissions)
            if (!Permissions.IsKnown(permission))
                throw new ArgumentException($"'{permission}' is not a registered permission. Add it to Permissions.All.", nameof(permissions));
        return builder.RequireAuthorization(string.Join(PermissionPolicyProvider.AnySeparator, permissions));
    }
}
