using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;

namespace Sacco.Seed.Seeders;

/// <summary>
/// One role per permission bundle and one staff user per role, so RBAC and maker-checker can be
/// demoed by switching logins. Demo password for every account: <see cref="DemoPassword"/>.
/// These are sandbox-only accounts and must be absent from any production database. Demo staff have no authenticator
/// set up: whoever signs in as one first scans the QR code (ADR 0016). One invitation proposed by the Branch Manager
/// waits for the System Admin, so the maker-checker invite flow is demoable straight away.
/// </summary>
public sealed class IdentitySeeder(IdentityDbContext db, UserService users, RoleService roles, StaffInvitationService invitations, ILogger<IdentitySeeder> logger) : ISeeder
{
    public int Order => 5;
    public const string DemoPassword = "Demo2026!pass";

    public static readonly (string Name, string Description, string[] Permissions)[] Roles =
    [
        ("Teller", "Front-office cash and deposits",
            [Permissions.Members.View, Permissions.Members.Exit, Permissions.Savings.View, Permissions.Savings.Deposit, Permissions.Savings.Withdraw, Permissions.Loans.View, Permissions.Loans.Repay, Permissions.Payments.View, Permissions.Payments.Initiate]),
        ("Loan Officer", "Originates and appraises loans",
            [Permissions.Members.View, Permissions.Members.Create, Permissions.Members.Edit, Permissions.Members.Exit, Permissions.Members.SelfServiceManage, Permissions.Savings.View, Permissions.Loans.View, Permissions.Loans.Originate, Permissions.Loans.Appraise, Permissions.Loans.Restructure, Permissions.Ledger.View]),
        ("Credit Committee", "Approves loans (N-of-M committee)",
            [Permissions.Members.View, Permissions.Savings.View, Permissions.Loans.View, Permissions.Loans.Approve, Permissions.Ledger.View]),
        ("Branch Manager", "Approves journals, withdrawals, disbursements; reviews applications",
            [Permissions.Members.View, Permissions.Members.Create, Permissions.Members.Edit, Permissions.Members.Suspend, Permissions.Members.ApplicationsReview,
             Permissions.Savings.View, Permissions.Savings.AccountsOpen, Permissions.Savings.WithdrawalApprove, Permissions.Savings.DividendsApprove, Permissions.Savings.SharesMarketplaceApprove, Permissions.Savings.FeesApprove,
             Permissions.Members.ExitApprove, Permissions.Members.SelfServiceManage, Permissions.Loans.View, Permissions.Loans.Approve, Permissions.Loans.Disburse, Permissions.Loans.Restructure, Permissions.Loans.ScoringManage,
             Permissions.Ledger.View, Permissions.Ledger.JournalApprove, Permissions.Ledger.AccountsManage, Permissions.Payments.View, Permissions.Payments.Reconcile, Permissions.Reporting.View,
             Permissions.Admin.UsersInvite]),
        ("Accountant", "Maintains the GL and prepares journals",
            [Permissions.Ledger.View, Permissions.Ledger.JournalCreate, Permissions.Ledger.JournalReverse, Permissions.Ledger.ChartManage, Permissions.Savings.View, Permissions.Savings.ProductsManage, Permissions.Savings.DividendsDeclare, Permissions.Savings.FeesManage,
             Permissions.Loans.View, Permissions.Loans.ProductsManage, Permissions.Loans.ProvisioningManage, Permissions.Payments.View, Permissions.Payments.Reconcile, Permissions.Reporting.View, Permissions.Reporting.StatutoryGenerate]),
        ("Compliance Officer", "KYC verification, audit and statutory reporting",
            [Permissions.Members.View, Permissions.Members.KycVerify, Permissions.Members.ApplicationsReview, Permissions.Savings.View, Permissions.Loans.View, Permissions.Ledger.View,
             Permissions.Admin.AuditView, Permissions.Reporting.View, Permissions.Reporting.StatutoryGenerate, Permissions.Reporting.StatutorySubmit, Permissions.Reporting.RecipientsManage]),
        ("System Admin", "Users, roles, tenant settings",
            [Permissions.Admin.UsersManage, Permissions.Admin.RolesManage, Permissions.Admin.TenantManage, Permissions.Admin.AuditView, Permissions.Admin.ConfigManage, Permissions.Members.View, Permissions.Ledger.View, Permissions.Reporting.View, Permissions.Reporting.RecipientsManage]),
    ];

