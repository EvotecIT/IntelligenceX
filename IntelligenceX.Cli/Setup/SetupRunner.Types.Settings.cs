using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private sealed class WorkflowSettings {
        public string ActionsRepo { get; set; } = DefaultActionsRepo;
        public string ActionsRef { get; set; } = DefaultActionsRef;
        public bool UseLocalReusableWorkflow { get; set; }
        public string RunsOn { get; set; } = DefaultRunsOn;
        public string ReviewerSource { get; set; } = "release";
        public string ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string Provider { get; set; } = "openai";
        public string Model { get; set; } = OpenAIModelCatalog.DefaultModel;
        public string OpenAITransport { get; set; } = "native";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool ExplicitSecrets { get; set; } = true;
        public bool Diagnostics { get; set; }
        public bool Preflight { get; set; }
        public int PreflightTimeoutSeconds { get; set; } = 15;
        public bool CleanupEnabled { get; set; }
        public string CleanupMode { get; set; } = "comment";
        public string CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;

        public static WorkflowSettings FromOptions(SetupOptions options) {
            return new WorkflowSettings {
                ActionsRepo = NormalizeActionsRepo(options.ActionsRepo ?? DefaultActionsRepo),
                ActionsRef = options.ActionsRef ?? DefaultActionsRef,
                UseLocalReusableWorkflow = false,
                RunsOn = options.RunsOn ?? DefaultRunsOn,
                ReviewerSource = options.ReviewerSource ?? "release",
                ReviewerReleaseRepo = options.ReviewerReleaseRepo ?? "EvotecIT/github-actions",
                ReviewerReleaseTag = options.ReviewerReleaseTag ?? "latest",
                ReviewerReleaseAsset = options.ReviewerReleaseAsset,
                ReviewerReleaseUrl = options.ReviewerReleaseUrl,
                Provider = options.Provider ?? "openai",
                Model = options.OpenAIModel ?? OpenAIModelCatalog.DefaultModel,
                OpenAITransport = options.OpenAITransport ?? "native",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates,
                ExplicitSecrets = options.ExplicitSecrets,
                Diagnostics = options.Diagnostics,
                Preflight = options.Preflight,
                PreflightTimeoutSeconds = options.PreflightTimeoutSeconds,
                CleanupEnabled = options.CleanupEnabled,
                CleanupMode = options.CleanupMode ?? "comment",
                CleanupScope = options.CleanupScope ?? "pr",
                CleanupRequireLabel = options.CleanupRequireLabel ?? string.Empty,
                CleanupMinConfidence = options.CleanupMinConfidence,
                CleanupAllowedEdits = options.CleanupAllowedEdits ?? "formatting,grammar,title,sections",
                CleanupPostEditComment = options.CleanupPostEditComment
            };
        }
    }

    private sealed class ConfigSettings {
        public string Provider { get; set; } = "openai";
        public string OpenAITransport { get; set; } = "native";
        public string OpenAIModel { get; set; } = OpenAIModelCatalog.DefaultModel;
        public string? OpenAIAccountId { get; set; }
        public bool OpenAIAccountIdsSet { get; set; }
        public string[] OpenAIAccountIds { get; set; } = Array.Empty<string>();
        public string OpenAIAccountRotation { get; set; } = "first-available";
        public bool OpenAIAccountFailover { get; set; } = true;
        public bool SummaryStability { get; set; } = true;
        public string ReviewDiffRange { get; set; } = "pr-base";
        public string? Intent { get; set; }
        public bool IntentSet { get; set; }
        public string? Strictness { get; set; }
        public bool StrictnessSet { get; set; }
        public string? VisionPath { get; set; }
        public bool VisionPathSet { get; set; }
        public string Profile { get; set; } = "balanced";
        public string[] MergeBlockerSections { get; set; } = Array.Empty<string>();
        public bool MergeBlockerSectionsSet { get; set; }
        public bool MergeBlockerRequireAllSections { get; set; } = true;
        public bool MergeBlockerRequireAllSectionsSet { get; set; }
        public bool MergeBlockerRequireSectionMatch { get; set; } = true;
        public bool MergeBlockerRequireSectionMatchSet { get; set; }
        public string Mode { get; set; } = "hybrid";
        public string CommentMode { get; set; } = "sticky";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeReviewThreads { get; set; } = true;
        public bool ReviewThreadsIncludeBots { get; set; } = true;
        public int ReviewThreadsMax { get; set; } = 25;
        public int ReviewThreadsMaxComments { get; set; } = 6;
        public bool ReviewThreadsAutoResolveStale { get; set; } = true;
        public string ReviewThreadsAutoResolveDiffRange { get; set; } = "pr-base";
        public int ReviewThreadsAutoResolveMax { get; set; } = 25;
        public bool ReviewThreadsAutoResolveSweepNoBlockers { get; set; } = true;
        public bool ReviewThreadsAutoResolveAIReply { get; set; } = true;
        public bool ReviewUsageSummary { get; set; } = true;
        public int ReviewUsageSummaryCacheMinutes { get; set; } = 10;
        public int ReviewUsageSummaryTimeoutSeconds { get; set; } = 10;
        public bool ReviewUsageBudgetGuard { get; set; } = true;
        public bool ReviewUsageBudgetAllowCredits { get; set; } = true;
        public bool ReviewUsageBudgetAllowWeeklyLimit { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool Diagnostics { get; set; }
        public bool Preflight { get; set; }
        public int PreflightTimeoutSeconds { get; set; } = 15;
        public bool AnalysisEnabled { get; set; }
        public bool AnalysisEnabledSet { get; set; }
        public bool AnalysisGateEnabled { get; set; }
        public bool AnalysisGateEnabledSet { get; set; }
        public bool AnalysisRunStrict { get; set; }
        public bool AnalysisRunStrictSet { get; set; }
        public string[] AnalysisPacks { get; set; } = Array.Empty<string>();
        public bool AnalysisPacksSet { get; set; }

        public static ConfigSettings FromOptions(SetupOptions options) {
            var settings = new ConfigSettings {
                Provider = options.Provider ?? "openai",
                OpenAITransport = options.OpenAITransport ?? "native",
                OpenAIModel = options.OpenAIModel ?? OpenAIModelCatalog.DefaultModel,
                OpenAIAccountId = string.IsNullOrWhiteSpace(options.OpenAIAccountId) ? null : options.OpenAIAccountId!.Trim(),
                OpenAIAccountIdsSet = options.OpenAIAccountIdsSet,
                OpenAIAccountIds = SplitCsv(options.OpenAIAccountIds),
                OpenAIAccountRotation = NormalizeOpenAiAccountRotation(options.OpenAIAccountRotation, strict: true),
                OpenAIAccountFailover = options.OpenAIAccountFailover,
                SummaryStability = true,
                ReviewDiffRange = "pr-base",
                Intent = NormalizeOptionalValue(options.ReviewIntent),
                IntentSet = options.ReviewIntentSet,
                Strictness = NormalizeOptionalValue(options.ReviewStrictness),
                StrictnessSet = options.ReviewStrictnessSet,
                VisionPath = NormalizeOptionalValue(options.ReviewVisionPath),
                VisionPathSet = options.ReviewVisionPathSet,
                Profile = options.ReviewProfile ?? "balanced",
                MergeBlockerSections = SplitCsv(options.MergeBlockerSections),
                MergeBlockerSectionsSet = options.MergeBlockerSectionsSet,
                MergeBlockerRequireAllSections = options.MergeBlockerRequireAllSections,
                MergeBlockerRequireAllSectionsSet = options.MergeBlockerRequireAllSectionsSet,
                MergeBlockerRequireSectionMatch = options.MergeBlockerRequireSectionMatch,
                MergeBlockerRequireSectionMatchSet = options.MergeBlockerRequireSectionMatchSet,
                Mode = options.ReviewMode ?? "hybrid",
                CommentMode = options.ReviewCommentMode ?? "sticky",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeReviewThreads = true,
                ReviewThreadsIncludeBots = true,
                ReviewThreadsMax = 25,
                ReviewThreadsMaxComments = 6,
                ReviewThreadsAutoResolveStale = true,
                ReviewThreadsAutoResolveDiffRange = "pr-base",
                ReviewThreadsAutoResolveMax = 25,
                ReviewThreadsAutoResolveSweepNoBlockers = true,
                ReviewThreadsAutoResolveAIReply = true,
                ReviewUsageSummary = true,
                ReviewUsageSummaryCacheMinutes = 10,
                ReviewUsageSummaryTimeoutSeconds = 10,
                ReviewUsageBudgetGuard = true,
                ReviewUsageBudgetAllowCredits = true,
                ReviewUsageBudgetAllowWeeklyLimit = true,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates,
                Diagnostics = options.Diagnostics,
                Preflight = options.Preflight,
                PreflightTimeoutSeconds = options.PreflightTimeoutSeconds,
                AnalysisEnabled = options.AnalysisEnabled,
                AnalysisEnabledSet = options.AnalysisEnabledSet,
                AnalysisGateEnabled = options.AnalysisGateEnabled,
                AnalysisGateEnabledSet = options.AnalysisGateEnabledSet,
                AnalysisRunStrict = options.AnalysisRunStrict,
                AnalysisRunStrictSet = options.AnalysisRunStrictSet,
                AnalysisPacks = SplitCsv(options.AnalysisPacks),
                AnalysisPacksSet = options.AnalysisPacksSet
            };
            ValidateReviewVisionPathContext(options);
            ApplyReviewLoopPolicy(settings,
                options.ReviewLoopPolicy,
                options.ReviewLoopPolicySet,
                options.ReviewVisionPath,
                options.ReviewVisionPathSet);
            ValidateMergeBlockerSections(settings);
            return settings;
        }

        private static string[] SplitCsv(string? raw) {
            if (string.IsNullOrWhiteSpace(raw)) {
                return Array.Empty<string>();
            }
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string? NormalizeOptionalValue(string? value) {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void ValidateReviewVisionPathContext(SetupOptions options) {
            if (!options.ReviewVisionPathSet) {
                return;
            }
            if (!options.ReviewLoopPolicySet) {
                throw new InvalidOperationException(
                    "--review-vision-path requires --review-loop-policy vision.");
            }
            if (!SetupReviewLoopPolicy.TryNormalize(options.ReviewLoopPolicy, out var normalizedPolicy)) {
                throw new InvalidOperationException(
                    $"Invalid --review-loop-policy value. Use {SetupReviewLoopPolicy.AllowedValuesMessage()}.");
            }
            if (!string.Equals(normalizedPolicy, SetupReviewLoopPolicy.Vision, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    "--review-vision-path is only supported with --review-loop-policy vision.");
            }
        }

        private static void ValidateMergeBlockerSections(ConfigSettings settings) {
            if (!settings.MergeBlockerSectionsSet) {
                return;
            }
            if (settings.MergeBlockerSections.Length > 0) {
                return;
            }
            throw new InvalidOperationException(
                "--merge-blocker-sections requires at least one non-empty section name.");
        }

        private static void ApplyReviewLoopPolicy(
            ConfigSettings settings,
            string? value,
            bool policySet,
            string? visionPath,
            bool visionPathSet) {
            if (!policySet) {
                return;
            }
            if (!SetupReviewLoopPolicy.TryNormalize(value, out var normalized)) {
                throw new InvalidOperationException(
                    $"Invalid --review-loop-policy value. Use {SetupReviewLoopPolicy.AllowedValuesMessage()}.");
            }
            switch (normalized) {
                case SetupReviewLoopPolicy.Strict:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: true,
                        requireSectionMatch: true);
                    return;
                case SetupReviewLoopPolicy.Balanced:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: false,
                        requireSectionMatch: true);
                    return;
                case SetupReviewLoopPolicy.Lenient:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: false,
                        requireSectionMatch: false);
                    return;
                case SetupReviewLoopPolicy.TodoOnly:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list" },
                        requireAllSections: true,
                        requireSectionMatch: true);
                    return;
                case SetupReviewLoopPolicy.Vision:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: false,
                        requireSectionMatch: true);
                    if (!settings.VisionPathSet && !string.IsNullOrWhiteSpace(visionPath)) {
                        settings.VisionPath = visionPath.Trim();
                        settings.VisionPathSet = true;
                    }
                    var visionDefaults = ResolveReviewDefaultsFromVisionDocument(settings.VisionPath, visionPathSet);
                    if (!settings.IntentSet) {
                        settings.Intent = visionDefaults.Intent ?? "maintainability";
                        settings.IntentSet = true;
                    }
                    if (!settings.StrictnessSet) {
                        settings.Strictness = visionDefaults.Strictness ?? "balanced";
                        settings.StrictnessSet = true;
                    }
                    return;
            }
        }

        private static void ApplyMergeBlockerPolicy(
            ConfigSettings settings,
            IReadOnlyList<string> sections,
            bool requireAllSections,
            bool requireSectionMatch) {
            if (!settings.MergeBlockerSectionsSet) {
                settings.MergeBlockerSections = sections.ToArray();
                settings.MergeBlockerSectionsSet = true;
            }
            if (!settings.MergeBlockerRequireAllSectionsSet) {
                settings.MergeBlockerRequireAllSections = requireAllSections;
                settings.MergeBlockerRequireAllSectionsSet = true;
            }
            if (!settings.MergeBlockerRequireSectionMatchSet) {
                settings.MergeBlockerRequireSectionMatch = requireSectionMatch;
                settings.MergeBlockerRequireSectionMatchSet = true;
            }
        }

        private static string NormalizeOpenAiAccountRotation(string? value, bool strict) {
            if (string.IsNullOrWhiteSpace(value)) {
                return "first-available";
            }
            var normalized = value.Trim().ToLowerInvariant() switch {
                "first" or "first-available" or "first_available" or "ordered" => "first-available",
                "round-robin" or "round_robin" or "rr" or "rotate" => "round-robin",
                "sticky" or "pin" or "pinned" => "sticky",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(normalized)) {
                return normalized;
            }
            if (strict) {
                throw new InvalidOperationException(
                    "Invalid --openai-account-rotation value. Use first-available, round-robin, or sticky.");
            }
            return "first-available";
        }
    }
}
