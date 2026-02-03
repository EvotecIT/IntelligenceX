using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Copilot;
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

        var intent = reviewObj.GetString("intent");
        if (!string.IsNullOrWhiteSpace(intent)) {
            ReviewIntents.Apply(intent!, settings);
            settings.Intent = intent;
        }

        var provider = reviewObj.GetString("provider");
        if (!string.IsNullOrWhiteSpace(provider)) {
            settings.Provider = provider.Trim().ToLowerInvariant() switch {
                "copilot" => ReviewProvider.Copilot,
                "openai" or "codex" => ReviewProvider.OpenAI,
                _ => settings.Provider
            };
        }

        var codeHost = reviewObj.GetString("codeHost");
        if (!string.IsNullOrWhiteSpace(codeHost)) {
            settings.CodeHost = codeHost.Trim().ToLowerInvariant() switch {
                "azure" or "azuredevops" or "azure-devops" or "ado" => ReviewCodeHost.AzureDevOps,
                _ => ReviewCodeHost.GitHub
            };
        }

        var style = reviewObj.GetString("style");
        if (!string.IsNullOrWhiteSpace(style)) {
            settings.Style = style;
            ReviewStyles.Apply(style!, settings);
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
        ApplyAzureDevOps(reviewObj, settings);
        ApplyCleanup(root, settings);
    }

    internal static string? ResolveConfigPath() {
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
        var reviewDiffRange = obj.GetString("reviewDiffRange");
        if (!string.IsNullOrWhiteSpace(reviewDiffRange)) {
            settings.ReviewDiffRange = ReviewSettings.NormalizeDiffRange(reviewDiffRange, settings.ReviewDiffRange);
        }
        var reasoningEffort = obj.GetString("reasoningEffort");
        if (!string.IsNullOrWhiteSpace(reasoningEffort)) {
            var parsed = IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseReasoningEffort(reasoningEffort);
            if (parsed.HasValue) {
                settings.ReasoningEffort = parsed;
            }
        }
        var reasoningSummary = obj.GetString("reasoningSummary");
        if (!string.IsNullOrWhiteSpace(reasoningSummary)) {
            var parsed = IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseReasoningSummary(reasoningSummary);
            if (parsed.HasValue) {
                settings.ReasoningSummary = parsed;
            }
        }
        settings.SeverityThreshold = obj.GetString("severityThreshold") ?? settings.SeverityThreshold;
        settings.RedactionReplacement = obj.GetString("redactionReplacement") ?? settings.RedactionReplacement;
        settings.PromptTemplate = obj.GetString("promptTemplate") ?? settings.PromptTemplate;
        settings.PromptTemplatePath = obj.GetString("promptTemplatePath") ?? settings.PromptTemplatePath;
        settings.OutputStyle = obj.GetString("outputStyle") ?? settings.OutputStyle;
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

        var includePaths = ReadStringList(obj, "includePaths");
        if (includePaths is not null) {
            settings.IncludePaths = includePaths;
        }

        var excludePaths = ReadStringList(obj, "excludePaths");
        if (excludePaths is not null) {
            settings.ExcludePaths = excludePaths;
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
        settings.RetryCount = ReadInt(obj, "retryCount", settings.RetryCount);
        settings.RetryDelaySeconds = ReadInt(obj, "retryDelaySeconds", settings.RetryDelaySeconds);
        settings.RetryMaxDelaySeconds = ReadInt(obj, "retryMaxDelaySeconds", settings.RetryMaxDelaySeconds);
        settings.RetryBackoffMultiplier = ReadDouble(obj, "retryBackoffMultiplier", settings.RetryBackoffMultiplier);
        settings.RetryJitterMinMs = ReadNonNegativeInt(obj, "retryJitterMinMs", settings.RetryJitterMinMs);
        settings.RetryJitterMaxMs = ReadNonNegativeInt(obj, "retryJitterMaxMs", settings.RetryJitterMaxMs);
        settings.PreflightTimeoutSeconds = ReadInt(obj, "preflightTimeoutSeconds", settings.PreflightTimeoutSeconds);
        settings.ReviewUsageSummaryCacheMinutes = Math.Max(0,
            ReadInt(obj, "reviewUsageSummaryCacheMinutes", settings.ReviewUsageSummaryCacheMinutes));
        settings.ReviewUsageSummaryTimeoutSeconds = Math.Max(1,
            ReadInt(obj, "reviewUsageSummaryTimeoutSeconds", settings.ReviewUsageSummaryTimeoutSeconds));
    }

    private static void ApplyBooleans(JsonObject obj, ReviewSettings settings) {
        settings.IncludeNextSteps = ReadBool(obj, "includeNextSteps", settings.IncludeNextSteps);
        settings.IncludeLanguageHints = ReadBool(obj, "languageHints", settings.IncludeLanguageHints);
        settings.ReviewBudgetSummary = ReadBool(obj, "reviewBudgetSummary", settings.ReviewBudgetSummary);
        settings.OverwriteSummary = ReadBool(obj, "overwriteSummary", settings.OverwriteSummary);
        settings.OverwriteSummaryOnNewCommit = ReadBool(obj, "overwriteSummaryOnNewCommit", settings.OverwriteSummaryOnNewCommit);
        settings.SummaryStability = ReadBool(obj, "summaryStability", settings.SummaryStability);
        settings.SkipDraft = ReadBool(obj, "skipDraft", settings.SkipDraft);
        settings.RedactPii = ReadBool(obj, "redactPii", settings.RedactPii);
        settings.ProgressUpdates = ReadBool(obj, "progressUpdates", settings.ProgressUpdates);
        settings.Diagnostics = ReadBool(obj, "diagnostics", settings.Diagnostics);
        settings.Preflight = ReadBool(obj, "preflight", settings.Preflight);
        settings.RetryExtraOnResponseEnded = ReadBool(obj, "retryExtraResponseEnded", settings.RetryExtraOnResponseEnded);
        settings.FailOpen = ReadBool(obj, "failOpen", settings.FailOpen);
        settings.FailOpenTransientOnly = ReadBool(obj, "failOpenTransientOnly", settings.FailOpenTransientOnly);
        settings.ReviewUsageSummary = ReadBool(obj, "reviewUsageSummary", settings.ReviewUsageSummary);
        settings.StructuredFindings = ReadBool(obj, "structuredFindings", settings.StructuredFindings);
        settings.TriageOnly = ReadBool(obj, "triageOnly", settings.TriageOnly);
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
        settings.IncludeReviewThreads = ReadBool(obj, "includeReviewThreads", settings.IncludeReviewThreads);
        settings.ReviewThreadsIncludeBots = ReadBool(obj, "reviewThreadsIncludeBots", settings.ReviewThreadsIncludeBots);
        settings.ReviewThreadsIncludeResolved = ReadBool(obj, "reviewThreadsIncludeResolved", settings.ReviewThreadsIncludeResolved);
        settings.ReviewThreadsIncludeOutdated = ReadBool(obj, "reviewThreadsIncludeOutdated", settings.ReviewThreadsIncludeOutdated);
        settings.ReviewThreadsMax = ReadNonNegativeInt(obj, "reviewThreadsMax", settings.ReviewThreadsMax);
        settings.ReviewThreadsMaxComments = ReadNonNegativeInt(obj, "reviewThreadsMaxComments", settings.ReviewThreadsMaxComments);
        settings.ReviewThreadsAutoResolveStale = ReadBool(obj, "reviewThreadsAutoResolveStale", settings.ReviewThreadsAutoResolveStale);
        settings.ReviewThreadsAutoResolveMissingInline = ReadBool(obj, "reviewThreadsAutoResolveMissingInline", settings.ReviewThreadsAutoResolveMissingInline);
        settings.ReviewThreadsAutoResolveBotsOnly = ReadBool(obj, "reviewThreadsAutoResolveBotsOnly", settings.ReviewThreadsAutoResolveBotsOnly);
        var reviewThreadsAutoResolveBotLogins = ReadStringList(obj, "reviewThreadsAutoResolveBotLogins");
        if (reviewThreadsAutoResolveBotLogins is not null) {
            settings.ReviewThreadsAutoResolveBotLogins = reviewThreadsAutoResolveBotLogins;
        }
        var diffRange = obj.GetString("reviewThreadsAutoResolveDiffRange");
        if (!string.IsNullOrWhiteSpace(diffRange)) {
            settings.ReviewThreadsAutoResolveDiffRange = ReviewSettings.NormalizeDiffRange(diffRange, settings.ReviewThreadsAutoResolveDiffRange);
        }
        settings.ReviewThreadsAutoResolveMax = ReadNonNegativeInt(obj, "reviewThreadsAutoResolveMax", settings.ReviewThreadsAutoResolveMax);
        settings.ReviewThreadsAutoResolveAI = ReadBool(obj, "reviewThreadsAutoResolveAI", settings.ReviewThreadsAutoResolveAI);
        settings.ReviewThreadsAutoResolveAIPostComment = ReadBool(obj, "reviewThreadsAutoResolveAIPostComment", settings.ReviewThreadsAutoResolveAIPostComment);
        settings.ReviewThreadsAutoResolveAIEmbed = ReadBool(obj, "reviewThreadsAutoResolveAIEmbed", settings.ReviewThreadsAutoResolveAIEmbed);
        settings.ReviewThreadsAutoResolveAISummary = ReadBool(obj, "reviewThreadsAutoResolveAISummary", settings.ReviewThreadsAutoResolveAISummary);
        settings.ReviewThreadsAutoResolveAIReply = ReadBool(obj, "reviewThreadsAutoResolveAIReply", settings.ReviewThreadsAutoResolveAIReply);
        settings.MaxCommentChars = ReadInt(obj, "maxCommentChars", settings.MaxCommentChars);
        settings.MaxComments = ReadInt(obj, "maxComments", settings.MaxComments);
        settings.CommentSearchLimit = ReadInt(obj, "commentSearchLimit", settings.CommentSearchLimit);
        settings.ContextDenyEnabled = ReadBool(obj, "contextDenyEnabled", settings.ContextDenyEnabled);
        var contextDenyPatterns = ReadStringList(obj, "contextDenyPatterns");
        if (contextDenyPatterns is not null) {
            settings.ContextDenyPatterns = contextDenyPatterns;
        }
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
        var envAllowlist = ReadStringList(copilot, "envAllowlist");
        if (envAllowlist is not null) {
            settings.CopilotEnvAllowlist = envAllowlist;
        }
        settings.CopilotInheritEnvironment = ReadBool(copilot, "inheritEnvironment", settings.CopilotInheritEnvironment);
        var env = ReadStringMap(copilot, "env");
        if (env is not null) {
            settings.CopilotEnv = env;
        }
        var transport = copilot.GetString("transport");
        if (!string.IsNullOrWhiteSpace(transport)) {
            settings.CopilotTransport = ParseCopilotTransport(transport, settings.CopilotTransport);
        }
        settings.CopilotDirectUrl = copilot.GetString("directUrl") ?? settings.CopilotDirectUrl;
        settings.CopilotDirectToken = copilot.GetString("directToken") ?? settings.CopilotDirectToken;
        settings.CopilotDirectTokenEnv = copilot.GetString("directTokenEnv") ?? settings.CopilotDirectTokenEnv;
        settings.CopilotDirectTimeoutSeconds = ReadInt(copilot, "directTimeoutSeconds", settings.CopilotDirectTimeoutSeconds);
        var directHeaders = ReadStringMap(copilot, "directHeaders");
        if (directHeaders is not null) {
            settings.CopilotDirectHeaders = directHeaders;
        }
    }

    private static void ApplyAzureDevOps(JsonObject obj, ReviewSettings settings) {
        var org = obj.GetString("azureOrg");
        if (!string.IsNullOrWhiteSpace(org)) {
            settings.AzureOrganization = org;
        }
        var project = obj.GetString("azureProject");
        if (!string.IsNullOrWhiteSpace(project)) {
            settings.AzureProject = project;
        }
        var repo = obj.GetString("azureRepo");
        if (!string.IsNullOrWhiteSpace(repo)) {
            settings.AzureRepository = repo;
        }
        var baseUrl = obj.GetString("azureBaseUrl");
        if (!string.IsNullOrWhiteSpace(baseUrl)) {
            settings.AzureBaseUrl = baseUrl;
        }
        var tokenEnv = obj.GetString("azureTokenEnv");
        if (!string.IsNullOrWhiteSpace(tokenEnv)) {
            settings.AzureTokenEnv = tokenEnv;
        }
        var authScheme = obj.GetString("azureAuthScheme");
        if (!string.IsNullOrWhiteSpace(authScheme)) {
            settings.AzureAuthScheme = ReviewSettings.ParseAzureAuthScheme(authScheme);
            settings.AzureAuthSchemeSpecified = true;
        }
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
            settings.Cleanup.AllowedEdits = CleanupSettings.NormalizeAllowedEdits(allowedEdits);
        }
        settings.Cleanup.Template = cleanup.GetString("template") ?? settings.Cleanup.Template;
        settings.Cleanup.TemplatePath = cleanup.GetString("templatePath") ?? settings.Cleanup.TemplatePath;
    }

    private static CopilotTransportKind ParseCopilotTransport(string value, CopilotTransportKind fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "direct" or "api" or "http" => CopilotTransportKind.Direct,
            "cli" => CopilotTransportKind.Cli,
            _ => fallback
        };
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

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonObject obj, string key) {
        if (!obj.TryGetValue(key, out var value)) {
            return null;
        }
        var mapObj = value?.AsObject();
        if (mapObj is null || mapObj.Count == 0) {
            return null;
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mapObj) {
            if (string.IsNullOrWhiteSpace(entry.Key)) {
                continue;
            }
            var text = entry.Value?.AsString();
            if (text is null) {
                continue;
            }
            result[entry.Key] = text;
        }
        return result;
    }

    private static int ReadInt(JsonObject obj, string key, int fallback) {
        var value = obj.GetInt64(key);
        if (value.HasValue && value.Value > 0) {
            return (int)value.Value;
        }
        return fallback;
    }

    private static int ReadNonNegativeInt(JsonObject obj, string key, int fallback) {
        var value = obj.GetInt64(key);
        if (value.HasValue && value.Value >= 0) {
            return (int)value.Value;
        }
        return fallback;
    }

    private static double ReadDouble(JsonObject obj, string key, double fallback) {
        var value = obj.GetDouble(key);
        if (value.HasValue && value.Value >= 1) {
            return value.Value;
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
