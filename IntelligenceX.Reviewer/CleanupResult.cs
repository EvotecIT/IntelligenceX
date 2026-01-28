using System;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed class CleanupResult {
    public bool NeedsCleanup { get; set; }
    public double Confidence { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Notes { get; set; }

    public static CleanupResult? TryParse(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }
        try {
            var value = JsonLite.Parse(json);
            var obj = value?.AsObject();
            if (obj is null) {
                return null;
            }
            var confidence = obj.GetDouble("confidence") ?? 0;
            return new CleanupResult {
                NeedsCleanup = obj.GetBoolean("needs_cleanup") || obj.GetBoolean("needsCleanup"),
                Confidence = CleanupSettings.ClampConfidence(confidence),
                Title = obj.GetString("title"),
                Body = obj.GetString("body"),
                Notes = obj.GetString("notes")
            };
        } catch {
            return null;
        }
    }

    private static string? ExtractJson(string text) {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
            var fenceEnd = trimmed.IndexOf('\n');
            if (fenceEnd > -1) {
                trimmed = trimmed.Substring(fenceEnd + 1);
            }
            var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence > -1) {
                trimmed = trimmed.Substring(0, endFence);
            }
            trimmed = trimmed.Trim();
        }
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal)) {
            return null;
        }
        return trimmed;
    }
}
