using System.Net;
using Microsoft.Extensions.Options;
using Sacco.Modules.Payments.Providers;

namespace Sacco.Modules.Payments.Application;

/// <summary>
/// Network-level guard for provider callbacks. Daraja callbacks are not signed, so the usual
/// defence is restricting the callback endpoint to the provider's published source ranges; the
/// idempotency table remains the guarantee against replay regardless.
/// </summary>
public sealed class WebhookSourceGuard(IOptions<PaymentsSettings> options)
{
    public bool IsAllowed(IPAddress? remote)
    {
        var cidrs = options.Value.WebhookAllowedCidrs;
        if (cidrs.Count == 0) return true;
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        foreach (var entry in cidrs)
        {
            var parts = entry.Split('/');
            if (!IPAddress.TryParse(parts[0].Trim(), out var network)) continue;
            var prefix = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : (network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            if (network.AddressFamily != remote.AddressFamily) continue;
            if (Matches(remote.GetAddressBytes(), network.GetAddressBytes(), prefix)) return true;
        }
        return false;
    }

    private static bool Matches(byte[] a, byte[] b, int prefix)
    {
        var fullBytes = prefix / 8; var rem = prefix % 8;
        for (var i = 0; i < fullBytes; i++) if (a[i] != b[i]) return false;
        if (rem == 0) return true;
        var mask = (byte)(0xFF << (8 - rem));
        return (a[fullBytes] & mask) == (b[fullBytes] & mask);
    }
}
