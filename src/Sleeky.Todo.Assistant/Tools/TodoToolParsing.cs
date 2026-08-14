using System.Diagnostics.CodeAnalysis;

namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// Turns what a model sent into what a command needs.
/// </summary>
/// <remarks>
/// Tool parameters are plain strings and integers rather than identifiers,
/// enums, and dates. Providers disagree about which JSON Schema formats they
/// will emit, and a small model gets them wrong more often, so the boundary
/// accepts text and reports precisely what was unreadable — which is a tool
/// error the model can act on rather than a schema rejection it cannot see.
/// </remarks>
public static class TodoToolParsing
{
    public static bool TryParseIds(
        string[]? ids,
        [NotNullWhen(true)] out Guid[]? parsed,
        [NotNullWhen(false)] out string? error)
    {
        parsed = null;

        if (ids is null || ids.Length == 0)
        {
            error = "At least one TODO identifier is required.";
            return false;
        }

        Guid[] result = new Guid[ids.Length];

        for (int index = 0; index < ids.Length; index++)
        {
            if (!Guid.TryParse(ids[index], out Guid id) || id == Guid.Empty)
            {
                error = $"'{ids[index]}' is not a TODO identifier. "
                    + "Use the id returned by a read.";
                return false;
            }

            result[index] = id;
        }

        if (result.Distinct().Count() != result.Length)
        {
            error = "A TODO can only appear once in a batch.";
            return false;
        }

        parsed = result;
        error = null;
        return true;
    }

    public static bool TryParseEnum<TEnum>(
        string? value,
        string parameterName,
        out TEnum parsed,
        [NotNullWhen(false)] out string? error)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed))
        {
            error = null;
            return true;
        }

        error = $"'{value}' is not a valid {parameterName}. Use one of: "
            + string.Join(", ", Enum.GetNames<TEnum>())
            + ".";
        return false;
    }

    public static bool TryParseDate(
        string? value,
        string parameterName,
        out DateOnly parsed,
        [NotNullWhen(false)] out string? error)
    {
        if (DateOnly.TryParse(value, out parsed))
        {
            error = null;
            return true;
        }

        error = $"'{value}' is not a valid {parameterName}. Use an ISO date, "
            + "such as 2026-08-14.";
        return false;
    }
}
