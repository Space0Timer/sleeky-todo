using FluentAssertions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Assistant.Tests.Providers;

[TestClass]
public sealed class AssistantSettingsServiceTests
{
    private const string UserKey = "sk-user-secret-value";

    private const string ApplicationKey = "sk-app-secret-value";

    /// <summary>
    /// A leaked database has to be worth nothing on its own, so what lands in
    /// persistence is never the key the user typed.
    /// </summary>
    [TestMethod]
    public async Task SaveStoresTheKeyAsCiphertext()
    {
        Harness harness = new Harness();

        await harness.Service.SaveAsync(Input(UserKey));

        AssistantSettingsRecord stored = harness.Repository.Saved.Single();
        stored.ProtectedApiKey.Should().NotBeNull();
        stored.ProtectedApiKey.Should().NotContain(UserKey);
    }

    /// <summary>
    /// A key can be replaced but never retrieved, so a stolen session cannot be
    /// used to walk away with the user's credentials.
    /// </summary>
    [TestMethod]
    public async Task DescribeReportsThatAKeyExistsWithoutRevealingIt()
    {
        Harness harness = new Harness();
        await harness.Service.SaveAsync(Input(UserKey));

        AssistantSettingsView view = await harness.Service.DescribeAsync();

        view.HasKey.Should().BeTrue();
        view.IsUsable.Should().BeTrue();
        view.Model.Should().Be("claude-sonnet-5");
        view.ToString().Should().NotContain(UserKey);
    }

    /// <summary>
    /// The user cannot read their key back to resubmit it, so a save that omits
    /// it is editing the model or the endpoint rather than clearing the key.
    /// </summary>
    [TestMethod]
    public async Task SaveWithoutAKeyKeepsTheStoredOne()
    {
        Harness harness = new Harness();
        await harness.Service.SaveAsync(Input(UserKey));

        await harness.Service.SaveAsync(Input(apiKey: null, model: "claude-opus-5"));

        AssistantConnection? resolved = await harness.Service.ResolveAsync();
        resolved.Should().NotBeNull();
        resolved!.Model.Should().Be("claude-opus-5");
        resolved.ApiKey.Should().Be(UserKey);
    }

    [TestMethod]
    public async Task ResolvePrefersTheUsersOwnConnection()
    {
        Harness harness = new Harness(applicationKey: ApplicationKey);
        await harness.Service.SaveAsync(Input(UserKey));

        AssistantConnection? resolved = await harness.Service.ResolveAsync();

        resolved!.Source.Should().Be(AssistantConnectionSource.User);
        resolved.ApiKey.Should().Be(UserKey);
    }

    [TestMethod]
    public async Task ResolveFallsBackToTheApplicationWhenTheUserHasNoKey()
    {
        Harness harness = new Harness(applicationKey: ApplicationKey);

        AssistantConnection? resolved = await harness.Service.ResolveAsync();

        resolved!.Source.Should().Be(AssistantConnectionSource.Application);
        resolved.ApiKey.Should().Be(ApplicationKey);
    }

    /// <summary>
    /// Falling back key-only would pair the application's credential with the
    /// user's chosen model, which is wrong whenever the two name different
    /// providers.
    /// </summary>
    [TestMethod]
    public async Task ResolveNeverPairsOneSourcesKeyWithAnothersModel()
    {
        Harness harness = new Harness(applicationKey: ApplicationKey);
        await harness.Repository.SaveAsync(new AssistantSettingsRecord(
            TestTodo.OwnerId,
            AssistantProvider.OpenAiCompatible.ToString(),
            "https://openrouter.ai/api/v1",
            "some/local-model",
            ProtectedApiKey: null));

        AssistantConnection? resolved = await harness.Service.ResolveAsync();

        resolved!.Source.Should().Be(AssistantConnectionSource.Application);
        resolved.Provider.Should().Be(AssistantProvider.Anthropic);
        resolved.Model.Should().Be("claude-sonnet-5");
        resolved.BaseUrl.Should().BeNull();
    }

    [TestMethod]
    public async Task ResolveReportsNothingUsableWhenNoKeyExistsAnywhere()
    {
        Harness harness = new Harness();

        AssistantConnection? resolved = await harness.Service.ResolveAsync();

        resolved.Should().BeNull();
        (await harness.Service.DescribeAsync()).IsUsable.Should().BeFalse();
    }

    /// <summary>
    /// What a rotated or lost key ring looks like. The record survives so the
    /// user replaces the key rather than rebuilding the configuration.
    /// </summary>
    [TestMethod]
    public async Task AnUnreadableStoredKeyLeavesTheConfigurationIntactButUnusable()
    {
        Harness harness = new Harness();
        await harness.Repository.SaveAsync(new AssistantSettingsRecord(
            TestTodo.OwnerId,
            AssistantProvider.Anthropic.ToString(),
            BaseUrl: null,
            "claude-sonnet-5",
            "not-ciphertext-this-key-ring-cannot-read"));

        AssistantSettingsView view = await harness.Service.DescribeAsync();

        view.HasKey.Should().BeTrue();
        view.IsUsable.Should().BeFalse();
        (await harness.Service.ResolveAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task DeleteReportsWhetherThereWasAnythingToRemove()
    {
        Harness harness = new Harness();

        (await harness.Service.DeleteAsync()).Should().BeFalse();
        await harness.Service.SaveAsync(Input(UserKey));
        (await harness.Service.DeleteAsync()).Should().BeTrue();
    }

    [TestMethod]
    public async Task SaveTreatsABlankEndpointAsUnset()
    {
        Harness harness = new Harness();

        await harness.Service.SaveAsync(new AssistantSettingsInput(
            AssistantProvider.OpenAiCompatible,
            "   ",
            "gpt-4o-mini",
            UserKey));

        harness.Repository.Saved.Single().BaseUrl.Should().BeNull();
    }

    private static AssistantSettingsInput Input(
        string? apiKey,
        string model = "claude-sonnet-5")
    {
        return new AssistantSettingsInput(
            AssistantProvider.Anthropic,
            BaseUrl: null,
            model,
            apiKey);
    }

    private sealed class Harness
    {
        public Harness(string? applicationKey = null)
        {
            this.Repository = new InMemoryAssistantSettingsRepository();
            this.Service = new AssistantSettingsService(
                this.Repository,
                new AssistantKeyProtector(new EphemeralDataProtectionProvider()),
                new TestCurrentUser(),
                Options.Create(new AssistantOptions
                {
                    Provider = AssistantProvider.Anthropic,
                    Model = "claude-sonnet-5",
                    ApiKey = applicationKey,
                }));
        }

        public InMemoryAssistantSettingsRepository Repository { get; }

        public AssistantSettingsService Service { get; }
    }
}
