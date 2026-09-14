using Sacco.Modules.Notifications.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Notifications;
using Shouldly;

namespace Sacco.UnitTests.Notifications;

public sealed class NotificationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_and_starts_unread()
    {
        var n = Notification.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), " ledger.journal.pending ", " Journal MJ-1 awaits approval ", " body ", "/ledger/journals/x", Now);
        n.Kind.ShouldBe("ledger.journal.pending");
        n.Title.ShouldBe("Journal MJ-1 awaits approval");
        n.Body.ShouldBe("body");
        n.IsRead.ShouldBeFalse();
        n.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void MarkRead_keeps_the_first_read_time()
    {
        var n = Notification.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), "k", "t", "b", null, Now);
        n.MarkRead(Now.AddMinutes(1));
        n.MarkRead(Now.AddMinutes(5));
        n.ReadAt.ShouldBe(Now.AddMinutes(1));
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("ledger/journals")]
    public void Links_must_be_portal_relative(string link)
    {
        Should.Throw<DomainRuleException>(() => Notification.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), "k", "t", "b", link, Now))
            .Code.ShouldBe("notifications.link_relative");
    }

    [Fact]
    public void Recipient_kind_and_title_are_required()
    {
        Should.Throw<DomainRuleException>(() => Notification.Create(Tenant, Guid.Empty, Guid.NewGuid(), "k", "t", "b", null, Now)).Code.ShouldBe("notifications.recipient_required");
        Should.Throw<DomainRuleException>(() => Notification.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), " ", "t", "b", null, Now)).Code.ShouldBe("notifications.kind_required");
        Should.Throw<DomainRuleException>(() => Notification.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), "k", "", "b", null, Now)).Code.ShouldBe("notifications.title_required");
    }

    [Fact]
    public void Audience_is_either_users_or_a_permission()
    {
        var a = Guid.NewGuid();
        NotificationAudience.User(a).UserIds.ShouldBe([a]);
        NotificationAudience.User(a).Permission.ShouldBeNull();
        NotificationAudience.HoldersOf("loans.approve").UserIds.ShouldBeEmpty();
        NotificationAudience.HoldersOf("loans.approve").Permission.ShouldBe("loans.approve");
    }
}
