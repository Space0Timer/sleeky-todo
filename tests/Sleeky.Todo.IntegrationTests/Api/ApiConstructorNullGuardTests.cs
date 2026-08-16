using FluentAssertions;

using Sleeky.Todo.Api.Controllers;
using Sleeky.Todo.Api.ErrorHandling;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class ApiConstructorNullGuardTests
{
    [TestMethod]
    public void TodosControllerRejectsNullSender()
    {
        Action action = () => _ = new TodosController(null!);

        action.Should()
            .Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("sender");
    }

    [TestMethod]
    public void SpacesControllerRejectsNullSender()
    {
        Action action = () => _ = new SpacesController(null!);

        action.Should()
            .Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("sender");
    }

    [TestMethod]
    public void ApiExceptionHandlerRejectsNullLogger()
    {
        Action action = () => _ = new ApiExceptionHandler(null!);

        action.Should()
            .Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }
}
