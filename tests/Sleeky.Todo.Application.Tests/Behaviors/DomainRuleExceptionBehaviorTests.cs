using FluentAssertions;

using MediatR;

using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Behaviors;

[TestClass]
public sealed class DomainRuleExceptionBehaviorTests
{
    [TestMethod]
    public async Task HandleMapsDomainExceptionToApplicationException()
    {
        DomainRuleExceptionBehavior<TestRequest, string> behavior =
            new DomainRuleExceptionBehavior<TestRequest, string>();
        RequestHandlerDelegate<string> next = _ =>
            throw new DomainException("The domain rule was rejected.");

        Func<Task> act = async () => await behavior.Handle(
            new TestRequest("valid"),
            next,
            CancellationToken.None);

        DomainRuleException exception = (await act.Should()
            .ThrowAsync<DomainRuleException>())
            .Which;
        exception.Message.Should().Be("The domain rule was rejected.");
        exception.InnerException.Should().BeOfType<DomainException>();
    }
}
