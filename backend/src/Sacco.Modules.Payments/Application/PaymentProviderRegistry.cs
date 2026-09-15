using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sacco.Modules.Payments.Providers;
using Sacco.Shared.Domain;
using Sacco.Shared.Payments;

namespace Sacco.Modules.Payments.Application;

/// <summary>Resolves the provider for a name from configuration: sandbox by default, live only when configured (never a code branch).</summary>
public sealed class PaymentProviderRegistry(IServiceProvider services, IOptions<PaymentsSettings> options)
{
    public IPaymentProvider Resolve(string providerName)
    {
        var s = options.Value;
        return providerName switch
        {
            ProviderNames.MPesa => s.MPesa.IsLive ? services.GetRequiredService<DarajaMpesaProvider>() : services.GetRequiredService<SandboxMpesaProvider>(),
            ProviderNames.AirtelMoney => s.AirtelMoney.IsLive ? services.GetRequiredService<AirtelMoneyProvider>() : services.GetRequiredService<SandboxAirtelMoneyProvider>(),
            ProviderNames.SandboxBank => services.GetRequiredService<SandboxBankProvider>(),
            var b when b.StartsWith(ProviderNames.BankPrefix, StringComparison.OrdinalIgnoreCase) => ResolveBank(b[ProviderNames.BankPrefix.Length..].ToUpperInvariant()),
            _ => throw new NotFoundException("Payment provider", providerName),
        };
    }

    private IPaymentProvider ResolveBank(string code)
    {
        if (!options.Value.Banks.TryGetValue(code, out var cfg) || !cfg.IsLive)
            throw new DomainRuleException("payments.provider.not_configured", $"Bank provider {code} is not configured; set Payments:Banks:{code}:Mode=Live with its credentials, or use {ProviderNames.SandboxBank}.");
        return code switch
        {
            EquityJengaProvider.BankCode => services.GetRequiredService<EquityJengaProvider>(),
            NcbaProvider.BankCode => services.GetRequiredService<NcbaProvider>(),
            _ => throw new DomainRuleException("payments.provider.not_implemented", $"No integration exists for bank code {code}; add a provider class per docs/integrations/payment-providers.md."),
        };
    }

    /// <summary>Everything a caller may name: the mobile-money providers, the sandbox bank, and every live-configured bank.</summary>
    public IReadOnlyList<string> Known =>
        [ProviderNames.MPesa, ProviderNames.AirtelMoney, ProviderNames.SandboxBank, .. options.Value.Banks.Where(b => b.Value.IsLive).Select(b => ProviderNames.BankPrefix + b.Key.ToUpperInvariant())];
}
