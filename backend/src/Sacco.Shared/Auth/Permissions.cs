namespace Sacco.Shared.Auth;

/// <summary>
/// Every granular permission in the system (non-negotiable #7: authorization is
/// permission-based, never role-name based). Roles are per-tenant bundles of these strings.
/// Add new permissions here and nowhere else so the seed tool and the admin UI can enumerate them.
/// </summary>
public static class Permissions
{
    public static class Ledger
    {
        public const string View = "ledger.view";
        public const string JournalCreate = "ledger.journal.create";
        public const string JournalApprove = "ledger.journal.approve";
        public const string JournalReverse = "ledger.journal.reverse";
        public const string AccountsManage = "ledger.accounts.manage";
        public const string ChartManage = "ledger.chart.manage";
    }

    public static class Members
    {
        public const string View = "members.view";
        public const string Create = "members.create";
        public const string Edit = "members.edit";
        public const string KycVerify = "members.kyc.verify";
        public const string Suspend = "members.suspend";
        public const string ApplicationsReview = "members.applications.review";
    }

    public static class Savings
    {
        public const string View = "savings.view";
        public const string ProductsManage = "savings.products.manage";
        public const string AccountsOpen = "savings.accounts.open";
        public const string Deposit = "savings.deposit";
        public const string Withdraw = "savings.withdraw";
        public const string WithdrawalApprove = "savings.withdrawal.approve";
        public const string DividendsDeclare = "savings.dividends.declare";
        public const string DividendsApprove = "savings.dividends.approve";
    }

    public static class Loans
    {
        public const string View = "loans.view";
        public const string ProductsManage = "loans.products.manage";
        public const string Originate = "loans.originate";
        public const string Appraise = "loans.appraise";
        public const string Approve = "loans.approve";
        public const string Disburse = "loans.disburse";
        public const string Repay = "loans.repay";
        public const string Restructure = "loans.restructure";
        public const string ProvisioningManage = "loans.provisioning.manage";
    }

    public static class Payments
    {
        public const string View = "payments.view";
        public const string Initiate = "payments.initiate";
        public const string Reconcile = "payments.reconcile";
    }

    public static class Reporting
    {
        public const string View = "reporting.view";
        public const string StatutoryGenerate = "reporting.statutory.generate";
        public const string StatutorySubmit = "reporting.statutory.submit";
    }

    public static class Admin
    {
        public const string UsersManage = "admin.users.manage";
        public const string RolesManage = "admin.roles.manage";
        public const string TenantManage = "admin.tenant.manage";
        public const string AuditView = "admin.audit.view";
        public const string ConfigManage = "admin.config.manage";
    }

    /// <summary>All permissions with human-readable descriptions (for seeding and admin UIs).</summary>
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(Ledger.View, "View GL accounts, journals, trial balance"),
        new(Ledger.JournalCreate, "Create a manual journal (maker)"),
        new(Ledger.JournalApprove, "Approve or reject a pending journal (checker)"),
        new(Ledger.JournalReverse, "Initiate reversal of a posted journal"),
        new(Ledger.AccountsManage, "Open, freeze, close sub-ledger accounts"),
        new(Ledger.ChartManage, "Maintain the chart of accounts"),

        new(Members.View, "View member profiles"),
        new(Members.Create, "Register a new member"),
        new(Members.Edit, "Edit member details"),
        new(Members.KycVerify, "Verify or reject member KYC"),
        new(Members.Suspend, "Suspend or reinstate a member"),
        new(Members.ApplicationsReview, "Review public membership applications"),

        new(Savings.View, "View savings/share accounts"),
        new(Savings.ProductsManage, "Maintain savings and share products"),
        new(Savings.AccountsOpen, "Open savings/share accounts for members"),
        new(Savings.Deposit, "Receive deposits"),
        new(Savings.Withdraw, "Initiate withdrawals"),
        new(Savings.WithdrawalApprove, "Approve withdrawals above teller limit"),
        new(Savings.DividendsDeclare, "Declare a dividend (maker)"),
        new(Savings.DividendsApprove, "Approve a dividend declaration (checker)"),

        new(Loans.View, "View loans"),
        new(Loans.ProductsManage, "Maintain loan products"),
        new(Loans.Originate, "Capture a loan application"),
        new(Loans.Appraise, "Appraise a loan application"),
        new(Loans.Approve, "Approve a loan (checker / committee member)"),
        new(Loans.Disburse, "Disburse an approved loan"),
        new(Loans.Repay, "Post loan repayments"),
        new(Loans.Restructure, "Restructure or write off a loan"),
        new(Loans.ProvisioningManage, "Maintain provisioning and aging configuration"),

        new(Payments.View, "View payment transactions"),
        new(Payments.Initiate, "Initiate mobile money / bank payments"),
        new(Payments.Reconcile, "Reconcile provider transactions"),

        new(Reporting.View, "View reports"),
        new(Reporting.StatutoryGenerate, "Generate SASRA statutory returns"),
        new(Reporting.StatutorySubmit, "Mark statutory returns as submitted"),

        new(Admin.UsersManage, "Manage staff users"),
        new(Admin.RolesManage, "Manage roles and permission bundles"),
        new(Admin.TenantManage, "Manage tenant settings and branding"),
        new(Admin.AuditView, "View the audit log"),
        new(Admin.ConfigManage, "Manage system configuration"),
    ];

    public static bool IsKnown(string permission) => All.Any(p => p.Name == permission);
}

public sealed record PermissionDefinition(string Name, string Description);
