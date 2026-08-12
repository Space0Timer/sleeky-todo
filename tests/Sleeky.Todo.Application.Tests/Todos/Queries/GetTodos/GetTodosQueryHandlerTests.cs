using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Queries.GetTodos;

[TestClass]
public sealed class GetTodosQueryHandlerTests
{
    private readonly ITodoListReader listReader = Substitute.For<ITodoListReader>();

    [TestMethod]
    public async Task DefaultPageReturnsFiftyItemsAndNextCursor()
    {
        TodoListCriteria? capturedCriteria = null;
        TodoListItemDto[] storedItems = Enumerable.Range(1, 51)
            .Select(index => CreateItem(index))
            .ToArray();
        listReader.GetTodosAsync(
                Arg.Do<TodoListCriteria>(criteria => capturedCriteria = criteria),
                Arg.Any<CancellationToken>())
            .Returns(storedItems);
        GetTodosQuery query = new GetTodosQuery();
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        CursorPage<TodoListItemDto> page = await handler.Handle(
            query,
            CancellationToken.None);

        page.Items.Should().HaveCount(GetTodosQuery.DefaultPageSize);
        page.NextCursor.Should().NotBeNullOrWhiteSpace();
        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Limit.Should().Be(GetTodosQuery.DefaultPageSize + 1);
        capturedCriteria.Scope.Should().Be(TodoListScope.Active);
        capturedCriteria.SortField.Should().Be(TodoSortField.DueDate);
        capturedCriteria.SortDirection.Should().Be(SortDirection.Asc);
        TodoCursorPayload cursor = TodoCursorCodec.Decode(page.NextCursor!);
        cursor.LastTodoId.Should().Be(page.Items[^1].Id);
        cursor.SortField.Should().Be("dueDate");
        cursor.Direction.Should().Be("asc");
    }

    [TestMethod]
    public async Task QueryForwardsEveryFilterToListReader()
    {
        TodoListCriteria? capturedCriteria = null;
        listReader.GetTodosAsync(
                Arg.Do<TodoListCriteria>(criteria => capturedCriteria = criteria),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TodoListItemDto>());
        GetTodosQuery query = new GetTodosQuery(
            TodoStatus.Completed,
            TodoPriority.High,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            TodoDependencyStatus.Unblocked,
            TodoListScope.Archived,
            TodoSortField.Priority,
            SortDirection.Desc,
            limit: 25);
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        _ = await handler.Handle(query, CancellationToken.None);

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Status.Should().Be(TodoStatus.Completed);
        capturedCriteria.Priority.Should().Be(TodoPriority.High);
        capturedCriteria.DueFrom.Should().Be(new DateOnly(2026, 8, 1));
        capturedCriteria.DueTo.Should().Be(new DateOnly(2026, 8, 31));
        capturedCriteria.DependencyStatus.Should().Be(TodoDependencyStatus.Unblocked);
        capturedCriteria.Scope.Should().Be(TodoListScope.Archived);
        capturedCriteria.SortField.Should().Be(TodoSortField.Priority);
        capturedCriteria.SortDirection.Should().Be(SortDirection.Desc);
        capturedCriteria.Limit.Should().Be(26);
    }

    [TestMethod]
    public async Task MalformedCursorIsRejectedBeforeDatabaseAccess()
    {
        GetTodosQuery query = new GetTodosQuery(cursor: "not+a+base64url+cursor");
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        Func<Task> act = async () =>
            await handler.Handle(query, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidCursorException>()
            .WithMessage("The cursor is malformed or unsupported.");
        await listReader.DidNotReceiveWithAnyArgs()
            .GetTodosAsync(default!, default);
    }

    [TestMethod]
    public async Task CursorReusedWithDifferentFilterIsRejected()
    {
        GetTodosQuery firstQuery = new GetTodosQuery(priority: TodoPriority.High);
        string signature = TodoCursorCodec.CreateFilterSignature(firstQuery);
        string cursor = TodoCursorCodec.Encode(
            TodoCursorCodec.Create(firstQuery, CreateItem(1), signature));
        GetTodosQuery changedQuery = new GetTodosQuery(
            priority: TodoPriority.Low,
            cursor: cursor);
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        Func<Task> act = async () =>
            await handler.Handle(changedQuery, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidCursorException>()
            .WithMessage("The cursor does not match the current filters, scope, or sorting.");
    }

    private static TodoListItemDto CreateItem(int index)
    {
        return new TodoListItemDto(
            TestTodoFactory.CreateId($"todo-{index:D3}"),
            $"TODO {index:D3}",
            null,
            new DateOnly(2026, 8, 1).AddDays(index / 3),
            TodoStatus.NotStarted,
            TodoPriority.Medium,
            isRecurring: false,
            isBlocked: false,
            incompleteDependencyCount: 0,
            version: 1,
            deletedAt: null,
            purgeAt: null);
    }
}