    /// <summary>Which office each demo user works at, so branch filters and branch-stamped postings have something to show.</summary>
    private static readonly Dictionary<string, string> UserBranches = new()
    {
        ["admin"] = "HQ", ["accountant"] = "HQ", ["compliance"] = "HQ", ["committee1"] = "HQ", ["committee2"] = "HQ",
        ["manager"] = "NKR", ["teller"] = "NKR", ["loanofficer"] = "ELD",
    };

    public static readonly (Guid Id, string UserName, string DisplayName, string Email, string Role)[] Users =
    [
        (DemoTenant.Users.Admin, "admin", "Grace Wanjiku (System Admin)", "admin@icodeio.example.co.ke", "System Admin"),
        (DemoTenant.Users.Teller, "teller", "Brian Ochieng (Teller)", "teller@icodeio.example.co.ke", "Teller"),
        (DemoTenant.Users.LoanOfficer, "loanofficer", "Mercy Kiptoo (Loan Officer)", "loans@icodeio.example.co.ke", "Loan Officer"),
        (DemoTenant.Users.CreditCommittee1, "committee1", "Peter Mwangi (Credit Committee)", "committee1@icodeio.example.co.ke", "Credit Committee"),
        (DemoTenant.Users.CreditCommittee2, "committee2", "Lydia Atieno (Credit Committee)", "committee2@icodeio.example.co.ke", "Credit Committee"),
        (DemoTenant.Users.BranchManager, "manager", "Samuel Kariuki (Branch Manager)", "manager@icodeio.example.co.ke", "Branch Manager"),
        (DemoTenant.Users.Accountant, "accountant", "Esther Njoroge (Accountant)", "accounts@icodeio.example.co.ke", "Accountant"),
        (DemoTenant.Users.ComplianceOfficer, "compliance", "Daniel Kimani (Compliance Officer)", "compliance@icodeio.example.co.ke", "Compliance Officer"),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var existingRoles = await db.Roles.ToDictionaryAsync(r => r.Name, ct);
        var created = 0;
        var reconciled = 0;
        foreach (var (name, description, permissions) in Roles)
        {
            if (existingRoles.TryGetValue(name, out var current))
            {
                // Demo bundles are the source of truth for the demo roles: new permissions reach an already-seeded database.
                var target = permissions.Distinct().OrderBy(x => x).ToList();
                if (!current.Permissions.Select(x => x.Permission).OrderBy(x => x).SequenceEqual(target))
                {
                    await roles.UpdateAsync(current.Id, current.Name, current.Description, target, DemoTenant.Users.System, ct);
                    reconciled++;
                }
                continue;
            }
            var role = await roles.CreateAsync(Ids.Deterministic($"role:{DemoTenant.Slug}:{name}"), name, description, permissions, DemoTenant.Users.System, isSystem: name == "System Admin", ct);
            existingRoles[name] = role;
            created++;
        }
        logger.Created("Roles", created);
        if (reconciled > 0) logger.LogInformation("  Roles reconciled to the seed bundles: {Count}", reconciled);

        var existingUsers = await db.Users.Select(u => u.UserName).ToHashSetAsync(ct);
        created = 0;
        foreach (var (id, userName, displayName, email, roleName) in Users)
        {
            if (existingUsers.Contains(userName)) continue;
            await users.CreateAsync(id, userName, email, displayName, "254700100000", DemoPassword, [existingRoles[roleName].Id], DemoTenant.Users.System, mustChangePassword: false, ct,
                branchId: UserBranches.TryGetValue(userName, out var branchCode) ? DemoTenant.BranchId(branchCode) : null);
            created++;
        }
        logger.Created("Users", created);

        // Databases seeded before branches existed: put each demo user in their office.
        var unassigned = await db.Users.Where(u => u.BranchId == null).ToListAsync(ct);
        foreach (var user in unassigned)
            if (UserBranches.TryGetValue(user.UserName, out var code)) user.SetBranch(DemoTenant.BranchId(code));
        if (unassigned.Count > 0) await db.SaveChangesAsync(ct);

        if (!await db.StaffInvitations.AnyAsync(ct))
        {
            await invitations.ProposeAsync(new ProposeInvitationCommand("fmuthoni", "faith.muthoni@icodeio.example.co.ke", "Faith Muthoni (Teller)", "254700100009",
                [existingRoles["Teller"].Id]), DemoTenant.Users.BranchManager, ct);
            logger.Created("Staff invitations awaiting approval", 1);
        }
    }
}
