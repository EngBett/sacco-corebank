namespace Sacco.Shared.Domain;

/// <summary>
/// Segregation-of-duties guard. Every approval of a money-moving or capital-affecting action
/// must call <see cref="EnsureDistinct"/> before taking effect. This is enforced in code, not
/// by convention (see .claude/CLAUDE.md non-negotiable #6 and docs/compliance/sasra-mapping.md).
/// </summary>
public static class MakerChecker
{
    public static void EnsureDistinct(Guid initiatedByUserId, Guid approvingUserId, string action)
    {
        if (initiatedByUserId == Guid.Empty)
            throw new DomainRuleException("maker_checker.missing_initiator", $"'{action}' has no recorded initiator.");
        if (approvingUserId == Guid.Empty)
            throw new DomainRuleException("maker_checker.missing_approver", $"'{action}' has no approver.");
        if (initiatedByUserId == approvingUserId)
            throw new MakerCheckerViolationException(action);
    }
}
