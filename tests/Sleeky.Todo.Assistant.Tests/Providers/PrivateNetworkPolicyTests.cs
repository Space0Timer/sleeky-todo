using System.Net;

using FluentAssertions;

using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Assistant.Tests.Providers;

[TestClass]
public sealed class PrivateNetworkPolicyTests
{
    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("127.1.2.3")]
    [DataRow("10.0.0.5")]
    [DataRow("172.16.0.1")]
    [DataRow("172.31.255.254")]
    [DataRow("192.168.1.1")]
    [DataRow("0.0.0.0")]
    [DataRow("100.64.0.1")]
    [DataRow("224.0.0.1")]
    [DataRow("255.255.255.255")]
    public void AddressesInsideTheNetworkAreBlocked(string address)
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    /// <summary>
    /// The address a cloud instance serves its own credentials on. Reaching it
    /// from a server-side request is the outcome this policy exists to prevent,
    /// so it is named rather than left to the link-local range that covers it.
    /// </summary>
    [TestMethod]
    public void TheCloudMetadataAddressIsBlocked()
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse("169.254.169.254"))
            .Should().BeTrue();
    }

    /// <summary>
    /// Loopback written as IPv6. Every range test reads the wrong bytes while
    /// the address stays in this form, which is the ordinary way a check like
    /// this one is walked past — so it is asserted rather than assumed.
    /// </summary>
    [TestMethod]
    [DataRow("::ffff:127.0.0.1")]
    [DataRow("::ffff:169.254.169.254")]
    [DataRow("::ffff:10.0.0.1")]
    public void AddressesMappedIntoIPv6AreBlocked(string address)
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    /// <summary>
    /// The two embedded-IPv4 encodings that are not the mapped one.
    /// </summary>
    /// <remarks>
    /// "::a.b.c.d" is the IPv4-compatible form and "64:ff9b::a.b.c.d" the NAT64
    /// well-known prefix. Neither is IPv4-mapped, neither is link-local, site
    /// local or multicast, and both begin 0x00, so every branch of the IPv6
    /// rules answers no and the address is permitted unless it is unwrapped
    /// first. On a network with a NAT64 gateway the second one arrives at the
    /// metadata service.
    /// </remarks>
    [TestMethod]
    [DataRow("::169.254.169.254")]
    [DataRow("::10.0.0.1")]
    [DataRow("::192.168.1.1")]
    [DataRow("64:ff9b::169.254.169.254")]
    [DataRow("64:ff9b::10.0.0.1")]
    [DataRow("64:ff9b::127.0.0.1")]
    [DataRow("::0.1.2.3")]
    public void AddressesEmbeddedInIPv6ByAnotherEncodingAreBlocked(string address)
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    /// <summary>
    /// The rest of 64:ff9b::/32, which exists only to be translated onto IPv4.
    /// </summary>
    /// <remarks>
    /// Only the well-known /96 keeps the IPv4 address in its last four octets.
    /// RFC 8215's local-use 64:ff9b:1::/48 and the other RFC 6052 prefix lengths
    /// split it around the reserved octet, so they are refused rather than
    /// decoded — a gateway would still put them onto the embedded address.
    /// </remarks>
    [TestMethod]
    [DataRow("64:ff9b:1::a9fe:a9fe")]
    [DataRow("64:ff9b:1::1")]
    [DataRow("64:ff9b:abcd::1")]
    public void TheRestOfTheNat64RangeIsBlocked(string address)
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    /// <summary>
    /// Unwrapping must not swallow the two addresses that live at the bottom of
    /// ::/96 and are already answered as themselves.
    /// </summary>
    [TestMethod]
    [DataRow("::1")]
    [DataRow("::")]
    [DataRow("fe80::1")]
    [DataRow("fc00::1")]
    [DataRow("fd12:3456::1")]
    [DataRow("ff02::1")]
    public void IPv6AddressesInsideTheNetworkAreBlocked(string address)
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();
    }

    /// <summary>
    /// The policy has to leave the actual providers reachable, so the ranges a
    /// real endpoint resolves to are asserted alongside the ones it refuses.
    /// </summary>
    [TestMethod]
    [DataRow("1.1.1.1")]
    [DataRow("8.8.8.8")]
    [DataRow("160.79.104.10")]
    [DataRow("172.15.255.255")]
    [DataRow("172.32.0.1")]
    [DataRow("192.167.255.255")]
    [DataRow("100.63.255.255")]
    [DataRow("2606:4700::1111")]
    public void PublicAddressesArePermitted(string address)
    {
        PrivateNetworkPolicy.IsBlocked(IPAddress.Parse(address)).Should().BeFalse();
    }

    [TestMethod]
    public void ANullAddressIsRejected()
    {
        Action act = () => PrivateNetworkPolicy.IsBlocked(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
