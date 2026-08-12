using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Queries.GetTodos;

namespace Sleeky.Todo.Application.Tests.Todos.Queries.GetTodos;

[TestClass]
public sealed class GetTodosQueryValidatorTests
{
    private readonly GetTodosQueryValidator validator = new GetTodosQueryValidator();

    [TestMethod]
    public void DefaultAndMaximumPageSizesAreValid()
    {
        GetTodosQuery defaultQuery = new GetTodosQuery();
        GetTodosQuery maximumQuery = new GetTodosQuery(
            limit: GetTodosQuery.MaximumPageSize);

        ValidationResult defaultResult = validator.Validate(defaultQuery);
        ValidationResult maximumResult = validator.Validate(maximumQuery);

        defaultQuery.Limit.Should().Be(GetTodosQuery.DefaultPageSize);
        defaultResult.IsValid.Should().BeTrue();
        maximumResult.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(101)]
    public void PageSizeOutsideContractIsInvalid(int limit)
    {
        GetTodosQuery query = new GetTodosQuery(limit: limit);

        ValidationResult result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == "Limit");
    }

    [TestMethod]
    public void InvertedDueDateRangeIsInvalid()
    {
        GetTodosQuery query = new GetTodosQuery(
            dueFrom: new DateOnly(2026, 8, 31),
            dueTo: new DateOnly(2026, 8, 1));

        ValidationResult result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == "DueTo");
    }
}
