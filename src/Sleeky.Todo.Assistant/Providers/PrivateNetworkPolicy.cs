using System.Net;
using System.Net.Sockets;

namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Decides whether an address belongs to the network the application is running
/// inside rather than to a provider on the internet.
/// </summary>
/// <remarks>
/// A user names the endpoint their assistant runs against, and the request to it
/// leaves from the server. Without a rule of this kind that turns the settings
/// form into a way to reach anything the container can reach — a metadata
/// service, an internal admin API, a database's HTTP interface — from outside
/// the network, using nothing but an ordinary account.
///
/// The judgement is on a resolved <see cref="IPAddress"/> rather than on a host
/// name, because a name proves nothing: it can resolve inside the network on
/// the second lookup even when it resolved outside on the first.
/// </remarks>
public static class PrivateNetworkPolicy
{
    /// <summary>
    /// Whether a request must not be sent to <paramref name="address"/>.
    /// </summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Unwrapped before anything else is asked of it. "::ffff:127.0.0.1" is
        // loopback written as IPv6, and every range test below reads the wrong
        // bytes while it stays in that form — which is the usual way a check
        // like this one is walked past.
        IPAddress candidate = address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;

        if (IPAddress.IsLoopback(candidate))
        {
            return true;
        }

        return candidate.AddressFamily == AddressFamily.InterNetwork
            ? IsBlockedIPv4(candidate)
            : IsBlockedIPv6(candidate);
    }

    private static bool IsBlockedIPv4(IPAddress address)
    {
        byte[] octets = address.GetAddressBytes();

        return octets[0] switch
        {
            // "This network". 0.0.0.0 reaches the local host on some stacks.
            0 => true,

            // RFC 1918 private use.
            10 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,

            // Carrier-grade NAT, which a cloud host may sit behind.
            100 => octets[1] >= 64 && octets[1] <= 127,

            // Link-local, and with it 169.254.169.254 — the address a cloud
            // instance serves its own credentials and configuration on.
            169 => octets[1] == 254,

            // Multicast and reserved, up to and including the broadcast address.
            >= 224 => true,
            _ => false,
        };
    }

    private static bool IsBlockedIPv6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        // Unique local, fc00::/7 — the IPv6 counterpart to RFC 1918.
        byte[] octets = address.GetAddressBytes();

        return (octets[0] & 0xFE) == 0xFC;
    }
}
