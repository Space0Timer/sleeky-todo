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
