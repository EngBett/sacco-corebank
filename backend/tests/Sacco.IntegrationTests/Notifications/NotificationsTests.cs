using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Notifications.Application;
using Sacco.Modules.Notifications.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Shouldly;

namespace Sacco.IntegrationTests.Notifications;

/// <summary>
/// A pending manual journal must reach every checker (holders of ledger.journal.approve) and nobody else;
/// each recipient's read state is their own; live sessions get the same row over the hub.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationsTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private async Task<string> InitiatePendingJournal(Guid initiatedBy)
    {
        using var scope = _factory.TenantScope();
        var workflow = scope.ServiceProvider.GetRequiredService<JournalWorkflow>();
        var reference = $"NT-{Guid.NewGuid():N}"[..14];
        await workflow.CreatePendingAsync(reference, "Notification test journal", new DateOnly(2026, 9, 1),
        [
            new PostingLine(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, 250m),
            new PostingLine(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Credit, 250m),
        ], initiatedBy, CancellationToken.None);
        return reference;
    }

    private static async Task<PagedResult<NotificationResponse>> Unread(HttpClient client)
        => await (await client.GetAsync("/api/notifications?unreadOnly=true&pageSize=100")).ReadAs<PagedResult<NotificationResponse>>();

    [Fact]
    public async Task Pending_journal_notifies_checkers_but_not_the_maker()
    {
        var reference = await InitiatePendingJournal(DemoTenant.Users.Accountant);

        // The branch manager holds ledger.journal.approve (seeded role); the accountant is the maker.
        var manager = _factory.ClientAs(DemoTenant.Users.BranchManager);
        var accountant = _factory.ClientAs(DemoTenant.Users.Accountant);

        var forManager = (await Unread(manager)).Items.Where(n => n.Title.Contains(reference)).ToList();
        forManager.Count.ShouldBe(1);
        forManager[0].Kind.ShouldBe("ledger.journal.pending");
        forManager[0].Link.ShouldStartWith("/ledger/journals/");
        forManager[0].ReadAt.ShouldBeNull();

        (await Unread(accountant)).Items.ShouldNotContain(n => n.Title.Contains(reference));
    }

    [Fact]
    public async Task Read_state_is_per_recipient_and_ids_do_not_leak()
    {
        var reference = await InitiatePendingJournal(DemoTenant.Users.Accountant);
        var manager = _factory.ClientAs(DemoTenant.Users.BranchManager);
        var mine = (await Unread(manager)).Items.Single(n => n.Title.Contains(reference));

        var before = await (await manager.GetAsync("/api/notifications/unread-count")).ReadAs<UnreadCountResponse>();
        before.Unread.ShouldBeGreaterThanOrEqualTo(1);

        // Another authenticated user cannot touch it — 404, not 403, so the id reveals nothing.
        var teller = _factory.ClientAs(DemoTenant.Users.Teller);
        (await teller.PostAsync($"/api/notifications/{mine.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var read = await (await manager.PostAsync($"/api/notifications/{mine.Id}/read", null)).ReadAs<NotificationResponse>();
        read.ReadAt.ShouldNotBeNull();
        (await Unread(manager)).Items.ShouldNotContain(n => n.Id == mine.Id);

        var all = await (await manager.PostAsync("/api/notifications/read-all", null)).ReadAs<MarkAllReadResponse>();
        all.Marked.ShouldBe(before.Unread - 1);
        (await (await manager.GetAsync("/api/notifications/unread-count")).ReadAs<UnreadCountResponse>()).Unread.ShouldBe(0);
    }

    [Fact]
    public async Task Notifications_require_authentication()
    {
        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await anonymous.GetAsync("/api/notifications")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>Real tokens end to end: the BFF-held access token buys a hub ticket, the ticket opens the hub, the access token itself does not.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationsHubTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    private HubConnection Connect(string ticket) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/notifications"), o =>
        {
            o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            o.AccessTokenProvider = () => Task.FromResult<string?>(ticket);
            o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
        })
        .Build();

    [Fact]
    public async Task Connected_checker_receives_the_pending_journal_live()
    {
        var accessToken = await _factory.TokenFor("manager", IdentitySeeder.DemoPassword);
        var api = _factory.ClientWithToken(accessToken);
        var ticket = await (await api.PostAsync("/api/notifications/hub-ticket", null)).ReadAs<HubTicketResponse>();
        ticket.HubPath.ShouldBe("/hubs/notifications");
        ticket.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        await using var connection = Connect(ticket.Ticket);
        var received = new TaskCompletionSource<NotificationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<NotificationResponse>("notification", n => { if (n.Title.Contains("HUB-")) received.TrySetResult(n); });
        await connection.StartAsync();
        connection.State.ShouldBe(HubConnectionState.Connected);

        string reference;
        using (var scope = _factory.TenantScope())
        {
            var workflow = scope.ServiceProvider.GetRequiredService<JournalWorkflow>();
            reference = $"HUB-{Guid.NewGuid():N}"[..14];
            await workflow.CreatePendingAsync(reference, "Live notification", new DateOnly(2026, 9, 1),
            [
                new PostingLine(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, 10m),
                new PostingLine(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Credit, 10m),
            ], DemoTenant.Users.Accountant, CancellationToken.None);
        }

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        pushed.Title.ShouldContain(reference);
        pushed.Kind.ShouldBe("ledger.journal.pending");
        pushed.ReadAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_api_access_token_is_not_a_hub_ticket_and_vice_versa()
    {
        var accessToken = await _factory.TokenFor("manager", IdentitySeeder.DemoPassword);

        await using var withAccessToken = Connect(accessToken);
        var ex = await Should.ThrowAsync<HttpRequestException>(() => withAccessToken.StartAsync());
        ex.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var ticket = await (await _factory.ClientWithToken(accessToken).PostAsync("/api/notifications/hub-ticket", null)).ReadAs<HubTicketResponse>();
        var withTicket = _factory.ClientWithToken(ticket.Ticket);
        withTicket.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await withTicket.GetAsync("/api/notifications")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
