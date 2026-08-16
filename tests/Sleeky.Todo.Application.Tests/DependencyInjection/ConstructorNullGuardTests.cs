using System.Reflection;

using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.Todos.Commands.AddDependency;
using Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Application.Todos.Commands.RemoveDependency;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;
using Sleeky.Todo.Application.Todos.Commands.UpdateTodo;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Application.Todos.Queries.GetTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;

namespace Sleeky.Todo.Application.Tests.DependencyInjection;

[TestClass]
public sealed class ConstructorNullGuardTests
{
    private static readonly Type[] DependencyConstructedTypes =
    [
        typeof(RequestLoggingBehavior<ConstructorGuardTestRequest, string>),
        typeof(ValidationBehavior<ConstructorGuardTestRequest, string>),
        typeof(AddDependencyCommandHandler),
        typeof(ChangeTodoStatusCommandHandler),
        typeof(CreateTodoCommandHandler),
        typeof(DeleteTodoCommandHandler),
        typeof(RemoveDependencyCommandHandler),
        typeof(RestoreTodoCommandHandler),
        typeof(UpdateTodoCommandHandler),
        typeof(DependencyCycleDetector),
        typeof(TodoDependencyEvaluator),
        typeof(GetTodoQueryHandler),
        typeof(GetTodosQueryHandler),
    ];

    [TestMethod]
    public void RequiredApplicationDependenciesRejectNullConstructorArguments()
    {
        foreach (Type type in DependencyConstructedTypes)
        {
            ConstructorInfo constructor = type.GetConstructors().Single();
            ParameterInfo[] parameters = constructor.GetParameters();

            for (int nullIndex = 0; nullIndex < parameters.Length; nullIndex++)
            {
                object?[] arguments = parameters
                    .Select(parameter => Substitute.For(
                        [parameter.ParameterType],
                        Array.Empty<object>()))
                    .ToArray();
                arguments[nullIndex] = null;

                Action action = () => constructor.Invoke(arguments);

                TargetInvocationException wrapper = action.Should()
                    .Throw<TargetInvocationException>(
                        $"{type.Name} should reject a null {parameters[nullIndex].Name}")
                    .Which;
                ArgumentNullException exception = wrapper.InnerException.Should()
                    .BeOfType<ArgumentNullException>()
                    .Which;
                exception.ParamName.Should().Be(parameters[nullIndex].Name);
            }
        }
    }
}
