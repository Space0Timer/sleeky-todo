namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// The names the model calls and the stream reports. Named once because they
/// appear in three places that must agree: the schema the model is given, the
/// <c>tool_executed</c> event, and the confirmation a person answers.
/// </summary>
public static class TodoToolNames
{
    public const string GetTodos = "get_todos";

    public const string GetTodoSelection = "get_todo_selection";

    public const string CreateTodo = "create_todo";

    public const string ChangeTodoStatus = "change_todo_status";

    public const string DeleteTodos = "delete_todos";

    public const string RestoreTodos = "restore_todos";
}
