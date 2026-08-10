using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// Selects how a Realtime transcription model accepts language and accuracy hints.
/// </summary>
public enum OpenAIRealtimeTranscriptionContextStyle {
    /// <summary>Uses plural <c>languages</c> hints.</summary>
    Contextual,
    /// <summary>Uses one singular <c>language</c> hint.</summary>
    Legacy
}

/// <summary>
/// Trades partial-transcript latency for additional audio context.
/// </summary>
public enum OpenAIRealtimeTranscriptionDelay {
    /// <summary>Emit partial text as early as possible.</summary>
    Minimal,
    /// <summary>Favor low latency.</summary>
    Low,
    /// <summary>Balance latency and accuracy.</summary>
    Medium,
    /// <summary>Favor additional context and accuracy.</summary>
    High,
    /// <summary>Allow the most delay for the most audio context.</summary>
    ExtraHigh
}

/// <summary>
/// Current model identifiers supported for Realtime input transcription.
/// </summary>
public static class OpenAIRealtimeTranscriptionModels {
    /// <summary>Recommended model for live microphone transcription.</summary>
    public const string GptLiveTranscribe = "gpt-live-transcribe";
    /// <summary>High-accuracy model for committed WebSocket turns.</summary>
    public const string GptTranscribe = "gpt-transcribe";
    /// <summary>Legacy GPT-4o transcription model.</summary>
    public const string Gpt4oTranscribe = "gpt-4o-transcribe";
    /// <summary>Lower-cost legacy GPT-4o mini transcription model.</summary>
    public const string Gpt4oMiniTranscribe = "gpt-4o-mini-transcribe";
    /// <summary>December 2025 snapshot of the GPT-4o mini transcription model.</summary>
    public const string Gpt4oMiniTranscribe2025_12_15 = "gpt-4o-mini-transcribe-2025-12-15";
    /// <summary>Legacy streaming transcription model.</summary>
    public const string GptRealtimeWhisper = "gpt-realtime-whisper";
    /// <summary>Original Whisper transcription model where Realtime supports it.</summary>
    public const string Whisper1 = "whisper-1";

    internal static OpenAIRealtimeTranscriptionContextStyle ResolveContextStyle(string model) {
        return string.Equals(model, Gpt4oTranscribe, StringComparison.Ordinal) ||
               string.Equals(model, Gpt4oMiniTranscribe, StringComparison.Ordinal) ||
               string.Equals(model, Gpt4oMiniTranscribe2025_12_15, StringComparison.Ordinal) ||
               string.Equals(model, GptRealtimeWhisper, StringComparison.Ordinal) ||
               string.Equals(model, Whisper1, StringComparison.Ordinal)
            ? OpenAIRealtimeTranscriptionContextStyle.Legacy
            : OpenAIRealtimeTranscriptionContextStyle.Contextual;
    }

    internal static bool ResolvePromptSupport(string model) {
        return !string.Equals(model, GptRealtimeWhisper, StringComparison.Ordinal);
    }

    internal static bool ResolveKeywordSupport(string model) {
        return string.Equals(model, GptLiveTranscribe, StringComparison.Ordinal) ||
               string.Equals(model, GptTranscribe, StringComparison.Ordinal);
    }

    internal static bool ResolveDelaySupport(string model) {
        return string.Equals(model, GptLiveTranscribe, StringComparison.Ordinal) ||
               string.Equals(model, GptRealtimeWhisper, StringComparison.Ordinal);
    }
}

/// <summary>
/// Configures transcription of microphone input in a Realtime session.
/// </summary>
public sealed class OpenAIRealtimeTranscriptionOptions {
    /// <summary>Gets or sets the transcription model identifier.</summary>
    public string Model { get; set; } = OpenAIRealtimeTranscriptionModels.GptLiveTranscribe;

    /// <summary>
    /// Gets or sets an optional schema override for custom or snapshot model identifiers.
    /// Known model identifiers are resolved automatically when this value is null.
    /// </summary>
    public OpenAIRealtimeTranscriptionContextStyle? ContextStyle { get; set; }

    /// <summary>Overrides prompt support for a custom or snapshot model identifier.</summary>
    public bool? SupportsPrompt { get; set; }

    /// <summary>Overrides keyword support for a custom or snapshot model identifier.</summary>
    public bool? SupportsKeywords { get; set; }

    /// <summary>Overrides delay support for a custom or snapshot model identifier.</summary>
    public bool? SupportsDelay { get; set; }

    /// <summary>Gets or sets free-form context about the audio.</summary>
    public string? Prompt { get; set; }

