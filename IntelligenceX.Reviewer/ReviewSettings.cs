using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Reviewer;

internal enum ReviewLength {
    Short,
    Medium,
    Long
}

internal enum ReviewCommentMode {
    Sticky,
    Fresh
}

internal enum ReviewProvider {
    OpenAI,
    Copilot
}

internal sealed class ReviewSettings {
    public string Mode { get; set; } = "hybrid";
    public ReviewProvider Provider { get; set; } = ReviewProvider.OpenAI;
    public string? Profile { get; set; }
    public string? Style { get; set; }
    public string? Strictness { get; set; }
    public string? Tone { get; set; }
    public IReadOnlyList<string> Focus { get; set; } = Array.Empty<string>();
    public string? Persona { get; set; }
    public string? Notes { get; set; }
    public string Model { get; set; } = "gpt-5.2-codex";
    public OpenAITransportKind OpenAITransport { get; set; } = OpenAITransportKind.AppServer;
    public ReviewLength Length { get; set; } = ReviewLength.Long;
    public bool IncludeNextSteps { get; set; } = true;
    public string? PromptTemplate { get; set; }
    public string? PromptTemplatePath { get; set; }
    public string? OutputStyle { get; set; }
    public string? SummaryTemplate { get; set; }
    public string? SummaryTemplatePath { get; set; }
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
    public bool ProgressUpdates { get; set; } = true;
    public int ProgressUpdateSeconds { get; set; } = 30;
    public int ProgressPreviewChars { get; set; } = 4000;
    public ReviewCommentMode CommentMode { get; set; } = ReviewCommentMode.Sticky;

    public string? CodexPath { get; set; }
    public string? CodexArgs { get; set; }
    public string? CodexWorkingDirectory { get; set; }

    public string? CopilotCliPath { get; set; }
    public string? CopilotCliUrl { get; set; }
    public string? CopilotWorkingDirectory { get; set; }
    public bool CopilotAutoInstall { get; set; }
    public string? CopilotAutoInstallMethod { get; set; }
    public bool CopilotAutoInstallPrerelease { get; set; }

    public static ReviewSettings Load() {
        var settings = new ReviewSettings();
        ReviewConfigLoader.Apply(settings);
        ApplyEnvironment(settings);
        return settings;
    }

    public static ReviewSettings FromEnvironment() {
        var settings = new ReviewSettings();
        ApplyEnvironment(settings);
        return settings;
    }

