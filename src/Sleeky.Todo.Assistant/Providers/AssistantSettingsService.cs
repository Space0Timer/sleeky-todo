using Microsoft.Extensions.Options;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;

namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Resolves whose provider, whose model, and whose key a turn runs on.
/// </summary>
/// <remarks>
/// A connection comes wholly from one source. Falling back key-only would pair
/// the application's credential with the user's chosen model, which is wrong
/// whenever the two name different providers, so a user record that cannot
/// produce a usable key is set aside entirely rather than patched.
/// </remarks>
public sealed class AssistantSettingsService : IAssistantSettingsService
{
    private readonly IAssistantSettingsRepository repository;

    private readonly AssistantKeyProtector protector;

    private readonly ICurrentUser currentUser;

    private readonly AssistantOptions options;

    public AssistantSettingsService(
        IAssistantSettingsRepository repository,
        AssistantKeyProtector protector,
        ICurrentUser currentUser,
        IOptions<AssistantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(options);

        this.repository = repository;
        this.protector = protector;
        this.currentUser = currentUser;
        this.options = options.Value;
    }

    public async Task<AssistantSettingsView> DescribeAsync(
        CancellationToken cancellationToken = default)
    {
        AssistantSettingsRecord? stored = await this.GetStoredAsync(cancellationToken);
        AssistantConnection? resolved = this.Resolve(stored);

        return stored is null
            ? this.DescribeApplicationFallback(resolved)
            : DescribeStored(stored, resolved);
    }

    public async Task<AssistantConnection?> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        AssistantSettingsRecord? stored = await this.GetStoredAsync(cancellationToken);

        return this.Resolve(stored);
    }

    public async Task<AssistantConnection?> ResolveDraftAsync(
        AssistantSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Model)
            || !AssistantBaseUrl.TryParse(input.BaseUrl, out Uri? baseUrl)
            || this.IsRefusedEndpoint(baseUrl))
        {
            return null;
        }

        string? apiKey = await this.ResolveDraftKeyAsync(input.ApiKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new AssistantConnection(
            input.Provider,
            input.Model,
            apiKey,
            baseUrl,
            AssistantConnectionSource.User);
    }

    public async Task SaveAsync(
        AssistantSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        AssistantSettingsRecord? stored = await this.GetStoredAsync(cancellationToken);

        // An absent key keeps whatever is stored, because the user cannot read
        // their key back to resubmit it alongside a model change.
        string? protectedKey = string.IsNullOrWhiteSpace(input.ApiKey)
            ? stored?.ProtectedApiKey
            : this.protector.Protect(input.ApiKey);

        await this.repository.SaveAsync(
            new AssistantSettingsRecord(
                this.currentUser.UserId,
                input.Provider.ToString(),
                NormalizeBaseUrl(input.BaseUrl),
                input.Model,
                protectedKey),
            cancellationToken);
    }

    public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        return this.repository.DeleteAsync(this.currentUser.UserId, cancellationToken);
    }

    private static string? NormalizeBaseUrl(string? baseUrl)
    {
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim();
    }

    /// <summary>
    /// The user's own configuration, which is what their settings form edits.
    /// </summary>
    /// <remarks>
    /// <c>IsUsable</c> therefore describes <em>that</em> configuration rather
    /// than whether any connection resolved: a stored record the resolver set
    /// aside is unusable even though the application fallback will carry the
    /// next turn, and <c>Source</c> says which of the two is actually running.
    /// </remarks>
    private static AssistantSettingsView DescribeStored(
        AssistantSettingsRecord stored,
        AssistantConnection? resolved)
    {
        return new AssistantSettingsView(
            stored.Provider,
            stored.BaseUrl,
            stored.Model,
            HasKey: stored.ProtectedApiKey is not null,
            IsUsable: resolved?.Source == AssistantConnectionSource.User,
            Source: (resolved?.Source ?? AssistantConnectionSource.User).ToString());
    }

    /// <summary>
    /// What a user with no settings of their own is running on: the
    /// application's provider and model, usable only if the operator supplied
    /// a key.
    /// </summary>
    private AssistantSettingsView DescribeApplicationFallback(AssistantConnection? resolved)
    {
        return new AssistantSettingsView(
            this.options.Provider.ToString(),
            this.options.BaseUrl,
            this.options.Model,
            HasKey: false,
            IsUsable: resolved is not null,
            Source: AssistantConnectionSource.Application.ToString());
    }

    /// <summary>
    /// The same rule the save follows: an absent key means the stored one,
    /// because the user cannot read it back to retype it.
    /// </summary>
    private async Task<string?> ResolveDraftKeyAsync(
        string? draftKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(draftKey))
        {
            return draftKey;
        }

        AssistantSettingsRecord? stored = await this.GetStoredAsync(cancellationToken);

        return this.protector.Unprotect(stored?.ProtectedApiKey);
    }

    private Task<AssistantSettingsRecord?> GetStoredAsync(CancellationToken cancellationToken)
    {
        return this.repository.GetAsync(this.currentUser.UserId, cancellationToken);
    }

    /// <summary>
    /// Whether a user's endpoint names an address the policy will not permit.
    /// Applies to user settings only; the application's own endpoint is
    /// operator configuration and is resolved without this.
    /// </summary>
    private bool IsRefusedEndpoint(Uri? baseUrl)
    {
        return !this.options.AllowPrivateEndpoints && AssistantBaseUrl.IsPrivate(baseUrl);
    }

    /// <summary>
    /// The user's connection when they have a usable one, else the
    /// application's; never a blend of the two.
    /// </summary>
    private AssistantConnection? Resolve(AssistantSettingsRecord? stored)
    {
        AssistantConnection? user = this.ResolveUser(stored);

        return user ?? this.ResolveApplication();
    }

    private AssistantConnection? ResolveUser(AssistantSettingsRecord? stored)
    {
        if (stored is null || string.IsNullOrWhiteSpace(stored.Model))
        {
            return null;
        }

        if (!Enum.TryParse(stored.Provider, out AssistantProvider provider))
        {
            return null;
        }

        // Refused rather than dropped: an unset endpoint means the provider's
        // own default, so running with a base URL the user cannot see was
        // ignored would send their key somewhere they never named.
        if (!AssistantBaseUrl.TryParse(stored.BaseUrl, out Uri? baseUrl))
        {
            return null;
        }

        // A record saved before the policy was in force, or while it was
        // relaxed, is judged now rather than at the point it was written.
        if (this.IsRefusedEndpoint(baseUrl))
        {
            return null;
        }

        // A key that will not unprotect is what a rotated or lost key ring
        // looks like. The record stays, so the user replaces the key rather
        // than rebuilding the whole configuration.
        string? apiKey = this.protector.Unprotect(stored.ProtectedApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new AssistantConnection(
            provider,
            stored.Model,
            apiKey,
            baseUrl,
            AssistantConnectionSource.User);
    }

    private AssistantConnection? ResolveApplication()
    {
        if (string.IsNullOrWhiteSpace(this.options.ApiKey)
            || string.IsNullOrWhiteSpace(this.options.Model))
        {
            return null;
        }

        if (!AssistantBaseUrl.TryParse(this.options.BaseUrl, out Uri? baseUrl))
        {
            return null;
        }

        return new AssistantConnection(
            this.options.Provider,
            this.options.Model,
            this.options.ApiKey,
            baseUrl,
            AssistantConnectionSource.Application);
    }
}
