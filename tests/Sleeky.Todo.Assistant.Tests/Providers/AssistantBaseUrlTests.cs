using FluentAssertions;

using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Assistant.Tests.Providers;

[TestClass]
public sealed class AssistantBaseUrlTests
{
    [TestMethod]
    [DataRow("http://127.0.0.1:11434/v1")]
    [DataRow("https://10.0.0.5")]
    [DataRow("http://192.168.1.10:8000/v1")]
    [DataRow("http://169.254.169.254/latest/meta-data/")]
    [DataRow("http://[::1]:8080")]
    [DataRow("http://[::ffff:127.0.0.1]")]
    public void AnEndpointNamingAnInternalAddressIsPrivate(string baseUrl)
    {
        AssistantBaseUrl.TryParse(baseUrl, out Uri? uri).Should().BeTrue();

        AssistantBaseUrl.IsPrivate(uri).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("https://api.anthropic.com")]
    [DataRow("https://openrouter.ai/api/v1")]
    [DataRow("https://1.1.1.1")]
    public void AnEndpointNamingAPublicAddressIsNotPrivate(string baseUrl)
    {
        AssistantBaseUrl.TryParse(baseUrl, out Uri? uri).Should().BeTrue();

        AssistantBaseUrl.IsPrivate(uri).Should().BeFalse();
    }

    /// <summary>
    /// An absent endpoint means the provider's own default, which is a public
    /// host, so it must not be caught by a check meant for internal addresses.
    /// </summary>
    [TestMethod]
    public void AnAbsentEndpointIsNotPrivate()
    {
        AssistantBaseUrl.IsPrivate(null).Should().BeFalse();
    }

    /// <summary>
    /// A host name is deliberately left alone here. What it resolves to now is
    /// not what it has to resolve to when the request is made, so this check
    /// does not pretend to answer for it — the connection guard does.
    /// </summary>
    [TestMethod]
    public void AHostNameIsNotJudgedByName()
    {
        AssistantBaseUrl.TryParse("http://localhost:11434/v1", out Uri? uri)
            .Should().BeTrue();

        AssistantBaseUrl.IsPrivate(uri).Should().BeFalse();
    }
}
