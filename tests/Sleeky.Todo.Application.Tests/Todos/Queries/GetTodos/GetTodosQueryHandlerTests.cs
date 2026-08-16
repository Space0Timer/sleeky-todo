using System.Globalization;

using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Services;

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
        string cursor = CreateCursorFor(firstQuery);
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

    [TestMethod]
    public async Task SearchTextIsTokenizedIntoCriteriaTerms()
    {
        TodoListCriteria? capturedCriteria = null;
        listReader.GetTodosAsync(
                Arg.Do<TodoListCriteria>(criteria => capturedCriteria = criteria),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TodoListItemDto>());
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        _ = await handler.Handle(
            new GetTodosQuery(searchText: "  Buy MILK, please "),
            CancellationToken.None);

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.SearchTerms.Should().Equal("buy", "milk", "please");
    }

    /// <summary>
    /// Text that tokenizes to nothing is not a search for the empty string. It
    /// filters nothing, so the list reads exactly as it would with the box
    /// empty rather than coming back empty.
    /// </summary>
    [TestMethod]
    public async Task PunctuationOnlySearchTextFiltersNothing()
    {
        TodoListCriteria? capturedCriteria = null;
        listReader.GetTodosAsync(
                Arg.Do<TodoListCriteria>(criteria => capturedCriteria = criteria),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TodoListItemDto>());
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        _ = await handler.Handle(
            new GetTodosQuery(searchText: "--- !!!"),
            CancellationToken.None);

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.SearchTerms.Should().BeEmpty();
    }

    [TestMethod]
    public async Task NoSearchTextProducesNoTerms()
    {
        TodoListCriteria? capturedCriteria = null;
        listReader.GetTodosAsync(
                Arg.Do<TodoListCriteria>(criteria => capturedCriteria = criteria),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TodoListItemDto>());
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        _ = await handler.Handle(new GetTodosQuery(), CancellationToken.None);

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.SearchTerms.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CursorFromOneSearchIsRejectedUnderAnother()
    {
        string cursor = CreateCursorFor(new GetTodosQuery(searchText: "milk"));
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        Func<Task> act = async () => await handler.Handle(
            new GetTodosQuery(searchText: "bread", cursor: cursor),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidCursorException>()
            .WithMessage("The cursor does not match the current filters, scope, or sorting.");
    }

    [TestMethod]
    public async Task CursorFromASearchIsRejectedWithNoSearch()
    {
        string cursor = CreateCursorFor(new GetTodosQuery(searchText: "milk"));
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        Func<Task> act = async () => await handler.Handle(
            new GetTodosQuery(cursor: cursor),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidCursorException>()
            .WithMessage("The cursor does not match the current filters, scope, or sorting.");
    }

    /// <summary>
    /// Cosmetic differences in what was typed reach the same tokens, so a
    /// cursor minted under one spelling continues under the other rather than
    /// failing on a keystroke the user cannot see.
    /// </summary>
    [TestMethod]
    public async Task CursorSurvivesCosmeticDifferencesInTheSearchText()
    {
        TodoListItemDto[] storedItems = Enumerable.Range(1, 3)
            .Select(index => CreateItem(index))
            .ToArray();
        listReader.GetTodosAsync(Arg.Any<TodoListCriteria>(), Arg.Any<CancellationToken>())
            .Returns(storedItems);
        string cursor = CreateCursorFor(new GetTodosQuery(searchText: "Buy Milk"));
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        CursorPage<TodoListItemDto> page = await handler.Handle(
            new GetTodosQuery(searchText: "  buy   milk!  ", cursor: cursor, limit: 2),
            CancellationToken.None);

        page.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// A cursor minted before search existed carries a signature over six
    /// components. Appending a seventh only when terms exist is what keeps that
    /// cursor usable across the deployment that introduced this.
    /// </summary>
    [TestMethod]
    public void SignatureOfAnUnsearchedQueryIsUnchangedByTheSearchComponent()
    {
        GetTodosQuery query = new GetTodosQuery(priority: TodoPriority.High);

        TodoCursorCodec.CreateFilterSignature(query, Array.Empty<string>())
            .Should().Be(TodoCursorCodec.CreateFilterSignature(
                new GetTodosQuery(priority: TodoPriority.High, searchText: "   "),
                Array.Empty<string>()));
    }

    /// <summary>
    /// Every ordinal the enum defines is a usable cursor position, and nothing
    /// outside it is.
    /// </summary>
    /// <remarks>
    /// The first half is the half that matters: validating against a hardcoded
    /// range rather than the enum means the day a status or priority is added,
    /// cursors carrying the new member are rejected as malformed and paging
    /// stops mid-list for anyone holding one. Enumerating the enum here keeps
    /// that from being a silent break.
    /// </remarks>
    [TestMethod]
    public async Task EveryDefinedStatusAndPriorityOrdinalIsAUsableCursorPosition()
    {
        listReader.GetTodosAsync(Arg.Any<TodoListCriteria>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TodoListItemDto>());
        GetTodosQueryHandler handler = new GetTodosQueryHandler(listReader);

        foreach (TodoStatus status in Enum.GetValues<TodoStatus>())
        {
            Func<Task> accepted = async () => await handler.Handle(
                BuildQuery(TodoSortField.Status, (int)status),
                CancellationToken.None);
            await accepted.Should().NotThrowAsync();
        }

        foreach (TodoPriority priority in Enum.GetValues<TodoPriority>())
        {
            Func<Task> accepted = async () => await handler.Handle(
                BuildQuery(TodoSortField.Priority, (int)priority),
                CancellationToken.None);
            await accepted.Should().NotThrowAsync();
        }

        Func<Task> undefinedStatus = async () => await handler.Handle(
            BuildQuery(TodoSortField.Status, Enum.GetValues<TodoStatus>().Length),
            CancellationToken.None);
        Func<Task> undefinedPriority = async () => await handler.Handle(
            BuildQuery(TodoSortField.Priority, Enum.GetValues<TodoPriority>().Length),
            CancellationToken.None);

        await undefinedStatus.Should().ThrowAsync<InvalidCursorException>();
        await undefinedPriority.Should().ThrowAsync<InvalidCursorException>();
    }

    private static GetTodosQuery BuildQuery(TodoSortField sortField, int sortValue)
    {
        GetTodosQuery query = new GetTodosQuery(sortField: sortField);
        TodoCursorPayload minted = TodoCursorCodec.Create(
            query,
            CreateItem(1),
            TodoCursorCodec.CreateFilterSignature(query, Array.Empty<string>()));
        string cursor = TodoCursorCodec.Encode(new TodoCursorPayload
        {
            Version = minted.Version,
            SortField = minted.SortField,
            Direction = minted.Direction,
            LastSortValue = sortValue.ToString(CultureInfo.InvariantCulture),
            LastTodoId = minted.LastTodoId,
            FilterSignature = minted.FilterSignature,
        });

        return new GetTodosQuery(sortField: sortField, cursor: cursor);
    }

    private static string CreateCursorFor(GetTodosQuery query)
    {
        IReadOnlyList<string> terms = SearchTokenizer.Tokenize(query.SearchText);

        return TodoCursorCodec.Encode(TodoCursorCodec.Create(
            query,
            CreateItem(1),
            TodoCursorCodec.CreateFilterSignature(query, terms)));
    }

    private static TodoListItemDto CreateItem(int index)
    {
        return new TodoListItemDto(
            TestTodoFactory.CreateId($"todo-{index:D3}"),
            $"TODO {index:D3}",
            null,
            new DateOnly(2026, 8, 1).AddDays(index / 3),
            TodoStatus.Open,
            TodoPriority.Medium,
            IsRecurring: false,
            IsBlocked: false,
            IncompleteDependencyCount: 0,
            Version: 1,
            DeletedAt: null,
            PurgeAt: null);
    }
}
