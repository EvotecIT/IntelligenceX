using System;
using IntelligenceX.Json;

namespace IntelligenceX.Treatment;

/// <summary>Best-effort structured projection shared by treatment providers; original response text is retained.</summary>
internal static class TreatmentResponseParser {
    internal static JsonValue? TryExtractJson(string? text) {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text!.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
            int firstNewLine = trimmed.IndexOf('\n');
            int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                trimmed = trimmed.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
        }
        try { return JsonLite.Parse(trimmed); }
        catch (FormatException) { return null; }
    }
}
