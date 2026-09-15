using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Notifications.Application;
using Sacco.Modules.Notifications.Domain;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Shouldly;

namespace Sacco.IntegrationTests.Notifications;

/// <summary>A notification that needs attention also goes out by email and SMS; the sandbox records the send on the outbox row.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Pending_journal_reaches_checkers_by_email_and_sms_and_the_outbox_is_admin_only()
    {
        string reference;
        using (var scope = _factory.TenantScope())
        {
            var workflow = scope.ServiceProvider.GetRequiredService<JournalWorkflow>();
            reference = $"OB-{Guid.NewGuid():N}"[..14];
            await workflow.CreatePendingAsync(reference, "Outbox test", new DateOnly(2026, 9, 1),
            [
                new PostingLine(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, 5m),
                new PostingLine(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Credit, 5m),
            ], DemoTenant.Users.Accountant, CancellationToken.None);
        }

        (await _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Ledger.View).GetAsync("/api/notifications/outbox")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = _factory.ClientAs(DemoTenant.Users.Admin, Permissions.Admin.AuditView);
        var channels = await (await admin.GetAsync("/api/notifications/outbox/channels")).ReadAs<ChannelInfo>();
        channels.SmsSandbox.ShouldBeTrue();
        channels.EmailSandbox.ShouldBeTrue();

        var outbox = await (await admin.GetAsync("/api/notifications/outbox?pageSize=100")).ReadAs<PagedResult<OutboundMessageResponse>>();
        var mine = outbox.Items.Where(m => m.Body.Contains(reference) || (m.Subject?.Contains(reference) ?? false)).ToList();
        mine.ShouldContain(m => m.Channel == MessageChannel.Email && m.RecipientUserId == DemoTenant.Users.BranchManager);
        mine.ShouldContain(m => m.Channel == MessageChannel.Sms && m.RecipientUserId == DemoTenant.Users.BranchManager, "pending approvals go out by SMS too");
        mine.ShouldAllBe(m => m.Status == OutboundStatus.Sent && m.ProviderReference != null);
        mine.ShouldNotContain(m => m.RecipientUserId == DemoTenant.Users.Accountant, "the maker is not told about their own action");
    }
}
