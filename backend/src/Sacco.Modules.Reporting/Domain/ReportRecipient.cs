using System.Text.RegularExpressions;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Reporting.Domain;

/// <summary>
/// A person who receives the nightly branded PDF digest (ADR 0013). Plain configuration, not a staff
/// user — the recipient need not have a portal login, e.g. a board member who only wants the email.
/// </summary>
public class ReportRecipient : TenantEntity
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private ReportRecipient() { }

    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public Guid AddedByUserId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    public static ReportRecipient Create(Guid id, Guid tenantId, string email, string name, Guid byUser, DateTimeOffset now)
    {
        email = email.Trim().ToLowerInvariant();
        if (!EmailPattern.IsMatch(email)) throw new DomainRuleException("reporting.recipients.invalid_email", $"'{email}' is not a valid email address.");
        return new ReportRecipient { Id = id, TenantId = tenantId, Email = email, Name = name.Trim(), AddedByUserId = byUser, AddedAt = now };
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
