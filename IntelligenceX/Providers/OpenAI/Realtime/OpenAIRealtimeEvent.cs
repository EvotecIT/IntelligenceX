using System;
using System.Text.Json;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// Represents one server event received from an OpenAI Realtime WebSocket.
/// </summary>
public sealed class OpenAIRealtimeEvent {
    private OpenAIRealtimeEvent(
        string type,
        string rawJson,
        string? textDelta,
        string? audioDelta,
        string? transcript,
        string? itemId,
        string[] transcriptionLanguages,
        string? errorMessage) {
        Type = type;
        RawJson = rawJson;
        TextDelta = textDelta;
        AudioDelta = audioDelta;
        Transcript = transcript;
        ItemId = itemId;
        TranscriptionLanguages = transcriptionLanguages;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the Realtime event type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the unmodified event JSON for fields not projected by this type.
    /// </summary>
    public string RawJson { get; }

    /// <summary>
    /// Gets a streamed text delta when the event contains one.
    /// </summary>
    public string? TextDelta { get; }

    /// <summary>
    /// Gets a base64-encoded streamed audio delta when the event contains one.
    /// </summary>
    public string? AudioDelta { get; }

    /// <summary>Gets the final transcript carried by a completion event.</summary>
    public string? Transcript { get; }

    /// <summary>Gets the input item identifier associated with the event.</summary>
    public string? ItemId { get; }

    /// <summary>Gets languages detected by a completed <c>gpt-transcribe</c> turn.</summary>
    public string[] TranscriptionLanguages { get; }

    /// <summary>
    /// Gets the server error message when the event represents an error.
    /// </summary>
    public string? ErrorMessage { get; }

    internal static OpenAIRealtimeEvent Parse(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            throw new ArgumentException("Realtime event JSON cannot be empty.", nameof(json));
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new FormatException("Realtime event JSON must be an object.");
        }

        var type = ReadString(root, "type") ?? "unknown";
        var delta = ReadString(root, "delta");
        string? textDelta = null;
        string? audioDelta = null;

        if (type.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("transcript", StringComparison.OrdinalIgnoreCase) >= 0) {
            textDelta = delta;
        } else if (type.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0) {
            audioDelta = delta;
        }

        string? errorMessage = null;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) {
            errorMessage = ReadString(error, "message");
        }
        errorMessage ??= ReadString(root, "message");

        return new OpenAIRealtimeEvent(
            type,
            json,
            textDelta,
            audioDelta,
            ReadString(root, "transcript"),
            ReadString(root, "item_id"),
            ReadTranscriptionLanguages(root),
            errorMessage);
    }

    private static string[] ReadTranscriptionLanguages(JsonElement root) {
        if (!root.TryGetProperty("languages", out var languages) ||
            languages.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }
        var result = new System.Collections.Generic.List<string>();
        foreach (var language in languages.EnumerateArray()) {
            var code = language.ValueKind == JsonValueKind.String
                ? language.GetString()
                : language.ValueKind == JsonValueKind.Object
                    ? ReadString(language, "code")
                    : null;
            if (!string.IsNullOrWhiteSpace(code)) {
                result.Add(code!);
            }
        }
        return result.ToArray();
    }

    private static string? ReadString(JsonElement element, string propertyName) {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
