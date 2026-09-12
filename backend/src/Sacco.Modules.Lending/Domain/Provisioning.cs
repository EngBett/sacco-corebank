using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Lending.Domain;

/// <summary>
/// NPL aging buckets and provisioning rates. Configuration, not constants: SASRA revises these by
/// circular (docs/compliance/sasra-mapping.md). Seeded with the Sacco Societies Regulations schedule.
/// </summary>
public class ProvisioningConfig : TenantEntity
{
    private readonly List<AgingBucket> _buckets = [];
    private ProvisioningConfig() { }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public string? Source { get; private set; }
    public IReadOnlyList<AgingBucket> Buckets => _buckets.OrderBy(b => b.MinDaysInArrears).ToList();

    public static ProvisioningConfig Create(Guid id, Guid tenantId, IEnumerable<AgingBucketDraft> buckets, string? source, Guid by, DateTimeOffset now)
    {
        var c = new ProvisioningConfig { Id = id, TenantId = tenantId };
        c.Replace(buckets, source, by, now);
        return c;
    }

    public IReadOnlyList<AgingBucket> Replace(IEnumerable<AgingBucketDraft> drafts, string? source, Guid by, DateTimeOffset now)
    {
        var list = drafts.OrderBy(d => d.MinDaysInArrears).ToList();
        if (list.Count == 0 || list[0].MinDaysInArrears != 0) throw new DomainRuleException("loans.provisioning.buckets", "Buckets must start at 0 days in arrears.");
        for (var i = 1; i < list.Count; i++)
            if (list[i].MinDaysInArrears <= list[i - 1].MinDaysInArrears) throw new DomainRuleException("loans.provisioning.buckets", "Bucket thresholds must strictly increase.");
        if (list.Any(d => d.ProvisionRateBps < 0 || d.ProvisionRateBps > 10_000)) throw new DomainRuleException("loans.provisioning.rate", "Provision rates must be 0–100%.");
        _buckets.Clear();
        var added = list.Select(d => AgingBucket.Create(Id, d.Name, d.MinDaysInArrears, d.ProvisionRateBps)).ToList();
        _buckets.AddRange(added);
        Source = source; UpdatedByUserId = by; UpdatedAt = now;
        return added;
    }

    public AgingBucket Classify(int daysInArrears) => Buckets.Last(b => daysInArrears >= b.MinDaysInArrears);
}

public sealed record AgingBucketDraft(string Name, int MinDaysInArrears, int ProvisionRateBps);

public class AgingBucket
{
    private AgingBucket() { }
    public Guid Id { get; private set; }
    public Guid ConfigId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int MinDaysInArrears { get; private set; }
    public int ProvisionRateBps { get; private set; }
    internal static AgingBucket Create(Guid configId, string name, int minDays, int rateBps) => new() { Id = Ids.New(), ConfigId = configId, Name = name, MinDaysInArrears = minDays, ProvisionRateBps = rateBps };
}

public enum ProvisioningRunStatus { PendingApproval = 1, Posted = 2, Rejected = 3 }

/// <summary>A provisioning computation as of a date. Posting the GL adjustment is maker-checker.</summary>
public class ProvisioningRun : TenantEntity
{
    private readonly List<ProvisioningLine> _lines = [];
    private ProvisioningRun() { }
    public DateOnly AsOf { get; private set; }
    public ProvisioningRunStatus Status { get; private set; }
    public Guid ComputedByUserId { get; private set; }
    public DateTimeOffset ComputedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public decimal TotalOutstanding { get; private set; }
    public decimal TotalProvisionRequired { get; private set; }
    public IReadOnlyList<ProvisioningLine> Lines => _lines;

    public static ProvisioningRun Create(Guid id, Guid tenantId, DateOnly asOf, Guid by, DateTimeOffset now, IEnumerable<ProvisioningLineDraft> drafts)
    {
        var run = new ProvisioningRun { Id = id, TenantId = tenantId, AsOf = asOf, Status = ProvisioningRunStatus.PendingApproval, ComputedByUserId = by, ComputedAt = now };
        foreach (var d in drafts)
        {
            run._lines.Add(ProvisioningLine.Create(run.Id, d));
            run.TotalOutstanding += d.OutstandingPrincipal; run.TotalProvisionRequired += d.ProvisionRequired;
        }
        return run;
    }

    public void Approve(Guid approver, DateTimeOffset now)
    {
        if (Status != ProvisioningRunStatus.PendingApproval) throw new DomainRuleException("loans.provisioning.not_pending", $"Run is {Status}.");
        MakerChecker.EnsureDistinct(ComputedByUserId, approver, $"provisioning run {AsOf:yyyy-MM-dd}");
        Status = ProvisioningRunStatus.Posted; ApprovedByUserId = approver; ApprovedAt = now;
    }

    public void Reject(Guid by, string reason)
    {
        if (Status != ProvisioningRunStatus.PendingApproval) throw new DomainRuleException("loans.provisioning.not_pending", $"Run is {Status}.");
        MakerChecker.EnsureDistinct(ComputedByUserId, by, $"provisioning run {AsOf:yyyy-MM-dd}");
        Status = ProvisioningRunStatus.Rejected; RejectionReason = reason;
    }
}

public sealed record ProvisioningLineDraft(Guid LoanId, string LoanNumber, Guid MemberId, string ProductCode, Segment Segment, int DaysInArrears, string Bucket, int ProvisionRateBps, decimal OutstandingPrincipal, decimal ArrearsAmount, decimal ProvisionRequired);

public class ProvisioningLine
{
    private ProvisioningLine() { }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid LoanId { get; private set; }
    public string LoanNumber { get; private set; } = string.Empty;
    public Guid MemberId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public Segment Segment { get; private set; }
    public int DaysInArrears { get; private set; }
    public string Bucket { get; private set; } = string.Empty;
    public int ProvisionRateBps { get; private set; }
    public decimal OutstandingPrincipal { get; private set; }
    public decimal ArrearsAmount { get; private set; }
    public decimal ProvisionRequired { get; private set; }
    internal static ProvisioningLine Create(Guid runId, ProvisioningLineDraft d) => new()
    {
        Id = Ids.New(), RunId = runId, LoanId = d.LoanId, LoanNumber = d.LoanNumber, MemberId = d.MemberId, ProductCode = d.ProductCode, Segment = d.Segment,
        DaysInArrears = d.DaysInArrears, Bucket = d.Bucket, ProvisionRateBps = d.ProvisionRateBps, OutstandingPrincipal = d.OutstandingPrincipal, ArrearsAmount = d.ArrearsAmount, ProvisionRequired = d.ProvisionRequired,
    };
}

/// <summary>Per-tenant loan numbering, advanced atomically.</summary>
public class LoanNumberSequence
{
    public Guid TenantId { get; set; }
    public int NextValue { get; set; }
}