    internal static void ApplyEnvironment(ReviewSettings settings) {
        var profile = GetInput("profile", "REVIEW_PROFILE");
        if (!string.IsNullOrWhiteSpace(profile)) {
            ReviewProfiles.Apply(profile!, settings);
            settings.Profile = profile;
        }

        var provider = GetInput("provider", "REVIEW_PROVIDER");
        if (!string.IsNullOrWhiteSpace(provider)) {
            settings.Provider = ParseProvider(provider);
        }

        var mode = GetInput("mode", "REVIEW_MODE");
        if (!string.IsNullOrWhiteSpace(mode)) {
            settings.Mode = mode!;
        }

        var strictness = GetInput("strictness", "REVIEW_STRICTNESS");
        if (!string.IsNullOrWhiteSpace(strictness)) {
            settings.Strictness = strictness;
        }

        var style = GetInput("style", "REVIEW_STYLE");
        if (!string.IsNullOrWhiteSpace(style)) {
            ReviewStyles.Apply(style!, settings);
            settings.Style = style;
        }

        var tone = GetInput("tone", "REVIEW_TONE");
        if (!string.IsNullOrWhiteSpace(tone)) {
            settings.Tone = tone;
        }

        var focus = GetInput("focus", "REVIEW_FOCUS");
        if (!string.IsNullOrWhiteSpace(focus)) {
            settings.Focus = ParseList(focus);
        }

        var persona = GetInput("persona", "REVIEW_PERSONA");
        if (!string.IsNullOrWhiteSpace(persona)) {
            settings.Persona = persona;
        }

        var notes = GetInput("notes", "REVIEW_NOTES");
        if (!string.IsNullOrWhiteSpace(notes)) {
            settings.Notes = notes;
        }

        var model = GetInput("model", "OPENAI_MODEL");
        if (!string.IsNullOrWhiteSpace(model)) {
            settings.Model = model!;
        }

        var transport = GetInput("openai_transport", "OPENAI_TRANSPORT");
        if (!string.IsNullOrWhiteSpace(transport)) {
            settings.OpenAITransport = ParseTransport(transport);
        }

        var overwriteSummary = GetInput("overwrite_summary", "OVERWRITE_SUMMARY");
        if (!string.IsNullOrWhiteSpace(overwriteSummary)) {
            settings.OverwriteSummary = ParseBoolean(overwriteSummary, settings.OverwriteSummary);
        }

        var skipDraft = GetInput("skip_draft", "SKIP_DRAFT");
        if (!string.IsNullOrWhiteSpace(skipDraft)) {
            settings.SkipDraft = ParseBoolean(skipDraft, settings.SkipDraft);
        }

        var skipTitles = GetInput("skip_titles", "SKIP_TITLES");
        if (!string.IsNullOrWhiteSpace(skipTitles)) {
            settings.SkipTitles = ParseList(skipTitles, settings.SkipTitles);
        }

        var skipLabels = GetInput("skip_labels", "SKIP_LABELS");
        if (!string.IsNullOrWhiteSpace(skipLabels)) {
            settings.SkipLabels = ParseList(skipLabels, settings.SkipLabels);
        }

        var skipPaths = GetInput("skip_paths", "SKIP_PATHS");
        if (!string.IsNullOrWhiteSpace(skipPaths)) {
            settings.SkipPaths = ParseList(skipPaths, settings.SkipPaths);
        }

        var maxFiles = GetInput("max_files", "OPENAI_MAX_FILES");
        if (!string.IsNullOrWhiteSpace(maxFiles)) {
            settings.MaxFiles = ParsePositiveInt(maxFiles, settings.MaxFiles);
        }

        var maxPatchChars = GetInput("max_patch_chars", "OPENAI_MAX_PATCH_CHARS");
        if (!string.IsNullOrWhiteSpace(maxPatchChars)) {
            settings.MaxPatchChars = ParsePositiveInt(maxPatchChars, settings.MaxPatchChars);
        }

        var maxInlineComments = GetInput("max_inline_comments", "OPENAI_MAX_INLINE_COMMENTS");
        if (!string.IsNullOrWhiteSpace(maxInlineComments)) {
            settings.MaxInlineComments = ParsePositiveInt(maxInlineComments, settings.MaxInlineComments);
        }

        var severityThreshold = GetInput("severity_threshold", "REVIEW_SEVERITY_THRESHOLD");
        if (!string.IsNullOrWhiteSpace(severityThreshold)) {
            settings.SeverityThreshold = NormalizeSeverity(severityThreshold);
        }

        var redactPii = GetInput("redact_pii", "REVIEW_REDACT_PII");
        if (!string.IsNullOrWhiteSpace(redactPii)) {
            settings.RedactPii = ParseBoolean(redactPii, settings.RedactPii);
        }

        var redactionPatterns = GetInput("redaction_patterns", "REDACTION_PATTERNS");
        if (!string.IsNullOrWhiteSpace(redactionPatterns)) {
            settings.RedactionPatterns = ParseList(redactionPatterns);
        }

        var redactionReplacement = GetInput("redaction_replacement", "REDACTION_REPLACEMENT");
        if (!string.IsNullOrWhiteSpace(redactionReplacement)) {
            settings.RedactionReplacement = redactionReplacement!;
        }

        var promptTemplate = GetInput("prompt_template", "REVIEW_PROMPT_TEMPLATE");
        if (!string.IsNullOrWhiteSpace(promptTemplate)) {
            settings.PromptTemplate = promptTemplate;
        }

        var promptTemplatePath = GetInput("prompt_template_path", "REVIEW_PROMPT_TEMPLATE_PATH");
        if (!string.IsNullOrWhiteSpace(promptTemplatePath)) {
            settings.PromptTemplatePath = promptTemplatePath;
        }

        var outputStyle = GetInput("output_style", "REVIEW_OUTPUT_STYLE");
        if (!string.IsNullOrWhiteSpace(outputStyle)) {
            settings.OutputStyle = outputStyle;
        }

        var summaryTemplate = GetInput("summary_template", "REVIEW_SUMMARY_TEMPLATE");
        if (!string.IsNullOrWhiteSpace(summaryTemplate)) {
            settings.SummaryTemplate = summaryTemplate;
        }

        var summaryTemplatePath = GetInput("summary_template_path", "REVIEW_SUMMARY_TEMPLATE_PATH");
        if (!string.IsNullOrWhiteSpace(summaryTemplatePath)) {
            settings.SummaryTemplatePath = summaryTemplatePath;
        }

        var waitSeconds = GetInput("wait_seconds", "REVIEW_WAIT_SECONDS");
        if (!string.IsNullOrWhiteSpace(waitSeconds)) {
            settings.WaitSeconds = ParsePositiveInt(waitSeconds, settings.WaitSeconds);
        }

        var idleSeconds = GetInput("idle_seconds", "REVIEW_IDLE_SECONDS");
        if (!string.IsNullOrWhiteSpace(idleSeconds)) {
            settings.IdleSeconds = ParsePositiveInt(idleSeconds, settings.IdleSeconds);
        }

        var progressUpdates = GetInput("progress_updates", "REVIEW_PROGRESS_UPDATES");
        if (!string.IsNullOrWhiteSpace(progressUpdates)) {
            settings.ProgressUpdates = ParseBoolean(progressUpdates, settings.ProgressUpdates);
        }

        var progressUpdateSeconds = GetInput("progress_update_seconds", "REVIEW_PROGRESS_UPDATE_SECONDS");
        if (!string.IsNullOrWhiteSpace(progressUpdateSeconds)) {
            settings.ProgressUpdateSeconds = ParsePositiveInt(progressUpdateSeconds, settings.ProgressUpdateSeconds);
        }

        var progressPreviewChars = GetInput("progress_preview_chars", "REVIEW_PROGRESS_PREVIEW_CHARS");
        if (!string.IsNullOrWhiteSpace(progressPreviewChars)) {
            settings.ProgressPreviewChars = ParsePositiveInt(progressPreviewChars, settings.ProgressPreviewChars);
        }

        var codexPath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH");
        if (!string.IsNullOrWhiteSpace(codexPath)) {
            settings.CodexPath = codexPath;
        }

        var codexArgs = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS");
        if (!string.IsNullOrWhiteSpace(codexArgs)) {
            settings.CodexArgs = codexArgs;
        }

        var codexCwd = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_CWD");
        if (!string.IsNullOrWhiteSpace(codexCwd)) {
            settings.CodexWorkingDirectory = codexCwd;
        }

        var copilotCliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(copilotCliPath)) {
            settings.CopilotCliPath = copilotCliPath;
        }
        var copilotCliUrl = Environment.GetEnvironmentVariable("COPILOT_CLI_URL");
        if (!string.IsNullOrWhiteSpace(copilotCliUrl)) {
            settings.CopilotCliUrl = copilotCliUrl;
        }
        var copilotWorkingDirectory = Environment.GetEnvironmentVariable("COPILOT_CLI_WORKDIR");
        if (!string.IsNullOrWhiteSpace(copilotWorkingDirectory)) {
            settings.CopilotWorkingDirectory = copilotWorkingDirectory;
        }
        var copilotAutoInstall = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL");
        if (!string.IsNullOrWhiteSpace(copilotAutoInstall)) {
            settings.CopilotAutoInstall = ParseBoolean(copilotAutoInstall, settings.CopilotAutoInstall);
        }
        var copilotAutoInstallMethod = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL_METHOD");
        if (!string.IsNullOrWhiteSpace(copilotAutoInstallMethod)) {
            settings.CopilotAutoInstallMethod = copilotAutoInstallMethod;
        }
        var copilotAutoInstallPrerelease = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL_PRERELEASE");
        if (!string.IsNullOrWhiteSpace(copilotAutoInstallPrerelease)) {
            settings.CopilotAutoInstallPrerelease = ParseBoolean(copilotAutoInstallPrerelease, settings.CopilotAutoInstallPrerelease);
        }

