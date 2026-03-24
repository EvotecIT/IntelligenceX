using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IntelligenceX.Cli;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {

    private static string MergeConfigJson(string existingContent, ConfigSettings settings) {
        var node = JsonNode.Parse(existingContent) as JsonObject ?? new JsonObject();
        var review = node["review"] as JsonObject ?? new JsonObject();
        review["provider"] = settings.Provider;
        review["model"] = settings.OpenAIModel;
        if (SetupProviderCatalog.IsOpenAiProvider(settings.Provider)) {
            review["openaiTransport"] = settings.OpenAITransport;
            review.Remove("anthropic");
            if (!string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
                review["openaiAccountId"] = settings.OpenAIAccountId;
            }
            if (settings.OpenAIAccountIds.Length > 0) {
                var accountIds = new JsonArray();
                foreach (var accountId in settings.OpenAIAccountIds) {
                    accountIds.Add(accountId);
                }
                review["openaiAccountIds"] = accountIds;
                review["openaiAccountRotation"] = settings.OpenAIAccountRotation;
                review["openaiAccountFailover"] = settings.OpenAIAccountFailover;
            } else if (settings.OpenAIAccountIdsSet) {
                review.Remove("openaiAccountIds");
                if (string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
                    review.Remove("openaiAccountRotation");
                    review.Remove("openaiAccountFailover");
                } else {
                    review["openaiAccountRotation"] = settings.OpenAIAccountRotation;
                    review["openaiAccountFailover"] = settings.OpenAIAccountFailover;
                }
            } else if (!string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
                review["openaiAccountRotation"] = settings.OpenAIAccountRotation;
                review["openaiAccountFailover"] = settings.OpenAIAccountFailover;
            }
        } else {
            review.Remove("openaiTransport");
            review.Remove("openaiAccountId");
            review.Remove("openaiAccountIds");
            review.Remove("openaiAccountRotation");
            review.Remove("openaiAccountFailover");
            if (SetupProviderCatalog.IsClaudeProvider(settings.Provider)) {
                review["anthropic"] = new JsonObject {
                    ["apiKeyEnv"] = SetupProviderCatalog.ClaudeSecretName
                };
            } else {
                review.Remove("anthropic");
            }
        }
        review["summaryStability"] = settings.SummaryStability;
        review["reviewDiffRange"] = settings.ReviewDiffRange;
        if (!string.IsNullOrWhiteSpace(settings.Intent)) {
            review["intent"] = settings.Intent;
        }
        if (!string.IsNullOrWhiteSpace(settings.Strictness)) {
            review["strictness"] = settings.Strictness;
        }
        if (!string.IsNullOrWhiteSpace(settings.VisionPath)) {
            review["visionPath"] = settings.VisionPath;
        }
        if (settings.MergeBlockerSectionsSet && settings.MergeBlockerSections.Length > 0) {
            var sections = new JsonArray();
            foreach (var section in settings.MergeBlockerSections) {
                sections.Add(section);
            }
            review["mergeBlockerSections"] = sections;
        }
        if (settings.MergeBlockerRequireAllSectionsSet) {
            review["mergeBlockerRequireAllSections"] = settings.MergeBlockerRequireAllSections;
        }
        if (settings.MergeBlockerRequireSectionMatchSet) {
            review["mergeBlockerRequireSectionMatch"] = settings.MergeBlockerRequireSectionMatch;
        }
        review["profile"] = settings.Profile;
        review["mode"] = settings.Mode;
        review["commentMode"] = settings.CommentMode;
        review["includeIssueComments"] = settings.IncludeIssueComments;
        review["includeReviewComments"] = settings.IncludeReviewComments;
        review["includeReviewThreads"] = settings.IncludeReviewThreads;
        review["reviewThreadsIncludeBots"] = settings.ReviewThreadsIncludeBots;
        review["reviewThreadsMax"] = settings.ReviewThreadsMax;
        review["reviewThreadsMaxComments"] = settings.ReviewThreadsMaxComments;
        review["reviewThreadsAutoResolveStale"] = settings.ReviewThreadsAutoResolveStale;
        review["reviewThreadsAutoResolveDiffRange"] = settings.ReviewThreadsAutoResolveDiffRange;
        review["reviewThreadsAutoResolveMax"] = settings.ReviewThreadsAutoResolveMax;
        review["reviewThreadsAutoResolveSweepNoBlockers"] = settings.ReviewThreadsAutoResolveSweepNoBlockers;
        review["reviewThreadsAutoResolveAIReply"] = settings.ReviewThreadsAutoResolveAIReply;
        review["reviewUsageSummary"] = settings.ReviewUsageSummary;
        review["reviewUsageSummaryCacheMinutes"] = settings.ReviewUsageSummaryCacheMinutes;
        review["reviewUsageSummaryTimeoutSeconds"] = settings.ReviewUsageSummaryTimeoutSeconds;
        review["reviewUsageBudgetGuard"] = settings.ReviewUsageBudgetGuard;
        review["reviewUsageBudgetAllowCredits"] = settings.ReviewUsageBudgetAllowCredits;
        review["reviewUsageBudgetAllowWeeklyLimit"] = settings.ReviewUsageBudgetAllowWeeklyLimit;
        review["includeRelatedPrs"] = settings.IncludeRelatedPullRequests;
        review["progressUpdates"] = settings.ProgressUpdates;
        review["diagnostics"] = settings.Diagnostics;
        review["preflight"] = settings.Preflight;
        review["preflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds;
        node["review"] = review;
        if (settings.AnalysisEnabledSet || settings.AnalysisGateEnabledSet || settings.AnalysisPacksSet ||
            settings.AnalysisRunStrictSet) {
            SetupAnalysisConfig.Apply(node,
                enabledSet: settings.AnalysisEnabledSet, enabled: settings.AnalysisEnabled,
                gateEnabledSet: settings.AnalysisGateEnabledSet, gateEnabled: settings.AnalysisGateEnabled,
                packsSet: settings.AnalysisPacksSet, packs: settings.AnalysisPacks,
                runStrictSet: settings.AnalysisRunStrictSet, runStrict: settings.AnalysisRunStrict);
        }
        return node.ToJsonString(CliJson.Indented);
    }

    private static string BuildWorkflowYaml(WorkflowSettings settings) {
        var template = ReadEmbeddedResource("review-intelligencex.yml");
        var managed = BuildManagedWorkflowBlock(settings, indent: 2).TrimEnd();
        return template.Replace("{{ManagedBlock}}", managed, StringComparison.Ordinal);
    }

    private static string BuildManagedWorkflowBlock(WorkflowSettings settings, int indent) {
        var template = settings.ExplicitSecrets
            ? ReadEmbeddedResource("review-intelligencex.managed.explicit.yml")
            : ReadEmbeddedResource("review-intelligencex.managed.yml");
        var tokens = new Dictionary<string, string> {
            ["ReusableWorkflowRef"] = settings.UseLocalReusableWorkflow
                ? "./.github/workflows/review-intelligencex-reusable.yml"
                : $"{settings.ActionsRepo}/.github/workflows/review-intelligencex-reusable.yml@{settings.ActionsRef}",
            ["RunsOn"] = NormalizeRunsOn(settings.RunsOn),
            ["ReviewerSource"] = settings.ReviewerSource,
            ["ReviewerReleaseRepo"] = settings.ReviewerReleaseRepo,
            ["ReviewerReleaseTag"] = settings.ReviewerReleaseTag,
            ["ReviewerReleaseAssetLine"] = string.IsNullOrWhiteSpace(settings.ReviewerReleaseAsset)
                ? string.Empty
                : $"reviewer_release_asset: {YamlQuote(settings.ReviewerReleaseAsset)}",
            ["ReviewerReleaseUrlLine"] = string.IsNullOrWhiteSpace(settings.ReviewerReleaseUrl)
                ? string.Empty
                : $"reviewer_release_url: {YamlQuote(settings.ReviewerReleaseUrl)}",
            ["Provider"] = settings.Provider,
            ["Model"] = settings.Model,
            ["OpenAITransport"] = settings.OpenAITransport,
            ["IncludeIssueComments"] = ToYamlBool(settings.IncludeIssueComments),
            ["IncludeReviewComments"] = ToYamlBool(settings.IncludeReviewComments),
            ["IncludeRelatedPullRequests"] = ToYamlBool(settings.IncludeRelatedPullRequests),
            ["ProgressUpdates"] = ToYamlBool(settings.ProgressUpdates),
            ["Diagnostics"] = ToYamlBool(settings.Diagnostics),
            ["Preflight"] = ToYamlBool(settings.Preflight),
            ["PreflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["CleanupEnabled"] = ToYamlBool(settings.CleanupEnabled),
            ["CleanupMode"] = settings.CleanupMode,
            ["CleanupScope"] = settings.CleanupScope,
            ["CleanupRequireLabel"] = YamlQuote(settings.CleanupRequireLabel),
            ["CleanupMinConfidence"] = settings.CleanupMinConfidence.ToString(CultureInfo.InvariantCulture),
            ["CleanupAllowedEdits"] = YamlQuote(settings.CleanupAllowedEdits),
            ["CleanupPostEditComment"] = ToYamlBool(settings.CleanupPostEditComment)
        };

        var block = ReplaceTokens(template, tokens);
        return IndentBlock(block, indent);
    }

    private static string ReplaceTokens(string template, IReadOnlyDictionary<string, string> tokens) {
        var result = template;
        foreach (var pair in tokens) {
            result = result.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty, StringComparison.Ordinal);
        }
        return result;
    }

    private static string IndentBlock(string content, int indent) {
        if (indent <= 0) {
            return content;
        }
        var normalized = NormalizeLineEndings(content);
        var lines = normalized.Split('\n');
        var pad = new string(' ', indent);
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].Length > 0) {
                lines[i] = pad + lines[i];
            }
        }
        return string.Join("\n", lines);
    }

    private static string ToYamlBool(bool value) => value ? "true" : "false";

    private static string YamlQuote(string? value) {
        var raw = value ?? string.Empty;
        var escaped = raw.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    private static string ReadEmbeddedResource(string name) {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) {
            throw new InvalidOperationException($"Embedded template not found: {name}");
        }
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string NormalizeRunsOn(string? value) {
        var trimmed = string.IsNullOrWhiteSpace(value) ? DefaultRunsOn : value.Trim();
        if (trimmed.StartsWith("${{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal)) {
            return trimmed;
        }
        if (trimmed.StartsWith("'") && trimmed.EndsWith("'")) {
            return trimmed;
        }
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) {
            return trimmed;
        }
        var escaped = trimmed.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    private static string NormalizeActionsRepo(string value) {
        var trimmed = value.Trim().TrimEnd('/');
        var marker = "/.github/workflows/";
        var index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index > 0) {
            trimmed = trimmed.Substring(0, index);
        }
        return trimmed.TrimEnd('/');
    }

    private static string? TryExtractManagedBlock(string content) {
        var pattern = @"^[ \t]*# INTELLIGENCEX:BEGIN[\s\S]*?^[ \t]*# INTELLIGENCEX:END[ \t]*\r?$";
        var match = Regex.Match(content, pattern, RegexOptions.Multiline);
        return match.Success ? match.Value : null;
    }

    private static string? ReplaceManagedBlock(string content, string newBlock) {
        var pattern = @"^[ \t]*# INTELLIGENCEX:BEGIN[\s\S]*?^[ \t]*# INTELLIGENCEX:END[ \t]*\r?$";
        if (!Regex.IsMatch(content, pattern, RegexOptions.Multiline)) {
            return null;
        }
        return Regex.Replace(content, pattern, newBlock.TrimEnd(), RegexOptions.Multiline);
    }

    private static bool TryReadWorkflowSnapshot(string content, out WorkflowSnapshot snapshot) {
        snapshot = new WorkflowSnapshot();
        var localMatch = Regex.Match(content,
            @"^\s*uses:\s*(\./\.github/workflows/review-intelligencex-reusable\.yml)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (localMatch.Success) {
            snapshot.UseLocalReusableWorkflow = true;
        } else {
            var remoteMatch = Regex.Match(content,
                @"^\s*uses:\s*([^\s@]+/\.github/workflows/review-intelligencex-reusable\.yml)@([^\s]+)\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (remoteMatch.Success) {
                snapshot.ActionsRepo = NormalizeActionsRepo(remoteMatch.Groups[1].Value.Trim());
                snapshot.ActionsRef = remoteMatch.Groups[2].Value.Trim();
            }
        }

        snapshot.RunsOn = ReadYamlScalar(content, "runs_on");
        snapshot.ReviewerSource = ReadYamlScalar(content, "reviewer_source");
        snapshot.ReviewerReleaseRepo = ReadYamlScalar(content, "reviewer_release_repo");
        snapshot.ReviewerReleaseTag = ReadYamlScalar(content, "reviewer_release_tag");
        snapshot.ReviewerReleaseAsset = ReadYamlScalar(content, "reviewer_release_asset");
        snapshot.ReviewerReleaseUrl = ReadYamlScalar(content, "reviewer_release_url");
        snapshot.Provider = ReadYamlScalar(content, "provider");
        snapshot.Model = ReadYamlScalar(content, "model");
        snapshot.OpenAITransport = ReadYamlScalar(content, "openai_transport");
        snapshot.IncludeIssueComments = ReadYamlBool(content, "include_issue_comments");
        snapshot.IncludeReviewComments = ReadYamlBool(content, "include_review_comments");
        snapshot.IncludeRelatedPullRequests = ReadYamlBool(content, "include_related_prs");
        snapshot.ProgressUpdates = ReadYamlBool(content, "progress_updates");
        snapshot.Diagnostics = ReadYamlBool(content, "diagnostics");
        snapshot.Preflight = ReadYamlBool(content, "preflight");
        snapshot.PreflightTimeoutSeconds = ReadYamlInt(content, "preflight_timeout_seconds");
        snapshot.CleanupEnabled = ReadYamlBool(content, "cleanup_enabled");
        snapshot.CleanupMode = ReadYamlScalar(content, "cleanup_mode");
        snapshot.CleanupScope = ReadYamlScalar(content, "cleanup_scope");
        snapshot.CleanupRequireLabel = ReadYamlScalar(content, "cleanup_require_label");
        snapshot.CleanupMinConfidence = ReadYamlDouble(content, "cleanup_min_confidence");
        snapshot.CleanupAllowedEdits = ReadYamlScalar(content, "cleanup_allowed_edits");
        snapshot.CleanupPostEditComment = ReadYamlBool(content, "cleanup_post_edit_comment");

        return snapshot.HasAny;
    }

    private static string? ReadYamlScalar(string content, string key) {
        var pattern = @"^\s*" + Regex.Escape(key) + @"\s*:\s*(.+?)\s*$";
        var match = Regex.Match(content, pattern, RegexOptions.Multiline);
        if (!match.Success) {
            return null;
        }
        var value = match.Groups[1].Value.Trim();
        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        value = commentIndex >= 0 ? value.Substring(0, commentIndex).Trim() : value;
        if (value.Length >= 2 &&
            ((value.StartsWith('\'') && value.EndsWith('\'')) || (value.StartsWith('"') && value.EndsWith('"')))) {
            value = value.Substring(1, value.Length - 2);
            value = value.Replace("''", "'", StringComparison.Ordinal);
        }
        return value;
    }

    private static bool? ReadYamlBool(string content, string key) {
        var value = ReadYamlScalar(content, key);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return value.Trim().ToLowerInvariant() switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => null
        };
    }

    private static double? ReadYamlDouble(string content, string key) {
        var value = ReadYamlScalar(content, key);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) {
            return result;
        }
        return null;
    }

    private static int? ReadYamlInt(string content, string key) {
        var value = ReadYamlScalar(content, key);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) {
            return result;
        }
        return null;
    }

    private static bool TryReadConfigSnapshot(string content, out ConfigSnapshot snapshot) {
        snapshot = new ConfigSnapshot();
        try {
            var root = JsonNode.Parse(content) as JsonObject;
            if (root is null) {
                return false;
            }
            var review = root["review"] as JsonObject;
            if (review is not null) {
                snapshot.Provider = ReadJsonString(review, "provider");
                snapshot.OpenAITransport = ReadJsonString(review, "openaiTransport");
                snapshot.OpenAIModel = ReadJsonString(review, "model") ?? ReadJsonString(review, "openaiModel");
                snapshot.OpenAIAccountId = ReadJsonString(review, "openaiAccountId") ?? ReadJsonString(review, "authAccountId");
                snapshot.OpenAIAccountIds = ReadJsonStringArray(review, "openaiAccountIds");
                if (snapshot.OpenAIAccountIds.Length == 0) {
                    snapshot.OpenAIAccountIds = ReadJsonStringArray(review, "openAiAccountIds");
                }
                snapshot.OpenAIAccountRotation =
                    ReadJsonString(review, "openaiAccountRotation") ??
                    ReadJsonString(review, "openAiAccountRotation");
                snapshot.OpenAIAccountFailover =
                    ReadJsonBool(review, "openaiAccountFailover") ??
                    ReadJsonBool(review, "openAiAccountFailover");
                snapshot.SummaryStability = ReadJsonBool(review, "summaryStability");
                snapshot.ReviewDiffRange = ReadJsonString(review, "reviewDiffRange");
                snapshot.Intent = ReadJsonString(review, "intent");
                snapshot.Strictness = ReadJsonString(review, "strictness");
                snapshot.VisionPath = ReadJsonString(review, "visionPath");
                snapshot.Profile = ReadJsonString(review, "profile");
                snapshot.MergeBlockerSections = ReadJsonStringArray(review, "mergeBlockerSections");
                snapshot.MergeBlockerRequireAllSections = ReadJsonBool(review, "mergeBlockerRequireAllSections");
                snapshot.MergeBlockerRequireSectionMatch = ReadJsonBool(review, "mergeBlockerRequireSectionMatch");
                snapshot.Mode = ReadJsonString(review, "mode");
                snapshot.CommentMode = ReadJsonString(review, "commentMode");
                snapshot.IncludeIssueComments = ReadJsonBool(review, "includeIssueComments");
                snapshot.IncludeReviewComments = ReadJsonBool(review, "includeReviewComments");
                snapshot.IncludeReviewThreads = ReadJsonBool(review, "includeReviewThreads");
                snapshot.ReviewThreadsIncludeBots = ReadJsonBool(review, "reviewThreadsIncludeBots");
                snapshot.ReviewThreadsMax = ReadJsonInt(review, "reviewThreadsMax");
                snapshot.ReviewThreadsMaxComments = ReadJsonInt(review, "reviewThreadsMaxComments");
                snapshot.ReviewThreadsAutoResolveStale = ReadJsonBool(review, "reviewThreadsAutoResolveStale");
                snapshot.ReviewThreadsAutoResolveDiffRange = ReadJsonString(review, "reviewThreadsAutoResolveDiffRange");
                snapshot.ReviewThreadsAutoResolveMax = ReadJsonInt(review, "reviewThreadsAutoResolveMax");
                snapshot.ReviewThreadsAutoResolveSweepNoBlockers = ReadJsonBool(review, "reviewThreadsAutoResolveSweepNoBlockers");
                snapshot.ReviewThreadsAutoResolveAIReply = ReadJsonBool(review, "reviewThreadsAutoResolveAIReply");
                snapshot.ReviewUsageSummary = ReadJsonBool(review, "reviewUsageSummary");
                snapshot.ReviewUsageSummaryCacheMinutes = ReadJsonInt(review, "reviewUsageSummaryCacheMinutes");
                snapshot.ReviewUsageSummaryTimeoutSeconds = ReadJsonInt(review, "reviewUsageSummaryTimeoutSeconds");
                snapshot.ReviewUsageBudgetGuard = ReadJsonBool(review, "reviewUsageBudgetGuard");
                snapshot.ReviewUsageBudgetAllowCredits = ReadJsonBool(review, "reviewUsageBudgetAllowCredits");
                snapshot.ReviewUsageBudgetAllowWeeklyLimit = ReadJsonBool(review, "reviewUsageBudgetAllowWeeklyLimit");
                snapshot.IncludeRelatedPullRequests =
                    ReadJsonBool(review, "includeRelatedPrs") ??
                    ReadJsonBool(review, "includeRelatedPullRequests");
                snapshot.ProgressUpdates = ReadJsonBool(review, "progressUpdates");
                snapshot.Diagnostics = ReadJsonBool(review, "diagnostics");
                snapshot.Preflight = ReadJsonBool(review, "preflight");
                snapshot.PreflightTimeoutSeconds = ReadJsonInt(review, "preflightTimeoutSeconds");
            }
            var analysis = root["analysis"] as JsonObject;
            if (analysis is not null) {
                snapshot.AnalysisEnabled = ReadJsonBool(analysis, "enabled");
                snapshot.AnalysisPacks = SetupAnalysisConfig.ReadStringArray(analysis, "packs");
                var run = analysis["run"] as JsonObject;
                if (run is not null) {
                    snapshot.AnalysisRunStrict = ReadJsonBool(run, "strict");
                }
                var gate = analysis["gate"] as JsonObject;
                if (gate is not null) {
                    snapshot.AnalysisGateEnabled = ReadJsonBool(gate, "enabled");
                }
            }
            return true;
        } catch {
            return false;
        }
    }

    private static string? ReadJsonString(JsonObject obj, string key) {
        return obj.TryGetPropertyValue(key, out var value) ? value?.GetValue<string>() : null;
    }

    private static bool? ReadJsonBool(JsonObject obj, string key) {
        if (!obj.TryGetPropertyValue(key, out var value) || value is null) {
            return null;
        }
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var result)) {
            return result;
        }
        return null;
    }

    private static int? ReadJsonInt(JsonObject obj, string key) {
        if (!obj.TryGetPropertyValue(key, out var value) || value is null) {
            return null;
        }
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var result)) {
            return result;
        }
        return null;
    }

    private static string[] ReadJsonStringArray(JsonObject obj, string key) {
        if (!obj.TryGetPropertyValue(key, out var value) || value is not JsonArray array) {
            return Array.Empty<string>();
        }
        var result = new List<string>();
        foreach (var node in array) {
            if (node is null) {
                continue;
            }
            try {
                var text = node.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text)) {
                    result.Add(text.Trim());
                }
            } catch {
                // Skip non-string values.
            }
        }
        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeLineEndings(string value) {
        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static bool IsIntelligenceXWorkflow(string content) {
        if (string.IsNullOrWhiteSpace(content)) {
            return false;
        }
        return content.IndexOf("review-intelligencex.yml", StringComparison.OrdinalIgnoreCase) >= 0 ||
               content.IndexOf("IntelligenceX Review", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
