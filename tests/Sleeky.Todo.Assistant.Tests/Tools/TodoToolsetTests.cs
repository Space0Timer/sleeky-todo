using System.Text.Json;

using FluentAssertions;

using MediatR;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.Tests.Tools;

[TestClass]
public sealed class TodoToolsetTests
{
    [TestMethod]
    public void CreateExposesExactlyTheSixOperations()
    {
        IReadOnlyList<AITool> toolset = TodoToolset.Create(BuildTools());

        toolset.Select(tool => tool.Name)
            .Should()
            .BeEquivalentTo(
                TodoToolNames.GetTodos,
                TodoToolNames.GetTodoSelection,
                TodoToolNames.CreateTodo,
                TodoToolNames.ChangeTodoStatus,
                TodoToolNames.DeleteTodos,
                TodoToolNames.RestoreTodos);
    }

    /// <summary>
    /// Declared rather than merely enforced, so a model never composes a batch
    /// that was doomed before it was sent.
    /// </summary>
    [TestMethod]
    public void CreateDeclaresTheBatchCapOnEveryBulkTool()
    {
        IReadOnlyList<AITool> toolset = TodoToolset.Create(BuildTools());
        string[] bulkTools =
        [
            TodoToolNames.ChangeTodoStatus,
            TodoToolNames.DeleteTodos,
            TodoToolNames.RestoreTodos,
        ];

        foreach (string name in bulkTools)
        {
            Find(toolset, name)
                .Description
                .Should()
                .Contain(BulkTodoLimits.MaximumSelectionSize.ToString());
        }
    }

    [TestMethod]
    public void CreateTakesIdentifiersAsStringsAndNeverVersions()
    {
        AIFunction changeStatus = Find(TodoToolset.Create(BuildTools()), TodoToolNames.ChangeTodoStatus);

        JsonElement properties = changeStatus.JsonSchema.GetProperty("properties");
        JsonElement ids = properties.GetProperty("ids");

        ids.GetProperty("type").GetString().Should().Be("array");
        ids.GetProperty("items").GetProperty("type").GetString().Should().Be("string");
        properties.TryGetProperty("version", out JsonElement _).Should().BeFalse();
        properties.TryGetProperty("versions", out JsonElement _).Should().BeFalse();
    }

    /// <summary>
    /// The token is plumbing, not something a model supplies.
    /// </summary>
    [TestMethod]
    public void CreateKeepsTheCancellationTokenOutOfEverySchema()
    {
        foreach (AITool tool in TodoToolset.Create(BuildTools()))
        {
            AIFunction function = (AIFunction)tool;

            function.JsonSchema
                .GetProperty("properties")
                .TryGetProperty("cancellationToken", out JsonElement _)
                .Should()
                .BeFalse();
        }
    }

    /// <summary>
    /// A parameter with no default is required in the generated schema, and the
    /// binder throws when a call omits one — which the loop reports to the model
    /// as a generic failure naming nothing. Every optional filter must therefore
    /// be optional in the schema, or asking for "everything due this week"
    /// fails because it named no status.
    /// </summary>
    [TestMethod]
    public void CreateRequiresOnlyWhatATodoCannotBeReadOrMadeWithout()
    {
        IReadOnlyList<AITool> toolset = TodoToolset.Create(BuildTools());

        Required(toolset, TodoToolNames.GetTodos).Should().BeEmpty();
        Required(toolset, TodoToolNames.CreateTodo)
            .Should().BeEquivalentTo("name", "dueDate", "priority");
        Required(toolset, TodoToolNames.ChangeTodoStatus)
            .Should().BeEquivalentTo("status", "ids");
        Required(toolset, TodoToolNames.DeleteTodos).Should().BeEquivalentTo("ids");
        Required(toolset, TodoToolNames.RestoreTodos).Should().BeEquivalentTo("ids");
        Required(toolset, TodoToolNames.GetTodoSelection).Should().BeEquivalentTo("ids");
    }

    /// <summary>
    /// Search is a plain optional string on the read tool. The tool list has to
    /// be identical on every request for a provider's prefix caching to hold,
    /// so this parameter is declared unconditionally rather than added when a
    /// turn happens to need it.
    /// </summary>
    [TestMethod]
    public void CreateOffersSearchAsAnOptionalStringOnTheReadTool()
    {
        AIFunction getTodos = Find(TodoToolset.Create(BuildTools()), TodoToolNames.GetTodos);

        JsonElement search = getTodos.JsonSchema
            .GetProperty("properties")
            .GetProperty("search");

        search.GetProperty("description").GetString().Should().Contain("start of a word");
        Required(TodoToolset.Create(BuildTools()), TodoToolNames.GetTodos)
            .Should().NotContain("search");
    }

    [TestMethod]
    public void CreateDescribesWhenToCallEachTool()
    {
        foreach (AITool tool in TodoToolset.Create(BuildTools()))
        {
            tool.Description.Should().Contain("Call this");
        }
    }

    private static IEnumerable<string> Required(IReadOnlyList<AITool> toolset, string name)
    {
        JsonElement schema = Find(toolset, name).JsonSchema;

        return schema.TryGetProperty("required", out JsonElement required)
            ? required.EnumerateArray().Select(value => value.GetString()!)
            : Array.Empty<string>();
    }

    private static AIFunction Find(IReadOnlyList<AITool> toolset, string name)
    {
        return (AIFunction)toolset.Single(tool => tool.Name == name);
    }

    private static TodoTools BuildTools()
    {
        return new TodoTools(
            Substitute.For<ISender>(),
            Substitute.For<IBulkConflictPolicy>(),
            new TodoVersionLedger(),
            Substitute.For<ITurnEventWriter>(),
            Substitute.For<ITurnController>(),
            NullLogger<TodoTools>.Instance);
    }
}