        var length = GetInput("length", "REVIEW_LENGTH");
        if (!string.IsNullOrWhiteSpace(length)) {
            settings.Length = length.Trim().ToLowerInvariant() switch {
                "short" => ReviewLength.Short,
                "medium" => ReviewLength.Medium,
                _ => ReviewLength.Long
            };
        }

        var includeNextSteps = GetInput("include_next_steps", "REVIEW_INCLUDE_NEXT_STEPS");
        if (!string.IsNullOrWhiteSpace(includeNextSteps)) {
            settings.IncludeNextSteps = ParseBoolean(includeNextSteps, settings.IncludeNextSteps);
        }

        var commentMode = GetInput("comment_mode", "REVIEW_COMMENT_MODE");
        if (!string.IsNullOrWhiteSpace(commentMode)) {
            settings.CommentMode = commentMode.Trim().ToLowerInvariant() switch {
                "fresh" => ReviewCommentMode.Fresh,
                _ => ReviewCommentMode.Sticky
            };
        }
    }

    private static ReviewProvider ParseProvider(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "copilot" => ReviewProvider.Copilot,
            "openai" or "codex" => ReviewProvider.OpenAI,
            _ => ReviewProvider.OpenAI
        };
    }

    private static OpenAITransportKind ParseTransport(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => OpenAITransportKind.AppServer
        };
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
