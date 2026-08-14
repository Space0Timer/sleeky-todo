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
        AssistantSettingsRecord? stored = await this.repository.GetAsync(
            this.currentUser.UserId,
            cancellationToken);
        AssistantConnection? resolved = this.Resolve(stored);

        if (stored is not null)
        {
            return new AssistantSettingsView(
                stored.Provider,
                stored.BaseUrl,
                stored.Model,
                HasKey: stored.ProtectedApiKey is not null,
                IsUsable: resolved is not null,
                Source: (resolved?.Source ?? AssistantConnectionSource.User).ToString());
        }

        return new AssistantSettingsView(
            this.options.Provider.ToString(),
            this.options.BaseUrl,
            this.options.Model,
            HasKey: false,
            IsUsable: resolved is not null,
            Source: AssistantConnectionSource.Application.ToString());
    }

    public async Task<AssistantConnection?> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        AssistantSettingsRecord? stored = await this.repository.GetAsync(
            this.currentUser.UserId,
            cancellationToken);

        return this.Resolve(stored);
    }

    public async Task SaveAsync(
        AssistantSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        AssistantSettingsRecord? stored = await this.repository.GetAsync(
            this.currentUser.UserId,
            cancellationToken);

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

    private static Uri? ParseBaseUrl(string? baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsed) ? parsed : null;
    }

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
            ParseBaseUrl(stored.BaseUrl),
            AssistantConnectionSource.User);
    }

    private AssistantConnection? ResolveApplication()
    {
        if (string.IsNullOrWhiteSpace(this.options.ApiKey)
            || string.IsNullOrWhiteSpace(this.options.Model))
        {
            return null;
        }

        return new AssistantConnection(
            this.options.Provider,
            this.options.Model,
            this.options.ApiKey,
            ParseBaseUrl(this.options.BaseUrl),
            AssistantConnectionSource.Application);
    }
}
