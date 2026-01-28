using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal static class ReviewConfigLoader {
    public static void Apply(ReviewSettings settings) {
        var path = ResolveConfigPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return;
        }

        var text = File.ReadAllText(path);
        var value = JsonLite.Parse(text);
        var root = value?.AsObject();
        if (root is null) {
            return;
        }

        var reviewObj = root.GetObject("review") ?? root;

        var profile = reviewObj.GetString("profile");
        if (!string.IsNullOrWhiteSpace(profile)) {
            ReviewProfiles.Apply(profile!, settings);
            settings.Profile = profile;
        }

        var provider = reviewObj.GetString("provider");
        if (!string.IsNullOrWhiteSpace(provider)) {
            settings.Provider = provider.Trim().ToLowerInvariant() switch {
                "copilot" => ReviewProvider.Copilot,
                "openai" or "codex" => ReviewProvider.OpenAI,
                _ => settings.Provider
            };
        }

        ApplyStrings(reviewObj, settings);
        ApplyLists(reviewObj, settings);
        ApplyNumbers(reviewObj, settings);
        ApplyBooleans(reviewObj, settings);
        ApplyCommentMode(reviewObj, settings);
        ApplyLength(reviewObj, settings);
        ApplyContext(reviewObj, settings);
        ApplyCodex(root, settings);
        ApplyCopilot(root, settings);
        ApplyCleanup(root, settings);
    }

    private static string? ResolveConfigPath() {
        var explicitPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath)) {
            return explicitPath;
        }

        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var baseDir = !string.IsNullOrWhiteSpace(workspace) ? workspace : Environment.CurrentDirectory;
        var candidate = Path.Combine(baseDir, ".intelligencex", "reviewer.json");
        return candidate;
    }

    private static void ApplyStrings(JsonObject obj, ReviewSettings settings) {
        settings.Mode = obj.GetString("mode") ?? settings.Mode;
        settings.Strictness = obj.GetString("strictness") ?? settings.Strictness;
        settings.Tone = obj.GetString("tone") ?? settings.Tone;
        settings.Persona = obj.GetString("persona") ?? settings.Persona;
        settings.Notes = obj.GetString("notes") ?? settings.Notes;
        settings.Model = obj.GetString("model") ?? settings.Model;
        settings.SeverityThreshold = obj.GetString("severityThreshold") ?? settings.SeverityThreshold;
        settings.RedactionReplacement = obj.GetString("redactionReplacement") ?? settings.RedactionReplacement;
        settings.PromptTemplate = obj.GetString("promptTemplate") ?? settings.PromptTemplate;
        settings.PromptTemplatePath = obj.GetString("promptTemplatePath") ?? settings.PromptTemplatePath;
        settings.SummaryTemplate = obj.GetString("summaryTemplate") ?? settings.SummaryTemplate;
        settings.SummaryTemplatePath = obj.GetString("summaryTemplatePath") ?? settings.SummaryTemplatePath;
    }

    private static void ApplyLists(JsonObject obj, ReviewSettings settings) {
        var focus = ReadStringList(obj, "focus");
        if (focus is not null) {
            settings.Focus = focus;
        }

        var skipTitles = ReadStringList(obj, "skipTitles");
        if (skipTitles is not null) {
            settings.SkipTitles = skipTitles;
        }

        var skipLabels = ReadStringList(obj, "skipLabels");
        if (skipLabels is not null) {
            settings.SkipLabels = skipLabels;
        }

        var skipPaths = ReadStringList(obj, "skipPaths");
        if (skipPaths is not null) {
            settings.SkipPaths = skipPaths;
        }

        var redactionPatterns = ReadStringList(obj, "redactionPatterns");
        if (redactionPatterns is not null) {
            settings.RedactionPatterns = redactionPatterns;
        }
    }

    private static void ApplyNumbers(JsonObject obj, ReviewSettings settings) {
        settings.MaxFiles = ReadInt(obj, "maxFiles", settings.MaxFiles);
        settings.MaxPatchChars = ReadInt(obj, "maxPatchChars", settings.MaxPatchChars);
        settings.MaxInlineComments = ReadInt(obj, "maxInlineComments", settings.MaxInlineComments);
        settings.WaitSeconds = ReadInt(obj, "waitSeconds", settings.WaitSeconds);
        settings.IdleSeconds = ReadInt(obj, "idleSeconds", settings.IdleSeconds);
        settings.ProgressUpdateSeconds = ReadInt(obj, "progressUpdateSeconds", settings.ProgressUpdateSeconds);
        settings.ProgressPreviewChars = ReadInt(obj, "progressPreviewChars", settings.ProgressPreviewChars);
    }

    private static void ApplyBooleans(JsonObject obj, ReviewSettings settings) {
        settings.IncludeNextSteps = ReadBool(obj, "includeNextSteps", settings.IncludeNextSteps);
        settings.OverwriteSummary = ReadBool(obj, "overwriteSummary", settings.OverwriteSummary);
        settings.SkipDraft = ReadBool(obj, "skipDraft", settings.SkipDraft);
        settings.RedactPii = ReadBool(obj, "redactPii", settings.RedactPii);
        settings.ProgressUpdates = ReadBool(obj, "progressUpdates", settings.ProgressUpdates);
    }

    private static void ApplyCommentMode(JsonObject obj, ReviewSettings settings) {
        var mode = obj.GetString("commentMode");
        if (string.IsNullOrWhiteSpace(mode)) {
            return;
        }
        settings.CommentMode = mode.Trim().ToLowerInvariant() switch {
            "fresh" => ReviewCommentMode.Fresh,
            _ => ReviewCommentMode.Sticky
        };
    }

    private static void ApplyLength(JsonObject obj, ReviewSettings settings) {
        var length = obj.GetString("length");
        if (string.IsNullOrWhiteSpace(length)) {
            return;
        }
        settings.Length = length.Trim().ToLowerInvariant() switch {
            "short" => ReviewLength.Short,
            "medium" => ReviewLength.Medium,
            _ => ReviewLength.Long
        };
    }

    private static void ApplyContext(JsonObject obj, ReviewSettings settings) {
        settings.IncludeIssueComments = ReadBool(obj, "includeIssueComments", settings.IncludeIssueComments);
        settings.IncludeReviewComments = ReadBool(obj, "includeReviewComments", settings.IncludeReviewComments);
        settings.MaxCommentChars = ReadInt(obj, "maxCommentChars", settings.MaxCommentChars);
        settings.MaxComments = ReadInt(obj, "maxComments", settings.MaxComments);
        settings.IncludeRelatedPrs = ReadBool(obj, "includeRelatedPrs", settings.IncludeRelatedPrs);
        settings.RelatedPrsQuery = obj.GetString("relatedPrsQuery") ?? settings.RelatedPrsQuery;
        settings.MaxRelatedPrs = ReadInt(obj, "maxRelatedPrs", settings.MaxRelatedPrs);
    }

    private static void ApplyCodex(JsonObject root, ReviewSettings settings) {
        var codex = root.GetObject("codex") ?? root.GetObject("appServer");
        if (codex is null) {
            return;
        }
        settings.CodexPath = codex.GetString("path") ?? settings.CodexPath;
        settings.CodexArgs = codex.GetString("args") ?? settings.CodexArgs;
        settings.CodexWorkingDirectory = codex.GetString("workingDirectory") ?? settings.CodexWorkingDirectory;
    }

    private static void ApplyCopilot(JsonObject root, ReviewSettings settings) {
        var copilot = root.GetObject("copilot");
        if (copilot is null) {
            return;
        }
        settings.CopilotCliPath = copilot.GetString("cliPath") ?? settings.CopilotCliPath;
        settings.CopilotCliUrl = copilot.GetString("cliUrl") ?? settings.CopilotCliUrl;
        settings.CopilotWorkingDirectory = copilot.GetString("workingDirectory") ?? settings.CopilotWorkingDirectory;
        settings.CopilotAutoInstall = ReadBool(copilot, "autoInstall", settings.CopilotAutoInstall);
        settings.CopilotAutoInstallMethod = copilot.GetString("autoInstallMethod") ?? settings.CopilotAutoInstallMethod;
        settings.CopilotAutoInstallPrerelease = ReadBool(copilot, "autoInstallPrerelease", settings.CopilotAutoInstallPrerelease);
    }

    private static void ApplyCleanup(JsonObject root, ReviewSettings settings) {
        var cleanup = root.GetObject("cleanup");
        if (cleanup is null) {
            return;
        }
        settings.Cleanup.Enabled = ReadBool(cleanup, "enabled", settings.Cleanup.Enabled);
        var mode = cleanup.GetString("mode");
        if (!string.IsNullOrWhiteSpace(mode)) {
            settings.Cleanup.Mode = CleanupSettings.ParseMode(mode, settings.Cleanup.Mode);
        }
        var scope = cleanup.GetString("scope");
        if (!string.IsNullOrWhiteSpace(scope)) {
            settings.Cleanup.Scope = scope;
        }
        settings.Cleanup.RequireLabel = cleanup.GetString("requireLabel") ?? settings.Cleanup.RequireLabel;
        settings.Cleanup.PostEditComment = ReadBool(cleanup, "postEditComment", settings.Cleanup.PostEditComment);
        var minConfidence = cleanup.GetDouble("minConfidence");
        if (minConfidence.HasValue) {
            settings.Cleanup.MinConfidence = CleanupSettings.ClampConfidence(minConfidence.Value);
        }
        var allowedEdits = ReadStringList(cleanup, "allowedEdits");
        if (allowedEdits is not null) {
            settings.Cleanup.AllowedEdits = allowedEdits;
        }
        settings.Cleanup.Template = cleanup.GetString("template") ?? settings.Cleanup.Template;
        settings.Cleanup.TemplatePath = cleanup.GetString("templatePath") ?? settings.Cleanup.TemplatePath;
    }

    private static IReadOnlyList<string>? ReadStringList(JsonObject obj, string key) {
        if (obj.TryGetValue(key, out var value)) {
            var array = value?.AsArray();
            if (array is not null) {
                var list = new List<string>();
                foreach (var item in array) {
                    var text = item.AsString();
                    if (!string.IsNullOrWhiteSpace(text)) {
                        list.Add(text);
                    }
                }
                return list;
            }
            var textValue = value?.AsString();
            if (!string.IsNullOrWhiteSpace(textValue)) {
                return textValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        return null;
    }

    private static int ReadInt(JsonObject obj, string key, int fallback) {
        var value = obj.GetInt64(key);
        if (value.HasValue && value.Value > 0) {
            return (int)value.Value;
        }
        return fallback;
    }

    private static bool ReadBool(JsonObject obj, string key, bool fallback) {
        if (obj.TryGetValue(key, out var value)) {
            return value?.AsBoolean(fallback) ?? fallback;
        }
        return fallback;
    }
}
