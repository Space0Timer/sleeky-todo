using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public static class TodoCursorCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumEncodedLength = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Encode(TodoCursorPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return ToBase64Url(json);
    }

    public static TodoCursorPayload Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumEncodedLength)
        {
            throw InvalidCursor();
        }

        try
        {
            byte[] json = FromBase64Url(cursor);
            TodoCursorPayload? payload = JsonSerializer.Deserialize<TodoCursorPayload>(
                json,
                JsonOptions);

            if (payload is null
                || payload.Version != CurrentVersion
                || string.IsNullOrWhiteSpace(payload.SortField)
                || string.IsNullOrWhiteSpace(payload.Direction)
                || payload.LastSortValue is null
                || payload.LastTodoId == Guid.Empty
                || string.IsNullOrWhiteSpace(payload.FilterSignature))
            {
                throw InvalidCursor();
            }

            return payload;
        }
        catch (InvalidCursorException)
        {
            throw;
        }
        catch (FormatException exception)
        {
            throw InvalidCursor(exception);
        }
        catch (JsonException exception)
        {
            throw InvalidCursor(exception);
        }
        catch (ArgumentException exception)
        {
            throw InvalidCursor(exception);
        }
    }

    public static TodoCursorPayload Create(
        GetTodosQuery query,
        TodoListItemDto lastItem,
        string filterSignature)
    {
        return new TodoCursorPayload
        {
            Version = CurrentVersion,
            SortField = GetSortFieldName(query.SortField),
            Direction = GetDirectionName(query.SortDirection),
            LastSortValue = GetSortValue(lastItem, query.SortField),
            LastTodoId = lastItem.Id,
            FilterSignature = filterSignature,
        };
    }

    /// <summary>
    /// Binds a cursor to the filters that produced it, so a page cannot be
    /// continued under a different question.
    /// </summary>
    /// <remarks>
    /// The search component is appended only when terms exist, which keeps the
    /// canonical form of every unsearched query byte-identical to what earlier
    /// versions produced: cursors already in flight survive a deployment.
    /// Terms are letters and digits only, so joining them with the same
    /// separator cannot be read two ways.
    ///
    /// Two orderings of the same words hash differently despite selecting the
    /// same TODOs. That is accepted rather than sorted away: reordering a search
    /// is a filter change, and the client already refetches from the first page
    /// whenever a filter changes.
    /// </remarks>
    public static string CreateFilterSignature(
        GetTodosQuery query,
        IReadOnlyList<string> searchTerms)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(searchTerms);

        string canonical = string.Join(
            '|',
            query.Status?.ToString() ?? string.Empty,
            query.Priority?.ToString() ?? string.Empty,
            query.DueFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            query.DueTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            query.DependencyStatus?.ToString() ?? string.Empty,
            query.Scope.ToString());

        if (searchTerms.Count > 0)
        {
            canonical = string.Concat(canonical, "|", string.Join('|', searchTerms));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return ToBase64Url(hash);
    }

    public static void ValidateForQuery(
        TodoCursorPayload payload,
        GetTodosQuery query,
        string filterSignature)
    {
        bool matches = string.Equals(
                payload.SortField,
                GetSortFieldName(query.SortField),
                StringComparison.Ordinal)
            && string.Equals(
                payload.Direction,
                GetDirectionName(query.SortDirection),
                StringComparison.Ordinal)
            && string.Equals(
                payload.FilterSignature,
                filterSignature,
                StringComparison.Ordinal)
            && IsValidSortValue(payload.LastSortValue, query.SortField);

        if (!matches)
        {
            throw new InvalidCursorException(
                "The cursor does not match the current filters, scope, or sorting.");
        }
    }

    private static string GetSortFieldName(TodoSortField sortField)
    {
        return sortField switch
        {
            TodoSortField.DueDate => "dueDate",
            TodoSortField.Priority => "priority",
            TodoSortField.Status => "status",
            TodoSortField.Name => "nameNormalized",
            _ => throw new ArgumentOutOfRangeException(nameof(sortField)),
        };
    }

    private static string GetDirectionName(SortDirection direction)
    {
        return direction switch
        {
            SortDirection.Asc => "asc",
            SortDirection.Desc => "desc",
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }

    private static string GetSortValue(TodoListItemDto item, TodoSortField sortField)
    {
        return sortField switch
        {
            TodoSortField.DueDate => item.DueDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            TodoSortField.Priority => GetNumericPriority(item.Priority)
                .ToString(CultureInfo.InvariantCulture),
            TodoSortField.Status => GetNumericStatus(item.Status)
                .ToString(CultureInfo.InvariantCulture),
            TodoSortField.Name => item.Name.ToLowerInvariant(),
            _ => throw new ArgumentOutOfRangeException(nameof(sortField)),
        };
    }

    private static int GetNumericPriority(TodoPriority priority)
    {
        if (Enum.IsDefined(priority))
        {
            return (int)priority;
        }

        throw new ArgumentOutOfRangeException(nameof(priority));
    }

    private static int GetNumericStatus(TodoStatus status)
    {
        if (Enum.IsDefined(status))
        {
            return (int)status;
        }

        throw new ArgumentOutOfRangeException(nameof(status));
    }

    private static bool IsValidSortValue(string value, TodoSortField sortField)
    {
        return sortField switch
        {
            TodoSortField.DueDate => DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _),
            TodoSortField.Priority => int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int priorityOrder)
                && Enum.IsDefined((TodoPriority)priorityOrder),
            TodoSortField.Status => int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int statusOrder)
                && Enum.IsDefined((TodoStatus)statusOrder),
            TodoSortField.Name => !string.IsNullOrEmpty(value),
            _ => false,
        };
    }

    private static byte[] FromBase64Url(string value)
    {
        if (value.Any(character =>
            !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new FormatException("The value is not Base64URL encoded.");
        }

        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("The value is not valid Base64URL."),
        };
        return Convert.FromBase64String(base64);
    }

    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static InvalidCursorException InvalidCursor()
    {
        return new InvalidCursorException("The cursor is malformed or unsupported.");
    }

    private static InvalidCursorException InvalidCursor(Exception exception)
    {
        return new InvalidCursorException(
            "The cursor is malformed or unsupported.",
            exception);
    }
}
