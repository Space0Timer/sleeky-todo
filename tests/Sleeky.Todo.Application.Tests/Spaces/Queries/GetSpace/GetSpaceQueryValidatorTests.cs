using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Queries.GetSpace;

namespace Sleeky.Todo.Application.Tests.Spaces.Queries.GetSpace;

[TestClass]
public sealed class GetSpaceQueryValidatorTests
{
    private readonly GetSpaceQueryValidator validator = new GetSpaceQueryValidator();

    [TestMethod]
    public void ValidateAcceptsASpaceIdentifier()
    {
        ValidationResult result = validator.Validate(new GetSpaceQuery(TestSpaceFactory.SpaceId));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySpaceIdentifier()
    {
        ValidationResult result = validator.Validate(new GetSpaceQuery(Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(GetSpaceQuery.SpaceId));
    }
}
