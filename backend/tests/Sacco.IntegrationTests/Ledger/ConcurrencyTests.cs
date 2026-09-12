using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Shouldly;

namespace Sacco.IntegrationTests.Ledger;

/// <summary>Phase 1 exit criterion: balances are correct under concurrent posting against the same account (ADR 0004).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConcurrencyTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private async Task<LedgerAccountSnapshot> OpenFundedAccount(decimal balance)
    {
        using var scope = _factory.TenantScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        var number = $"T{Guid.NewGuid():N}"[..16];
        var account = await ledger.OpenAccountAsync(new OpenLedgerAccountRequest(number, DemoTenant.MemberId("M00001"), Coa.FosaSavingsControl, Segment.Fosa, LedgerAccountKind.Current, LedgerSeeder.ProductFosaCurrent, DemoTenant.Users.System), CancellationToken.None);
        await ledger.PostAsync(new PostingRequest($"FUND:{number}", "Fund test account", new DateOnly(2026, 9, 1), "Test", DemoTenant.Users.Teller,
        [
            new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, balance),
            new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Credit, balance, number),
        ]), CancellationToken.None);
        return (await ledger.FindAccountAsync(number, CancellationToken.None))!;
    }

    private Task<Exception?> TryDebit(string accountNumber, decimal amount, int i) => Task.Run(async () =>
    {
        using var scope = _factory.TenantScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        try
        {
            await ledger.PostAsync(new PostingRequest($"WD:{accountNumber}:{i}", "Concurrent withdrawal", new DateOnly(2026, 9, 2), "Test", DemoTenant.Users.Teller,
            [
                new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Debit, amount, accountNumber),
                new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Credit, amount),
            ]), CancellationToken.None);
            return null;
        }
        catch (Exception ex) { return ex; }
    });

    [Fact]
    public async Task Concurrent_debits_never_overdraw_the_account()
    {
        var account = await OpenFundedAccount(1_000m);

        // 40 simultaneous withdrawals of 100 against a balance of 1,000: exactly 10 may succeed.
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => TryDebit(account.AccountNumber, 100m, i)));

        var succeeded = results.Count(r => r is null);
        var insufficient = results.Count(r => r is InsufficientFundsException);
        var other = results.Where(r => r is not null and not InsufficientFundsException).ToList();

        other.ShouldBeEmpty(string.Join("\n", other.Select(e => e!.ToString())));
        succeeded.ShouldBe(10);
        insufficient.ShouldBe(30);

        using var scope = _factory.TenantScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        var final = await ledger.FindAccountAsync(account.AccountNumber, CancellationToken.None);
        final!.Balance.ShouldBe(0m);

        // And the ledger as a whole is still internally consistent.
        var queries = scope.ServiceProvider.GetRequiredService<Sacco.Modules.Ledger.Application.LedgerQueries>();
        var report = await queries.ReconcileAsync(CancellationToken.None);
        report.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_credits_and_debits_across_accounts_do_not_deadlock()
    {
        var a = await OpenFundedAccount(5_000m);
        var b = await OpenFundedAccount(5_000m);

        // Transfers in both directions at once: a→b and b→a, 20 each.
        Task<Exception?> Transfer(string from, string to, int i) => Task.Run(async () =>
        {
            using var scope = _factory.TenantScope();
            var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
            try
            {
                await ledger.PostAsync(new PostingRequest($"TX:{from}:{to}:{i}", "Transfer", new DateOnly(2026, 9, 2), "Test", DemoTenant.Users.Teller,
                [
                    new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Debit, 100m, from),
                    new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Credit, 100m, to),
                ]), CancellationToken.None);
                return null;
            }
            catch (Exception ex) { return ex; }
        });

        var tasks = Enumerable.Range(0, 20).SelectMany(i => new[] { Transfer(a.AccountNumber, b.AccountNumber, i), Transfer(b.AccountNumber, a.AccountNumber, i) });
        var results = await Task.WhenAll(tasks);
        results.Where(r => r is not null).ShouldBeEmpty(string.Join("\n", results.Where(r => r is not null).Select(e => e!.Message)));

        using var scope = _factory.TenantScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        (await ledger.FindAccountAsync(a.AccountNumber, CancellationToken.None))!.Balance.ShouldBe(5_000m);
        (await ledger.FindAccountAsync(b.AccountNumber, CancellationToken.None))!.Balance.ShouldBe(5_000m);
    }

    [Fact]
    public async Task Hold_reduces_available_balance_atomically()
    {
        var account = await OpenFundedAccount(1_000m);
        using var scope = _factory.TenantScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedgerService>();

        await ledger.PlaceHoldAsync(account.AccountNumber, 700m, "guarantor lien", CancellationToken.None);
        await Should.ThrowAsync<InsufficientFundsException>(() => ledger.PlaceHoldAsync(account.AccountNumber, 400m, "second lien", CancellationToken.None));

        var withdrawTooMuch = await TryDebit(account.AccountNumber, 400m, 1);
        withdrawTooMuch.ShouldBeOfType<InsufficientFundsException>();

        (await TryDebit(account.AccountNumber, 300m, 2)).ShouldBeNull();
        await ledger.ReleaseHoldAsync(account.AccountNumber, 700m, "lien released", CancellationToken.None);
        var final = await ledger.FindAccountAsync(account.AccountNumber, CancellationToken.None);
        final!.Balance.ShouldBe(700m);
        final.HeldAmount.ShouldBe(0m);
        final.AvailableBalance.ShouldBe(700m);
    }
}
