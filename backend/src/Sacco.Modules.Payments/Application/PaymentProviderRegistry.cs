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
            ProviderNames.AirtelMoney => s.AirtelMoney.IsLive
                ? throw new DomainRuleException("payments.provider.not_implemented", "Airtel Money live integration is not implemented yet; verify the current Airtel Money API and add a provider class.")
                : services.GetRequiredService<SandboxAirtelMoneyProvider>(),
            var b when b.StartsWith(ProviderNames.BankPrefix) => s.Bank.IsLive
                ? throw new DomainRuleException("payments.provider.not_implemented", "Bank API live integration depends on the pilot SACCO's settlement bank; add a provider class.")
                : services.GetRequiredService<SandboxBankProvider>(),
            _ => throw new NotFoundException("Payment provider", providerName),
        };
    }

    public static readonly string[] Known = [ProviderNames.MPesa, ProviderNames.AirtelMoney, ProviderNames.SandboxBank];
}
