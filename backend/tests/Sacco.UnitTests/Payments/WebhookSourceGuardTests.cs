using System.Net;
using Microsoft.Extensions.Options;
using Sacco.Modules.Payments.Application;
using Sacco.Modules.Payments.Providers;
using Shouldly;

namespace Sacco.UnitTests.Payments;

public class WebhookSourceGuardTests
{
    private static WebhookSourceGuard Guard(params string[] cidrs) => new(Options.Create(new PaymentsSettings { WebhookAllowedCidrs = [.. cidrs] }));

    [Fact]
    public void Empty_allowlist_allows_everything()
    {
        Guard().IsAllowed(IPAddress.Parse("203.0.113.9")).ShouldBeTrue();
        Guard().IsAllowed(null).ShouldBeTrue();
    }

    [Fact]
    public void Cidr_and_single_addresses_are_matched()
    {
        var g = Guard("196.201.214.0/24", "10.0.0.5");
        g.IsAllowed(IPAddress.Parse("196.201.214.200")).ShouldBeTrue();
        g.IsAllowed(IPAddress.Parse("196.201.215.1")).ShouldBeFalse();
        g.IsAllowed(IPAddress.Parse("10.0.0.5")).ShouldBeTrue();
        g.IsAllowed(IPAddress.Parse("::ffff:10.0.0.5")).ShouldBeTrue();
        g.IsAllowed(null).ShouldBeFalse();
    }
}
