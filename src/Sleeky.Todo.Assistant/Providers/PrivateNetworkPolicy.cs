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
        IPAddress candidate = UnwrapEmbeddedIPv4(address);

        if (IPAddress.IsLoopback(candidate))
        {
            return true;
        }

        return candidate.AddressFamily == AddressFamily.InterNetwork
            ? IsBlockedIPv4(candidate)
            : IsBlockedIPv6(candidate);
    }

    /// <summary>
    /// Rewrites an IPv6 address that carries an IPv4 one inside it to the
    /// address it actually reaches, so the IPv4 ranges below judge it.
    /// </summary>
    /// <remarks>
    /// There are three of these encodings and only the first is well known.
    /// "::ffff:169.254.169.254" is the IPv4-mapped form. "::169.254.169.254" is
    /// the IPv4-compatible form, deprecated by RFC 4291 and not put onto IPv4 by
    /// a current stack, but free to be written. "64:ff9b::169.254.169.254" is
    /// the NAT64 well-known prefix of RFC 6052, and on a network with a NAT64
    /// gateway — the ordinary shape of an IPv6-only cloud subnet — it is
    /// translated onto the embedded address and arrives at the metadata service.
    ///
    /// Judging all three by what they embed costs nothing and removes the need
    /// to know which of them the host network happens to translate.
    /// </remarks>
    private static IPAddress UnwrapEmbeddedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        byte[] octets = address.GetAddressBytes();

        // "::" and "::1" are answered as themselves by IPv6Any and IsLoopback,
        // so they are the only two the IPv4-compatible range hands back
        // untouched. Everything else in ::/96 is read as what it embeds —
        // including 0.0.0.0/8, which the IPv4 rules refuse and which a wider
        // guard here would otherwise let through.
        if (IsZeroThrough(octets, 0, 12)
            && !(IsZeroThrough(octets, 12, 15) && octets[15] <= 1))
        {
            return new IPAddress(octets[12..]);
        }

        if (IsNat64WellKnownPrefix(octets))
        {
            return new IPAddress(octets[12..]);
        }

        return address;
    }

    /// <summary>
    /// Whether the address is the NAT64 well-known prefix of RFC 6052,
    /// 64:ff9b::/96, whose last four octets are the IPv4 address verbatim.
    /// </summary>
    private static bool IsNat64WellKnownPrefix(byte[] octets)
    {
        return IsNat64Range(octets) && IsZeroThrough(octets, 4, 12);
    }

    /// <summary>
    /// Whether the address sits anywhere in 64:ff9b::/32, the range reserved for
    /// NAT64 translation.
    /// </summary>
    /// <remarks>
    /// Covers the RFC 8215 local-use prefix 64:ff9b:1::/48 as well as the
    /// well-known one. Only the well-known prefix carries the IPv4 address in
    /// its last four octets; at other prefix lengths RFC 6052 splits it around
    /// the reserved octet, so the rest of the range is refused outright rather
    /// than decoded. Nothing is lost by that: the whole range exists to be
    /// translated onto IPv4, so no provider is reachable at one of these
    /// addresses in its own right.
    ///
    /// Operator-chosen network-specific prefixes are ordinary global unicast and
    /// cannot be recognised from the address alone. A deployment using one, and
    /// wanting this guard to hold, has to keep the assistant off a NAT64 path or
    /// name the prefix in configuration.
    /// </remarks>
    private static bool IsNat64Range(byte[] octets)
    {
        return octets[0] == 0x00
            && octets[1] == 0x64
            && octets[2] == 0xFF
            && octets[3] == 0x9B;
    }

    private static bool IsZeroThrough(byte[] octets, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (octets[index] != 0)
            {
                return false;
            }
        }

        return true;
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

        // Anything still in 64:ff9b::/32 here carries its IPv4 address in a
        // position this code does not decode, so it is refused rather than read.
        if (IsNat64Range(address.GetAddressBytes()))
        {
            return true;
        }

        // Unique local, fc00::/7 — the IPv6 counterpart to RFC 1918.
        byte[] octets = address.GetAddressBytes();

        return (octets[0] & 0xFE) == 0xFC;
    }
}
