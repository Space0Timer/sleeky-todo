using Microsoft.Extensions.AI;

using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Stands in for a provider so a whole turn can run against the real host.
/// </summary>
/// <remarks>
/// The unit suite drives the loop through this same idea, but stops at the
/// loop's edge. Put here instead, it lets one test exercise everything between
/// the HTTP request and MongoDB: the event stream, the loop, the tool layer,
/// the MediatR pipeline, and the ownership boundary.
///
/// The same client is returned for every connection, and the test scripts it
/// before making the request, because the identifiers it has to name are only
/// known once the fixture has been created.
/// </remarks>
internal sealed class ScriptedChatClientFactory : IChatClientFactory
{
    public ScriptedChatClient Client { get; } = new ScriptedChatClient();

    public IChatClient Create(AssistantConnection connection) => Client;
}
