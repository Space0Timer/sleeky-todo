using System.Text;

namespace Sleeky.Todo.Domain.Services;

/// <summary>
/// Splits text into the normalized tokens search stores and matches against.
/// </summary>
/// <remarks>
/// One implementation serves the write path, the query side, and the backfill,
/// so a stored token and a typed term are produced by the same rules rather
/// than by two definitions that have to be kept in agreement.
///
/// Casing follows <c>NameNormalized</c> and is invariant, because the
/// comparison is between two stored strings rather than between two people's
/// locales. Invariant lowercasing maps one character to one character, so a
/// character with no simple lowercase form is stored as it was written and is
/// found by typing it the same way. Diacritics are deliberately not folded,
/// which keeps this consistent with the normalized name the sort already uses.
/// </remarks>
public static class SearchTokenizer
{
    /// <summary>
    /// The longest stored token. A term beyond this is a pasted identifier or
    /// a run-on rather than a word, and truncating bounds the index entry
    /// without changing which documents a realistic term reaches.
    /// </summary>
    public const int MaximumTokenLength = 64;

    public static IReadOnlyList<string> Tokenize(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        List<string> tokens = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? value in values)
        {
            AddTokens(value, tokens, seen);
        }

        return tokens;
    }

    private static void AddTokens(string? value, List<string> tokens, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string lowered = value.ToLowerInvariant();
        StringBuilder token = new StringBuilder();

        foreach (char character in lowered)
        {
            if (char.IsLetterOrDigit(character))
            {
                _ = token.Append(character);
                continue;
            }

            Flush(token, tokens, seen);
        }

        Flush(token, tokens, seen);
    }

    private static void Flush(StringBuilder token, List<string> tokens, HashSet<string> seen)
    {
        if (token.Length == 0)
        {
            return;
        }

        string value = token.Length > MaximumTokenLength
            ? token.ToString(0, MaximumTokenLength)
            : token.ToString();
        _ = token.Clear();

        if (seen.Add(value))
        {
            tokens.Add(value);
        }
    }
}
