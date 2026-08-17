using System.Text.Json;

using Microsoft.Extensions.AI;

using Sleeky.Todo.Assistant.Tools;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Moves the conversation between the wire and the loop.
/// </summary>
public static class TranscriptCodec
{
    private const string IdProperty = "id";

    private const string VersionProperty = "version";

    /// <summary>
    /// Reads the transcript a client echoed back.
    /// </summary>
    /// <remarks>
    /// Unreadable content starts a fresh conversation rather than failing the
    /// turn. There is nothing to protect here — the turn has already been
    /// checked against the Space it names, and the assistant then runs with
    /// exactly the caller's rights in that Space and dispatches commands the
    /// caller can already send over HTTP — so a mangled transcript is a
    /// usability problem, not a security one.
    /// </remarks>
    public static List<ChatMessage> Read(JsonElement? transcript)
    {
        if (transcript is null || transcript.Value.ValueKind != JsonValueKind.Array)
        {
            return new List<ChatMessage>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ChatMessage>>(
                transcript.Value,
                AIJsonUtilities.DefaultOptions)
                ?? new List<ChatMessage>();
        }
        catch (JsonException)
        {
            return new List<ChatMessage>();
        }
        catch (NotSupportedException)
        {
            // Well-formed JSON the serializer will not map: a content
            // discriminator this version does not know, which is what a
            // transcript held over a deployment looks like. Without this arm a
            // stale transcript would fault every turn a client tried, and the
            // client has no way to know it should discard it.
            return new List<ChatMessage>();
        }
    }

    public static JsonElement Write(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return JsonSerializer.SerializeToElement(messages, AIJsonUtilities.DefaultOptions);
    }

    public static JsonElement Empty()
    {
        return JsonSerializer.SerializeToElement(Array.Empty<ChatMessage>(), AIJsonUtilities.DefaultOptions);
    }

    /// <summary>
    /// Recovers what the model has already read, so a write in this turn can
    /// still bind the version the actor last saw.
    /// </summary>
    /// <remarks>
    /// The server keeps no history, so without this a conversation that read
    /// its TODOs three turns ago could never write to them: the model would not
    /// re-read, because from its side the results are right there in context.
    ///
    /// The scan looks for any object carrying both an identifier and a version
    /// rather than parsing each tool's result shape. Every read this assistant
    /// performs produces that pair, and matching on the pair keeps the seed
    /// working when a result shape gains a field.
    /// </remarks>
    public static void SeedLedger(JsonElement? transcript, TodoVersionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (transcript is not null)
        {
            Scan(transcript.Value, ledger);
        }
    }

    private static void Scan(JsonElement element, TodoVersionLedger ledger)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                RecordIfVersioned(element, ledger);

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Scan(property.Value, ledger);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Scan(item, ledger);
                }

                break;

            case JsonValueKind.String:
                ScanEmbedded(element, ledger);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// A provider may carry a tool result as a JSON string rather than as
    /// structured content, which would otherwise hide every version behind one
    /// opaque value.
    /// </summary>
    private static void ScanEmbedded(JsonElement element, TodoVersionLedger ledger)
    {
        string? text = element.GetString();

        if (text is null
            || text.Length == 0
            || (text[0] != '{' && text[0] != '['))
        {
            return;
        }

        try
        {
            using JsonDocument embedded = JsonDocument.Parse(text);
            Scan(embedded.RootElement, ledger);
        }
        catch (JsonException)
        {
            // Text that merely looks like JSON.
        }
    }

    /// <summary>
    /// Any object carrying a readable identifier and version is a TODO the
    /// model has seen. Anything else — a different pair of properties, or a
    /// version that is not a number — is passed over rather than guessed at.
    /// </summary>
    private static void RecordIfVersioned(JsonElement element, TodoVersionLedger ledger)
    {
        if (!element.TryGetProperty(IdProperty, out JsonElement id)
            || !element.TryGetProperty(VersionProperty, out JsonElement version))
        {
            return;
        }

        if (id.ValueKind == JsonValueKind.String
            && version.ValueKind == JsonValueKind.Number
            && Guid.TryParse(id.GetString(), out Guid parsedId)
            && version.TryGetInt64(out long parsedVersion))
        {
            ledger.Record(parsedId, parsedVersion);
        }
    }
}
