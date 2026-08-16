using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.DTOs;

public sealed record TodoListItemDto(
    Guid Id,
    string Name,
    string? DescriptionPreview,
    DateOnly DueDate,
    TodoStatus Status,
    TodoPriority Priority,
    bool IsRecurring,
    bool IsBlocked,
    int IncompleteDependencyCount,
    long Version,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? PurgeAt);
