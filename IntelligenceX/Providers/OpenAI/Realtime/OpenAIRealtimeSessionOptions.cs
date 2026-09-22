using System;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// Describes a Realtime session and its short-lived client credential.
/// </summary>
public sealed class OpenAIRealtimeSessionOptions {
    /// <summary>
    /// Realtime model identifier.
    /// </summary>
    public string Model { get; set; } = OpenAIModelCatalog.DefaultRealtimeModel;

    /// <summary>
    /// Optional session instructions.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Output modalities requested from the model. Voice sessions normally use <c>audio</c>;
    /// text-only sessions can set this to <c>text</c>.
    /// </summary>
    public string[] OutputModalities { get; set; } = new[] { "audio" };

    /// <summary>
    /// Optional Realtime voice name. When omitted, the service default is used.
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// Gets or sets input transcription. Leave null when the client does not
    /// need user-audio transcript events.
    /// </summary>
    public OpenAIRealtimeTranscriptionOptions? InputTranscription { get; set; }

    /// <summary>
    /// Lifetime requested for the short-lived client credential.
    /// </summary>
    public TimeSpan ClientSecretLifetime { get; set; } = TimeSpan.FromMinutes(2);

    internal void Validate() {
        Model = NormalizeRequired(Model, nameof(Model));
        Instructions = NormalizeOptional(Instructions);
        Voice = NormalizeOptional(Voice);
        InputTranscription?.Validate();

        if (OutputModalities is null || OutputModalities.Length != 1) {
            throw new ArgumentException(
                "OutputModalities must contain exactly one value: 'audio' or 'text'.",
                nameof(OutputModalities));
        }

        var modality = NormalizeRequired(OutputModalities[0], nameof(OutputModalities)).ToLowerInvariant();
        if (!string.Equals(modality, "audio", StringComparison.Ordinal) &&
            !string.Equals(modality, "text", StringComparison.Ordinal)) {
            throw new ArgumentException("Output modality must be 'audio' or 'text'.", nameof(OutputModalities));
        }
        OutputModalities = new[] { modality };

        if (ClientSecretLifetime.TotalSeconds < 10 || ClientSecretLifetime.TotalSeconds > 7200) {
            throw new ArgumentOutOfRangeException(
                nameof(ClientSecretLifetime),
                "ClientSecretLifetime must be between 10 seconds and 2 hours.");
        }
    }

    private static string NormalizeRequired(string? value, string propertyName) {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) {
            throw new ArgumentException(propertyName + " cannot be null or whitespace.", propertyName);
        }
        return normalized!;
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