    /// <summary>Gets or sets expected input languages.</summary>
    public string[] LanguageHints { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets literal terms that may appear in the audio.</summary>
    public string[] Keywords { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the live transcription latency/accuracy preference.</summary>
    public OpenAIRealtimeTranscriptionDelay? Delay { get; set; }

    internal void Validate() {
        Model = NormalizeRequired(Model, nameof(Model));
        Prompt = NormalizeOptional(Prompt);
        LanguageHints = NormalizeItems(LanguageHints, nameof(LanguageHints));
        Keywords = NormalizeItems(Keywords, nameof(Keywords));

        for (var i = 0; i < Keywords.Length; i++) {
            if (Keywords[i].IndexOfAny(new[] { '<', '>', '\r', '\n' }) >= 0) {
                throw new ArgumentException(
                    "Transcription keywords cannot contain angle brackets or line breaks.",
                    nameof(Keywords));
            }
        }

        if (!ResolvedPromptSupport && Prompt is not null) {
            throw new ArgumentException(
                "The selected transcription model does not accept a prompt.",
                nameof(Prompt));
        }

        if (!ResolvedKeywordSupport && Keywords.Length > 0) {
            throw new ArgumentException(
                "The selected transcription model does not accept keywords.",
                nameof(Keywords));
        }

        if (!ResolvedDelaySupport && Delay.HasValue) {
            throw new ArgumentException(
                "The selected transcription model does not accept delay.",
                nameof(Delay));
        }

        if (ResolvedContextStyle == OpenAIRealtimeTranscriptionContextStyle.Legacy) {
            if (LanguageHints.Length > 1) {
                throw new ArgumentException(
                    "Legacy transcription models accept at most one language hint.",
                    nameof(LanguageHints));
            }
        }
    }

    internal JsonObject ToJson() {
        var payload = new JsonObject().Add("model", Model);
        if (ResolvedPromptSupport && !string.IsNullOrWhiteSpace(Prompt)) {
            payload.Add("prompt", Prompt);
        }

        if (ResolvedContextStyle == OpenAIRealtimeTranscriptionContextStyle.Contextual) {
            if (LanguageHints.Length > 0) {
                payload.Add("languages", ToJsonArray(LanguageHints));
            }
        } else if (LanguageHints.Length == 1) {
            payload.Add("language", LanguageHints[0]);
        }
        if (ResolvedKeywordSupport && Keywords.Length > 0) {
            payload.Add("keywords", ToJsonArray(Keywords));
        }
        if (ResolvedDelaySupport && Delay.HasValue) {
            payload.Add("delay", DelayToWireValue(Delay.Value));
        }
        return payload;
    }

    private OpenAIRealtimeTranscriptionContextStyle ResolvedContextStyle =>
        ContextStyle ?? OpenAIRealtimeTranscriptionModels.ResolveContextStyle(Model);

    private bool ResolvedPromptSupport =>
        SupportsPrompt ?? OpenAIRealtimeTranscriptionModels.ResolvePromptSupport(Model);

    private bool ResolvedKeywordSupport =>
        SupportsKeywords ?? OpenAIRealtimeTranscriptionModels.ResolveKeywordSupport(Model);

    private bool ResolvedDelaySupport =>
        SupportsDelay ?? OpenAIRealtimeTranscriptionModels.ResolveDelaySupport(Model);

    private static JsonArray ToJsonArray(string[] values) {
        var result = new JsonArray();
        for (var i = 0; i < values.Length; i++) {
            result.Add(values[i]);
        }
        return result;
    }

    private static string DelayToWireValue(OpenAIRealtimeTranscriptionDelay delay) {
        return delay switch {
            OpenAIRealtimeTranscriptionDelay.Minimal => "minimal",
            OpenAIRealtimeTranscriptionDelay.Low => "low",
            OpenAIRealtimeTranscriptionDelay.Medium => "medium",
            OpenAIRealtimeTranscriptionDelay.High => "high",
            OpenAIRealtimeTranscriptionDelay.ExtraHigh => "xhigh",
            _ => throw new ArgumentOutOfRangeException(nameof(delay))
        };
    }

    private static string[] NormalizeItems(string[]? values, string propertyName) {
        if (values is null) {
            throw new ArgumentNullException(propertyName);
        }
        var normalized = new List<string>(values.Length);
        for (var i = 0; i < values.Length; i++) {
            var value = values[i]?.Trim();
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(propertyName + " cannot contain empty values.", propertyName);
            }
            normalized.Add(value!);
        }
        return normalized.ToArray();
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
