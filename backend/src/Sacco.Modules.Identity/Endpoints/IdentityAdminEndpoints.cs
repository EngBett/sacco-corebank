using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Endpoints;

public sealed record RoleResponse(Guid Id, string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions);
public sealed record SaveRoleRequest(string Name, string Description, IReadOnlyList<string> Permissions);
public sealed record UserResponse(Guid Id, string UserName, string Email, string DisplayName, string? PhoneNumber, bool IsActive, bool MustChangePassword, DateTimeOffset? LastLoginAt, IReadOnlyList<Guid> RoleIds);
public sealed record CreateUserRequest(string UserName, string Email, string DisplayName, string? PhoneNumber, string Password, IReadOnlyList<Guid> RoleIds);
public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record MeResponse(Guid Id, string UserName, string DisplayName, string Email, string TenantSlug, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed class IdentityAdminEndpoints : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").WithTags("Current user");

        me.MapGet("", async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> (ICurrentUser user, ITenantContext tenant, IPermissionResolver resolver, UserService users, CancellationToken ct) =>
        {
            var u = await users.FindActiveAsync(tenant.TenantId, user.UserId, ct);
            if (u is null) return TypedResults.Unauthorized();
            var perms = await resolver.GetPermissionsAsync(tenant.TenantId, user.UserId, ct);
            return TypedResults.Ok(new MeResponse(u.Id, u.UserName, u.DisplayName, u.Email, tenant.TenantSlug, u.RoleNames, perms.OrderBy(p => p).ToList()));
        }).RequireAuthorization().WithName("GetMe");

        me.MapPost("/change-password", async (ChangePasswordRequest req, ICurrentUser user, UserService users, CancellationToken ct) =>
        {
            await users.ChangePasswordAsync(user.UserId, req.CurrentPassword, req.NewPassword, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithName("ChangeMyPassword");

        var admin = app.MapGroup("/api/admin").WithTags("Identity admin");

        admin.MapGet("/permissions", () => TypedResults.Ok(Permissions.All))
            .RequirePermission(Permissions.Admin.RolesManage).WithName("ListPermissions");

        admin.MapGet("/roles", async (IdentityDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => ToResponse(r)).ToListAsync(ct)))
            .RequirePermission(Permissions.Admin.RolesManage).WithName("ListRoles");

        admin.MapPost("/roles", async (SaveRoleRequest req, RoleService roles, ICurrentUser user, CancellationToken ct) =>
        {
            var role = await roles.CreateAsync(null, req.Name, req.Description, req.Permissions, user.UserId, false, ct);
            return TypedResults.Created($"/api/admin/roles/{role.Id}", ToResponse(role));
        }).RequirePermission(Permissions.Admin.RolesManage).WithName("CreateRole");

        admin.MapPut("/roles/{id:guid}", async (Guid id, SaveRoleRequest req, RoleService roles, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await roles.UpdateAsync(id, req.Name, req.Description, req.Permissions, user.UserId, ct))))
            .RequirePermission(Permissions.Admin.RolesManage).WithName("UpdateRole");

        admin.MapDelete("/roles/{id:guid}", async (Guid id, RoleService roles, ICurrentUser user, CancellationToken ct) =>
        {
            await roles.DeleteAsync(id, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.RolesManage).WithName("DeleteRole");

        admin.MapGet("/users", async (IdentityDbContext db, CancellationToken ct) =>
            TypedResults.Ok((await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Admin.UsersManage).WithName("ListUsers");

        admin.MapGet("/users/{id:guid}", async Task<Results<Ok<UserResponse>, NotFound>> (Guid id, IdentityDbContext db, CancellationToken ct) =>
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return u is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(u));
        }).RequirePermission(Permissions.Admin.UsersManage).WithName("GetUser");

        admin.MapPost("/users", async (CreateUserRequest req, UserService users, ICurrentUser user, CancellationToken ct) =>
        {
            var created = await users.CreateAsync(null, req.UserName, req.Email, req.DisplayName, req.PhoneNumber, req.Password, req.RoleIds, user.UserId, mustChangePassword: true, ct);
            return TypedResults.Created($"/api/admin/users/{created.Id}", ToResponse(created));
        }).RequirePermission(Permissions.Admin.UsersManage).WithName("CreateUser");

        admin.MapPut("/users/{id:guid}/roles", async (Guid id, SetUserRolesRequest req, UserService users, ICurrentUser user, CancellationToken ct) =>
        {
            await users.SetRolesAsync(id, req.RoleIds, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.UsersManage).WithName("SetUserRoles");

        admin.MapPost("/users/{id:guid}/deactivate", async (Guid id, UserService users, ICurrentUser user, CancellationToken ct) =>
        {
            await users.SetActiveAsync(id, false, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.UsersManage).WithName("DeactivateUser");

        admin.MapPost("/users/{id:guid}/reactivate", async (Guid id, UserService users, ICurrentUser user, CancellationToken ct) =>
        {
            await users.SetActiveAsync(id, true, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.UsersManage).WithName("ReactivateUser");

        admin.MapPost("/users/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest req, UserService users, ICurrentUser user, CancellationToken ct) =>
        {
            await users.ResetPasswordAsync(id, req.NewPassword, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.UsersManage).WithName("ResetUserPassword");
    }

    private static RoleResponse ToResponse(Role r) => new(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList());
    private static UserResponse ToResponse(StaffUser u) => new(u.Id, u.UserName, u.Email, u.DisplayName, u.PhoneNumber, u.IsActive, u.MustChangePassword, u.LastLoginAt, u.Roles.Select(r => r.RoleId).ToList());
}
