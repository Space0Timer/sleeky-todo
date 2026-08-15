namespace Sleeky.Todo.Api.Hosting;

/// <summary>
/// Bounds on what one authenticated user may ask of the API at once.
/// </summary>
/// <remarks>
/// Every endpoint worth bounding is behind an authorization policy already, so
/// these are limits on abuse and on a client that has gone wrong, not a defence
/// against anonymous traffic. The defaults sit far above what using the
/// application looks like, so reaching one means something is repeating.
/// </remarks>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the number of assistant turns one user may have running at once.
    /// A turn holds a request scope, a database session, and a provider call
    /// open for as long as it lasts, so this is a count of held resources
    /// rather than a rate.
    /// </summary>
    public int AssistantTurnConcurrency { get; init; } = 2;

    /// <summary>
    /// Gets the number of mutating requests one user may make per
    /// <see cref="MutationWindow"/>. Reads are not counted.
    /// </summary>
    public int MutationPermitLimit { get; init; } = 120;

    public TimeSpan MutationWindow { get; init; } = TimeSpan.FromMinutes(1);
}
