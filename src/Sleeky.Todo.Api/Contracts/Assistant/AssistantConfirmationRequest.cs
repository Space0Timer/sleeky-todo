namespace Sleeky.Todo.Api.Contracts.Assistant;

/// <summary>
/// A person's answer to a destructive proposal, carrying back the versions the
/// proposal displayed.
/// </summary>
public sealed class AssistantConfirmationRequest
{
    public string Tool { get; init; } = string.Empty;

    public IReadOnlyCollection<AssistantConfirmationItem> Items { get; init; } =
        Array.Empty<AssistantConfirmationItem>();
}
