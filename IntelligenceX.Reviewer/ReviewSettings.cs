using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Reviewer;

internal enum ReviewLength {
    Short,
    Long
}

internal sealed class ReviewSettings {
    public string Mode { get; set; } = "hybrid";
    public string? Persona { get; set; }
    public string? Notes { get; set; }
    public string Model { get; set; } = "gpt-5.1-codex";
    public ReviewLength Length { get; set; } = ReviewLength.Long;
    public bool IncludeNextSteps { get; set; } = true;
    public bool OverwriteSummary { get; set; } = true;
    public bool SkipDraft { get; set; } = true;
    public IReadOnlyList<string> SkipTitles { get; set; } = new[] { "[WIP]", "[skip-review]" };
    public IReadOnlyList<string> SkipLabels { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SkipPaths { get; set; } = Array.Empty<string>();
    public int MaxFiles { get; set; } = 20;
    public int MaxPatchChars { get; set; } = 4000;
    public int MaxInlineComments { get; set; } = 10;
    public string? SeverityThreshold { get; set; }
    public bool RedactPii { get; set; }
    public IReadOnlyList<string> RedactionPatterns { get; set; } = Array.Empty<string>();
    public string RedactionReplacement { get; set; } = "[REDACTED]";
    public int WaitSeconds { get; set; } = 60;
    public int IdleSeconds { get; set; } = 5;

    public string? CodexPath { get; set; }
    public string? CodexArgs { get; set; }
    public string? CodexWorkingDirectory { get; set; }

    public static ReviewSettings FromEnvironment() {
        var settings = new ReviewSettings {
            Mode = GetInput("mode", "REVIEW_MODE") ?? "hybrid",
            Persona = GetInput("persona", "REVIEW_PERSONA"),
            Notes = GetInput("notes", "REVIEW_NOTES"),
            Model = GetInput("model", "OPENAI_MODEL") ?? "gpt-5.1-codex",
            OverwriteSummary = ParseBoolean(GetInput("overwrite_summary", "OVERWRITE_SUMMARY"), true),
            SkipDraft = ParseBoolean(GetInput("skip_draft", "SKIP_DRAFT"), true),
            SkipTitles = ParseList(GetInput("skip_titles", "SKIP_TITLES"), new[] { "[WIP]", "[skip-review]" }),
            SkipLabels = ParseList(GetInput("skip_labels", "SKIP_LABELS")),
            SkipPaths = ParseList(GetInput("skip_paths", "SKIP_PATHS")),
            MaxFiles = ParsePositiveInt(GetInput("max_files", "OPENAI_MAX_FILES"), 20),
            MaxPatchChars = ParsePositiveInt(GetInput("max_patch_chars", "OPENAI_MAX_PATCH_CHARS"), 4000),
            MaxInlineComments = ParsePositiveInt(GetInput("max_inline_comments", "OPENAI_MAX_INLINE_COMMENTS"), 10),
            SeverityThreshold = NormalizeSeverity(GetInput("severity_threshold", "REVIEW_SEVERITY_THRESHOLD")),
            RedactPii = ParseBoolean(GetInput("redact_pii", "REVIEW_REDACT_PII"), false),
            RedactionPatterns = ParseList(GetInput("redaction_patterns", "REDACTION_PATTERNS")),
            RedactionReplacement = GetInput("redaction_replacement", "REDACTION_REPLACEMENT") ?? "[REDACTED]",
            WaitSeconds = ParsePositiveInt(GetInput("wait_seconds", "REVIEW_WAIT_SECONDS"), 60),
            IdleSeconds = ParsePositiveInt(GetInput("idle_seconds", "REVIEW_IDLE_SECONDS"), 5),
            CodexPath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH"),
            CodexArgs = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS"),
            CodexWorkingDirectory = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_CWD")
        };

        var length = GetInput("length", "REVIEW_LENGTH");
        settings.Length = string.Equals(length, "short", StringComparison.OrdinalIgnoreCase) ? ReviewLength.Short : ReviewLength.Long;

        var includeNextSteps = GetInput("include_next_steps", "REVIEW_INCLUDE_NEXT_STEPS");
        if (!string.IsNullOrWhiteSpace(includeNextSteps)) {
            settings.IncludeNextSteps = ParseBoolean(includeNextSteps, true);
        }

        return settings;
    }

    private static string? GetInput(string inputName, string? envName = null) {
        var value = Environment.GetEnvironmentVariable($"INPUT_{inputName.ToUpperInvariant()}");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value.Trim();
        }
        if (!string.IsNullOrWhiteSpace(envName)) {
            value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ParseList(string? value, IReadOnlyList<string>? fallback = null) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback ?? Array.Empty<string>();
        }
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? fallback ?? Array.Empty<string>() : parts;
    }

    private static bool ParseBoolean(string? value, bool fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => fallback
        };
    }

    private static int ParsePositiveInt(string? value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) {
            return parsed;
        }
        return fallback;
    }

    private static string? NormalizeSeverity(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "low" or "medium" or "high" or "critical" => normalized,
            _ => null
        };
    }
}
