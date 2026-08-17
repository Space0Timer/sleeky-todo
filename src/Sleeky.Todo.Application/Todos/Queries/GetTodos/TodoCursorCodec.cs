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
    private const string DateFormat = "yyyy-MM-dd";
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

            if (payload is null || !IsComplete(payload))
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
    /// Binds a cursor to the Space and filters that produced it, so a page
    /// cannot be continued under a different question — or in a different
    /// Space.
    /// </summary>
    /// <remarks>
    /// The Space leads the canonical form. A cursor minted while listing one
    /// Space and replayed against another therefore fails the signature check
    /// and is refused as invalid, rather than resuming a page of the second
    /// Space from a position the first one produced.
    ///
    /// The search component is appended only when terms exist, so an
    /// unsearched query's canonical form does not vary with the search code
    /// path. Terms are letters and digits only, so joining them with the same
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

        string canonical = BuildCanonicalFilterForm(query, searchTerms);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return ToBase64Url(hash);
    }

    public static void ValidateForQuery(
        TodoCursorPayload payload,
        GetTodosQuery query,
        string filterSignature)
    {
        if (MatchesQuery(payload, query, filterSignature))
        {
            return;
        }

        throw new InvalidCursorException(
            "The cursor does not match the current filters, scope, or sorting.");
    }

    /// <summary>
    /// A cursor resumes a query only when its sort, direction, and filter
    /// signature are the query's own and its sort value parses for that field.
    /// </summary>
    private static bool MatchesQuery(
        TodoCursorPayload payload,
        GetTodosQuery query,
        string filterSignature)
    {
        return string.Equals(
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
    }

    /// <summary>
    /// Every field a resumed query reads has to be present; a partial payload
    /// would decode cleanly and then fail somewhere less explicable.
    /// </summary>
    private static bool IsComplete(TodoCursorPayload payload)
    {
        return payload.Version == CurrentVersion
            && !string.IsNullOrWhiteSpace(payload.SortField)
            && !string.IsNullOrWhiteSpace(payload.Direction)
            && payload.LastSortValue is not null
            && payload.LastTodoId != Guid.Empty
            && !string.IsNullOrWhiteSpace(payload.FilterSignature);
    }

    /// <summary>
    /// The Space and the filter fields in a fixed order and format, so two
    /// equal queries always hash the same. Absent filters contribute an empty
    /// segment.
    /// </summary>
    private static string BuildCanonicalFilterForm(
        GetTodosQuery query,
        IReadOnlyList<string> searchTerms)
    {
        string canonical = string.Join(
            '|',
            query.SpaceId.ToString("D", CultureInfo.InvariantCulture),
            query.Status?.ToString() ?? string.Empty,
            query.Priority?.ToString() ?? string.Empty,
            query.DueFrom?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
            query.DueTo?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
            query.DependencyStatus?.ToString() ?? string.Empty,
            query.Scope.ToString());

        if (searchTerms.Count == 0)
        {
            return canonical;
        }

        return string.Concat(canonical, "|", string.Join('|', searchTerms));
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
                DateFormat,
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
                DateFormat,
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
