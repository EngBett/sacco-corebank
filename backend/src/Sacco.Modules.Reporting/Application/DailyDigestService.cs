using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Reporting.Domain;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Reporting.Application;

public sealed record ReportRecipientResponse(Guid Id, string Email, string Name, bool IsActive, DateTimeOffset AddedAt);
public sealed record DigestResult(bool Sent, int RecipientCount, string? Error);

/// <summary>
/// Recipient list management and the nightly PDF digest itself (ADR 0013): same data <see cref="StatutoryReportService"/>
/// already computes for on-demand viewing, rendered through <see cref="DailyDigestHtml"/> and <see cref="HtmlToPdfRenderer"/>,
/// emailed via the Shared <see cref="IEmailSender"/> contract so this module never depends on Notifications directly.
/// </summary>
public sealed class DailyDigestService(ReportingDbContext db, StatutoryReportService reports, ILendingService lending, HtmlToPdfRenderer renderer,
    IEmailSender email, ITenantEnumerator tenants, ITenantContext tenant, IClock clock, IAuditLogger audit, ILogger<DailyDigestService> logger)
{
    public async Task<IReadOnlyList<ReportRecipientResponse>> ListRecipientsAsync(CancellationToken ct)
        => (await db.Recipients.AsNoTracking().OrderBy(r => r.Email).ToListAsync(ct)).Select(ToResponse).ToList();

    public async Task<ReportRecipientResponse> AddRecipientAsync(string email, string name, Guid byUser, CancellationToken ct)
    {
        var normalised = email.Trim().ToLowerInvariant();
        if (await db.Recipients.AnyAsync(r => r.Email == normalised, ct))
            throw new ConflictException("reporting.recipients.duplicate", $"'{normalised}' is already a recipient.");
        var recipient = ReportRecipient.Create(Ids.New(), tenant.TenantId, email, name, byUser, clock.UtcNow);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("reporting.recipients.added", nameof(ReportRecipient), recipient.Id.ToString(), byUser, $$"""{"email":"{{recipient.Email}}"}"""), ct);
        return ToResponse(recipient);
    }

    public async Task RemoveRecipientAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var recipient = await db.Recipients.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Report recipient", id);
        db.Recipients.Remove(recipient);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("reporting.recipients.removed", nameof(ReportRecipient), recipient.Id.ToString(), byUser, $$"""{"email":"{{recipient.Email}}"}"""), ct);
    }

    /// <summary>Builds today's digest PDF without sending it — the admin "Preview" action, and reused by <see cref="RunForCurrentTenantAsync"/>.</summary>
    public async Task<(byte[] Pdf, string FileName, string Subject)> BuildAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var branding = await tenants.GetBrandingAsync(tenant.TenantId, ct) ?? new TenantBrandingInfo(tenant.TenantSlug, tenant.TenantSlug, "#0f766e", "#f59e0b", null, "");
        var position = await reports.FinancialPositionAsync(null, today, ct);
        var capital = await reports.CapitalAdequacyAsync(today, ct);
        var liquidity = await reports.LiquidityAsync(today, ct);
        var portfolio = await lending.GetPortfolioQualityAsync(today, ct);
        var html = DailyDigestHtml.Build(branding, today, position, capital, liquidity, portfolio);
        var pdf = await renderer.RenderAsync(html, ct);
        return (pdf, $"{branding.ShortName}-daily-snapshot-{today:yyyy-MM-dd}.pdf", $"{branding.Name} — daily snapshot, {today:d MMM yyyy}");
    }

    /// <summary>Called once per tenant per night by <see cref="DailyDigestScheduler"/> (and on demand via the admin "Send now" action). A tenant with no recipients is a no-op, not an error.</summary>
    public async Task<DigestResult> RunForCurrentTenantAsync(CancellationToken ct)
    {
        var recipients = await db.Recipients.AsNoTracking().Where(r => r.IsActive).Select(r => r.Email).ToListAsync(ct);
        if (recipients.Count == 0) return new DigestResult(false, 0, null);
        try
        {
            var (pdf, fileName, subject) = await BuildAsync(ct);
            var attachment = new EmailAttachment(fileName, "application/pdf", pdf);
            var bodyHtml = $"<p>Attached is today's daily snapshot PDF, generated automatically.</p><p style=\"color:#888;font-size:12px\">This is an automated message — see Admin → Reports to change who receives it.</p>";
            foreach (var to in recipients) await email.SendAsync(to, subject, bodyHtml, [attachment], ct);
            logger.LogInformation("Daily digest sent to {Count} recipient(s) for tenant {Tenant}", recipients.Count, tenant.TenantSlug);
            return new DigestResult(true, recipients.Count, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Daily digest failed for tenant {Tenant}", tenant.TenantSlug);
            return new DigestResult(false, recipients.Count, ex.Message);
        }
    }

    private static ReportRecipientResponse ToResponse(ReportRecipient r) => new(r.Id, r.Email, r.Name, r.IsActive, r.AddedAt);
}
