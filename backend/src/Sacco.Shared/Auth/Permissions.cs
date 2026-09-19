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
        public const string Exit = "members.exit";
        public const string ExitApprove = "members.exit.approve";
        public const string SelfServiceManage = "members.self_service.manage";
    }

    /// <summary>Granted implicitly to member logins (never to staff roles): a member may only see and act on their own records.</summary>
    public static class Self
    {
        public const string ProfileView = "self.profile.view";
        public const string AccountsView = "self.accounts.view";
        public const string StatementsView = "self.statements.view";
        public const string LoansView = "self.loans.view";
        public const string LoansApply = "self.loans.apply";
        public const string PaymentsInitiate = "self.payments.initiate";
        /// <summary>Requesting a withdrawal from one's own account — still maker-checker (ADR 0014): a staff member must approve before any payout is triggered.</summary>
        public const string WithdrawalsRequest = "self.withdrawals.request";
        public const string DividendsView = "self.dividends.view";
        /// <summary>List/browse/buy on the shares marketplace — still maker-checker: a staff member must approve before shares or cash actually move.</summary>
        public const string SharesMarketplaceTrade = "self.shares_marketplace.trade";
        public static readonly IReadOnlyList<string> All =
            [ProfileView, AccountsView, StatementsView, LoansView, LoansApply, PaymentsInitiate, WithdrawalsRequest, DividendsView, SharesMarketplaceTrade];
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
        /// <summary>Checker for a member-to-member share sale claimed on the marketplace (ADR 0008 follow-up).</summary>
        public const string SharesMarketplaceApprove = "savings.shares_marketplace.approve";
        /// <summary>Maker for the fee matrix (ADR 0015): propose a new or revised fee rule.</summary>
        public const string FeesManage = "savings.fees.manage";
        /// <summary>Checker for the fee matrix: approve/reject proposed rules and deactivate live ones.</summary>
        public const string FeesApprove = "savings.fees.approve";
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
        public const string ScoringManage = "loans.scoring.manage";
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
        /// <summary>Set who receives the nightly branded PDF digest and trigger it on demand (ADR 0013).</summary>
        public const string RecipientsManage = "reporting.recipients.manage";
    }

    public static class Admin
    {
        public const string UsersManage = "admin.users.manage";
        /// <summary>Propose a new staff user; a holder of <see cref="UsersManage"/> must approve before the invitation email is sent.</summary>
        public const string UsersInvite = "admin.users.invite";
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
        new(Members.Exit, "Request a member's exit (maker)"),
        new(Members.ExitApprove, "Approve a member's exit and settle balances (checker)"),
        new(Members.SelfServiceManage, "Enable or disable a member's self-service login"),

        new(Savings.View, "View savings/share accounts"),
        new(Savings.ProductsManage, "Maintain savings and share products"),
        new(Savings.AccountsOpen, "Open savings/share accounts for members"),
        new(Savings.Deposit, "Receive deposits"),
        new(Savings.Withdraw, "Initiate withdrawals"),
        new(Savings.WithdrawalApprove, "Approve withdrawals above teller limit"),
        new(Savings.DividendsDeclare, "Declare a dividend (maker)"),
        new(Savings.DividendsApprove, "Approve a dividend declaration (checker)"),
        new(Savings.SharesMarketplaceApprove, "Approve a member-to-member share sale (checker)"),
        new(Savings.FeesManage, "Propose deposit, withdrawal and balance-enquiry fee rules (maker)"),
        new(Savings.FeesApprove, "Approve, reject or deactivate fee rules (checker)"),

        new(Loans.View, "View loans"),
        new(Loans.ProductsManage, "Maintain loan products"),
        new(Loans.Originate, "Capture a loan application"),
        new(Loans.Appraise, "Appraise a loan application"),
        new(Loans.Approve, "Approve a loan (checker / committee member)"),
        new(Loans.Disburse, "Disburse an approved loan"),
        new(Loans.Repay, "Post loan repayments"),
        new(Loans.Restructure, "Restructure or write off a loan"),
        new(Loans.ProvisioningManage, "Maintain provisioning and aging configuration"),
        new(Loans.ScoringManage, "Maintain the credit scorecard (weights and cut-offs)"),

        new(Payments.View, "View payment transactions"),
        new(Payments.Initiate, "Initiate mobile money / bank payments"),
        new(Payments.Reconcile, "Reconcile provider transactions"),

        new(Reporting.View, "View reports"),
        new(Reporting.StatutoryGenerate, "Generate SASRA statutory returns"),
        new(Reporting.StatutorySubmit, "Mark statutory returns as submitted"),
        new(Reporting.RecipientsManage, "Set recipients for the nightly PDF digest and send it on demand"),

        new(Admin.UsersManage, "Manage staff users"),
        new(Admin.UsersInvite, "Propose new staff users (a user administrator approves)"),
        new(Admin.RolesManage, "Manage roles and permission bundles"),
        new(Admin.TenantManage, "Manage tenant settings and branding"),
        new(Admin.AuditView, "View the audit log"),
        new(Admin.ConfigManage, "Manage system configuration"),

        new(Self.ProfileView, "Member self-service: view own profile"),
        new(Self.AccountsView, "Member self-service: view own accounts and balances"),
        new(Self.StatementsView, "Member self-service: view own statements"),
        new(Self.LoansView, "Member self-service: view own loans"),
        new(Self.LoansApply, "Member self-service: apply for a loan"),
        new(Self.PaymentsInitiate, "Member self-service: initiate a mobile-money deposit"),
        new(Self.WithdrawalsRequest, "Member self-service: request a withdrawal from own account"),
        new(Self.DividendsView, "Member self-service: view own dividend history"),
        new(Self.SharesMarketplaceTrade, "Member self-service: list, browse and buy on the shares marketplace"),
    ];

    /// <summary>Permissions a staff role may bundle: everything except the member self-service set.</summary>
    public static IEnumerable<PermissionDefinition> StaffAssignable => All.Where(p => !p.Name.StartsWith("self.", StringComparison.Ordinal));

    public static bool IsKnown(string permission) => All.Any(p => p.Name == permission);
}

public sealed record PermissionDefinition(string Name, string Description);
