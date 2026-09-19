using Sacco.Modules.Reporting.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Reporting;

public class ReportRecipientTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid By = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Email_is_normalised_and_validated()
    {
        var r = ReportRecipient.Create(Guid.NewGuid(), Tenant, "  Board.Chair@Icodeio.example.CO.KE  ", "Board Chair", By, Now);
        r.Email.ShouldBe("board.chair@icodeio.example.co.ke");
        r.IsActive.ShouldBeTrue();

        Should.Throw<DomainRuleException>(() => ReportRecipient.Create(Guid.NewGuid(), Tenant, "not-an-email", "x", By, Now))
            .Code.ShouldBe("reporting.recipients.invalid_email");
    }

    [Fact]
    public void Can_be_deactivated_and_reactivated()
    {
        var r = ReportRecipient.Create(Guid.NewGuid(), Tenant, "person@example.com", "Person", By, Now);
        r.Deactivate();
        r.IsActive.ShouldBeFalse();
        r.Activate();
        r.IsActive.ShouldBeTrue();
    }
}
