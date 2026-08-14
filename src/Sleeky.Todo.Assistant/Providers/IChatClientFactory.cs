using Microsoft.Extensions.AI;

namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Builds the client a turn runs on. The only place in the assistant that knows
/// a provider SDK exists.
/// </summary>
public interface IChatClientFactory
{
    IChatClient Create(AssistantConnection connection);
}
