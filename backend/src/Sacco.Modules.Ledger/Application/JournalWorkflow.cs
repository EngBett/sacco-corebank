using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Ledger.Application;

/// <summary>
/// Maker-checker workflow for manual GL journals and reversals. A manual journal is a GL
/// adjustment and therefore never takes effect on the initiator's say-so alone
/// (non-negotiable #6). Every decision is audit-logged.
/// </summary>
public sealed class JournalWorkflow(LedgerDbContext db, PostingEngine engine, ITenantContext tenant, IClock clock, IAuditLogger audit)
{
    public async Task<JournalEntry> CreatePendingAsync(string reference, string description, DateOnly valueDate, IReadOnlyList<PostingLine> lines, Guid initiatedBy, CancellationToken ct)
    {
        var drafts = await engine.ResolveLinesAsync(lines, ct);
        var entry = JournalEntry.Create(Ids.New(), tenant.TenantId, reference, description, valueDate, "Manual", initiatedBy, clock.UtcNow, drafts, JournalEntryStatus.PendingApproval);
        db.JournalEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostingEngine.IsUniqueViolation(ex, "reference"))
        {
            throw new ConflictException("ledger.journal.duplicate_reference", $"A journal with reference '{reference}' already exists.");
        }
        await audit.RecordAsync(new AuditEvent("ledger.journal.initiated", nameof(JournalEntry), entry.Id.ToString(), initiatedBy,
            $$"""{"reference":"{{entry.Reference}}","amount":{{entry.TotalAmount}}}"""), ct);
        return entry;
    }

    public async Task<JournalEntry> ApproveAsync(Guid entryId, Guid approvingUserId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var entry = await db.JournalEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
                        ?? throw new NotFoundException("Journal", entryId);

            entry.Approve(approvingUserId, clock.UtcNow); // throws MakerCheckerViolationException if same user

            if (entry.ReversalOfEntryId is Guid originalId)
            {
                var original = await db.JournalEntries.FirstAsync(e => e.Id == originalId, ct);
                original.MarkReversedBy(entry.Id);
            }

            await db.SaveChangesAsync(ct);
            await engine.ApplyBalancesAsync(entry, ct);
            await audit.RecordAsync(new AuditEvent("ledger.journal.approved", nameof(JournalEntry), entry.Id.ToString(), approvingUserId,
                $$"""{"reference":"{{entry.Reference}}","amount":{{entry.TotalAmount}},"initiatedBy":"{{entry.InitiatedByUserId}}"}"""), ct);
            await tx.CommitAsync(ct);
            return entry;
        });
    }

    public async Task<JournalEntry> RejectAsync(Guid entryId, Guid rejectingUserId, string reason, CancellationToken ct)
    {
        var entry = await db.JournalEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
                    ?? throw new NotFoundException("Journal", entryId);
        entry.Reject(rejectingUserId, reason);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("ledger.journal.rejected", nameof(JournalEntry), entry.Id.ToString(), rejectingUserId,
            $$"""{"reference":"{{entry.Reference}}","reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return entry;
    }

    /// <summary>Creates a pending mirror-image entry. It takes effect only when a different user approves it.</summary>
    public async Task<JournalEntry> RequestReversalAsync(Guid originalId, string reason, Guid initiatedBy, CancellationToken ct)
    {
        var original = await db.JournalEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == originalId, ct)
                       ?? throw new NotFoundException("Journal", originalId);
        if (original.Status != JournalEntryStatus.Posted)
            throw new DomainRuleException("ledger.journal.not_posted", $"Journal {original.Reference} is not posted and cannot be reversed.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainRuleException("ledger.journal.reversal_reason_required", "A reversal reason is required.");

        var alreadyPending = await db.JournalEntries.AnyAsync(e => e.ReversalOfEntryId == originalId && e.Status == JournalEntryStatus.PendingApproval, ct);
        if (alreadyPending)
            throw new ConflictException("ledger.journal.reversal_pending", $"A reversal of {original.Reference} is already pending approval.");

        var drafts = original.Lines.Select(l => new JournalLineDraft(l.GlAccountId, l.GlAccountCode, l.LedgerAccountId, l.LedgerAccountNumber, l.Segment,
            l.Direction == EntryDirection.Debit ? EntryDirection.Credit : EntryDirection.Debit, l.Amount, $"Reversal: {l.Narrative}")).ToList();

        var reversal = JournalEntry.Create(Ids.New(), tenant.TenantId, $"REV:{original.Reference}", $"Reversal of {original.Reference}: {reason.Trim()}",
            clock.Today, "Reversal", initiatedBy, clock.UtcNow, drafts, JournalEntryStatus.PendingApproval, original.Id);
        db.JournalEntries.Add(reversal);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostingEngine.IsUniqueViolation(ex, "reference"))
        {
            throw new ConflictException("ledger.journal.already_reversed", $"Journal {original.Reference} has already been reversed.");
        }
        await audit.RecordAsync(new AuditEvent("ledger.journal.reversal_requested", nameof(JournalEntry), reversal.Id.ToString(), initiatedBy,
            $$"""{"original":"{{original.Reference}}","reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return reversal;
    }
}
