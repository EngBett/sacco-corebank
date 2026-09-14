using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Notifications.Domain;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

/// <summary>
/// Most demo notifications are produced by the earlier seeders as a side effect of driving the real
/// workflows (a pending journal notifies the checkers, an approved loan notifies the officer). This seeder
/// only adds a welcome note per user and marks the oldest half of everyone's inbox read, so the bell
/// shows a realistic mix of read and unread on first sign-in.
/// </summary>
public sealed class NotificationsSeeder(NotificationsDbContext db, IClock clock, ILogger<NotificationsSeeder> logger) : ISeeder
{
    public int Order => 60;
    private const string WelcomeKind = "platform.welcome";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.Kind == WelcomeKind, ct)) return;

        var welcomeAt = clock.UtcNow.AddDays(-14);
        foreach (var (id, _, displayName, _, role) in IdentitySeeder.Users)
        {
            db.Notifications.Add(Notification.Create(DemoTenant.Id, id, SystemActors.System, WelcomeKind, $"Welcome to the {DemoTenant.Name} portal",
                $"You are signed in as {displayName.Split(" (")[0]} with the {role} role. Approvals that need you will appear here as they happen.", "/", welcomeAt));
        }
        await db.SaveChangesAsync(ct);

        // Oldest half of each inbox read; the newest stay unread so every role has something to clear.
        var marked = 0;
        foreach (var group in (await db.Notifications.ToListAsync(ct)).GroupBy(n => n.RecipientUserId))
        {
            var ordered = group.OrderBy(n => n.CreatedAt).ToList();
            foreach (var n in ordered.Take(ordered.Count / 2)) { n.MarkRead(n.CreatedAt.AddMinutes(30)); marked++; }
        }
        await db.SaveChangesAsync(ct);
        logger.Created($"Welcome notifications ({marked} older notifications marked read)", IdentitySeeder.Users.Length);
    }
}
