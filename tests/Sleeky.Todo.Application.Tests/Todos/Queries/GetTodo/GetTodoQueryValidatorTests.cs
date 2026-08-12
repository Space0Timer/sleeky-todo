using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Queries.GetTodo;
using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Tests.Todos.Queries.GetTodo;

[TestClass]
public sealed class GetTodoQueryValidatorTests
{
    private readonly GetTodoQueryValidator validator = new GetTodoQueryValidator();

    [TestMethod]
    public void ValidateRejectsEmptyIdentifier()
    {
        ValidationResult result = validator.Validate(new GetTodoQuery(Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(GetTodoQuery.Id));
    }

    [TestMethod]
    public void ValidateAcceptsValidIdentifier()
    {
        ValidationResult result = validator.Validate(
            new GetTodoQuery(TestTodoFactory.CreateId("todo-1")));

        result.IsValid.Should().BeTrue();
    }
}
