namespace Sleeky.Todo.Application.Todos.Dependencies;

public sealed record TodoDependencyState(int IncompleteDependencyCount)
{
    public bool IsBlocked => IncompleteDependencyCount > 0;
}
