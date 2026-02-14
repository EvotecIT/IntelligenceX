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
    private static FilePlan PlanWorkflowChange(SetupOptions options, string? existingContent) {
        var path = ".github/workflows/review-intelligencex.yml";
        if (string.IsNullOrWhiteSpace(existingContent)) {
            var freshSettings = ResolveWorkflowSettings(options, null);
            var content = BuildWorkflowYaml(freshSettings);
            return PlanWrite(path, existingContent, content, options.Force);
        }

        var allowUpgrade = options.Upgrade || !options.Force;
        if (!allowUpgrade) {
            return FilePlan.Skip(path, "Already exists (use --upgrade or --force)");
        }

        var managedBlock = TryExtractManagedBlock(existingContent);
        if (managedBlock is null && !IsIntelligenceXWorkflow(existingContent) && !options.Force) {
            return FilePlan.Skip(path, "Existing workflow not recognized (use --force to overwrite)");
        }

        var snapshotSource = managedBlock ?? existingContent;
        var settings = ResolveWorkflowSettings(options, snapshotSource);
        var updatedBlock = BuildManagedWorkflowBlock(settings, indent: 2);
        var upgraded = managedBlock is null
            ? BuildWorkflowYaml(settings)
            : ReplaceManagedBlock(existingContent, updatedBlock) ?? BuildWorkflowYaml(settings);
        return PlanWrite(path, existingContent, upgraded, options.Force);
    }

    private static FilePlan PlanConfigChange(SetupOptions options, string? existingReviewerContent, string? seedContent) {
        // Reviewer config is consumed by IntelligenceX.Reviewer at runtime.
        // `.intelligencex/config.json` is reserved for the .NET library/app-server client config.
        var path = ".intelligencex/reviewer.json";
        var overrideContent = ReadConfigOverride(options);
        if (!string.IsNullOrWhiteSpace(overrideContent)) {
            return PlanWrite(path, existingReviewerContent, overrideContent, options.Force);
        }
        var settings = ResolveConfigSettings(options, seedContent, out var parsed);
        if (!parsed && !options.Force && !string.IsNullOrWhiteSpace(seedContent)) {
            return FilePlan.Skip(path, "Config exists but could not be parsed (use --force to overwrite)");
        }

        var content = !string.IsNullOrWhiteSpace(seedContent) && parsed
            ? MergeConfigJson(seedContent, settings)
            : BuildConfigJson(settings);
        return PlanWrite(path, existingReviewerContent, content, options.Force);
    }

    internal static string BuildReviewerConfigJson(string[] args) {
        var options = SetupOptions.Parse(args);
        ValidateAnalysisOptionContextOrThrow(options);
        var plan = PlanConfigChange(options, existingReviewerContent: null, seedContent: null);
        return plan.Content ?? string.Empty;
    }

    // Test helper kept for backward compatibility in existing tests.
    [Obsolete("Use BuildReviewerConfigJson for production and new call sites.")]
    internal static string BuildReviewerConfigJsonForTests(string[] args) => BuildReviewerConfigJson(args);

    // Test helper for merge coverage against existing reviewer.json content.
    internal static string BuildReviewerConfigJsonFromSeedForTests(string[] args, string seedContent) {
        var options = SetupOptions.Parse(args);
        ValidateAnalysisOptionContextOrThrow(options);
        var plan = PlanConfigChange(options, existingReviewerContent: seedContent, seedContent: seedContent);
        return plan.Content ?? string.Empty;
    }

    private static void ValidateAnalysisOptionContextOrThrow(SetupOptions options) {
        if (TryValidateAnalysisOptionContextForCurrentOperation(options, out _, out var analysisOptionError)) {
            return;
        }
        throw new InvalidOperationException(analysisOptionError ?? "Invalid analysis options.");
    }

    // Test helper for workflow upgrade coverage against existing workflow content.
    internal static string BuildWorkflowYamlFromSeedForTests(string[] args, string seedContent) {
        var options = SetupOptions.Parse(args);
        var plan = PlanWorkflowChange(options, seedContent);
        return plan.Content ?? seedContent;
    }

    private static string? ReadConfigOverride(SetupOptions options) {
        if (!string.IsNullOrWhiteSpace(options.ConfigJson)) {
            return options.ConfigJson;
        }
        if (!string.IsNullOrWhiteSpace(options.ConfigPath)) {
            try {
                return File.ReadAllText(options.ConfigPath);
            } catch {
                return null;
            }
        }
        return null;
    }

    private static FilePlan PlanWrite(string path, string? existingContent, string content, bool force) {
        if (string.IsNullOrWhiteSpace(existingContent)) {
            return new FilePlan(path, "create", content);
        }
        if (NormalizeLineEndings(existingContent) == NormalizeLineEndings(content)) {
            return FilePlan.Skip(path, "no changes");
        }
        return new FilePlan(path, force ? "overwrite" : "update", content);
    }

    private static WorkflowSettings ResolveWorkflowSettings(SetupOptions options, string? existingManagedBlock) {
        var settings = WorkflowSettings.FromOptions(options);
        if (string.IsNullOrWhiteSpace(existingManagedBlock)) {
            return settings;
        }

        if (TryReadWorkflowSnapshot(existingManagedBlock, out var snapshot)) {
            if (!options.ActionsRepoSet && !string.IsNullOrWhiteSpace(snapshot.ActionsRepo)) {
                settings.ActionsRepo = NormalizeActionsRepo(snapshot.ActionsRepo!);
            }
            if (!options.ActionsRefSet && !string.IsNullOrWhiteSpace(snapshot.ActionsRef)) {
                settings.ActionsRef = snapshot.ActionsRef!;
            }
            if (!options.RunsOnSet && !string.IsNullOrWhiteSpace(snapshot.RunsOn)) {
                settings.RunsOn = snapshot.RunsOn!;
            }
            if (!options.ReviewerSourceSet && !string.IsNullOrWhiteSpace(snapshot.ReviewerSource)) {
                settings.ReviewerSource = snapshot.ReviewerSource!;
            }
            if (!options.ReviewerReleaseRepoSet && !string.IsNullOrWhiteSpace(snapshot.ReviewerReleaseRepo)) {
                settings.ReviewerReleaseRepo = snapshot.ReviewerReleaseRepo!;
            }
            if (!options.ReviewerReleaseTagSet && !string.IsNullOrWhiteSpace(snapshot.ReviewerReleaseTag)) {
                settings.ReviewerReleaseTag = snapshot.ReviewerReleaseTag!;
            }
            if (!options.ReviewerReleaseAssetSet && snapshot.ReviewerReleaseAsset is not null) {
                settings.ReviewerReleaseAsset = snapshot.ReviewerReleaseAsset;
            }
            if (!options.ReviewerReleaseUrlSet && snapshot.ReviewerReleaseUrl is not null) {
                settings.ReviewerReleaseUrl = snapshot.ReviewerReleaseUrl;
            }
            if (!options.ProviderSet && !string.IsNullOrWhiteSpace(snapshot.Provider)) {
                settings.Provider = snapshot.Provider!;
            }
            if (!options.OpenAIModelSet && !string.IsNullOrWhiteSpace(snapshot.Model)) {
                settings.Model = snapshot.Model!;
            }
            if (!options.OpenAITransportSet && !string.IsNullOrWhiteSpace(snapshot.OpenAITransport)) {
                settings.OpenAITransport = snapshot.OpenAITransport!;
            }
            if (!options.IncludeIssueCommentsSet && snapshot.IncludeIssueComments.HasValue) {
                settings.IncludeIssueComments = snapshot.IncludeIssueComments.Value;
            }
            if (!options.IncludeReviewCommentsSet && snapshot.IncludeReviewComments.HasValue) {
                settings.IncludeReviewComments = snapshot.IncludeReviewComments.Value;
            }
            if (!options.IncludeRelatedPullRequestsSet && snapshot.IncludeRelatedPullRequests.HasValue) {
                settings.IncludeRelatedPullRequests = snapshot.IncludeRelatedPullRequests.Value;
            }
            if (!options.ProgressUpdatesSet && snapshot.ProgressUpdates.HasValue) {
                settings.ProgressUpdates = snapshot.ProgressUpdates.Value;
            }
            if (!options.DiagnosticsSet && snapshot.Diagnostics.HasValue) {
                settings.Diagnostics = snapshot.Diagnostics.Value;
            }
            if (!options.PreflightSet && snapshot.Preflight.HasValue) {
                settings.Preflight = snapshot.Preflight.Value;
            }
            if (!options.PreflightTimeoutSecondsSet && snapshot.PreflightTimeoutSeconds.HasValue) {
                settings.PreflightTimeoutSeconds = snapshot.PreflightTimeoutSeconds.Value;
            }
            if (!options.CleanupEnabledSet && snapshot.CleanupEnabled.HasValue) {
                settings.CleanupEnabled = snapshot.CleanupEnabled.Value;
            }
            if (!options.CleanupModeSet && !string.IsNullOrWhiteSpace(snapshot.CleanupMode)) {
                settings.CleanupMode = snapshot.CleanupMode!;
            }
            if (!options.CleanupScopeSet && !string.IsNullOrWhiteSpace(snapshot.CleanupScope)) {
                settings.CleanupScope = snapshot.CleanupScope!;
            }
            if (!options.CleanupRequireLabelSet && snapshot.CleanupRequireLabel is not null) {
                settings.CleanupRequireLabel = snapshot.CleanupRequireLabel;
            }
            if (!options.CleanupMinConfidenceSet && snapshot.CleanupMinConfidence.HasValue) {
                settings.CleanupMinConfidence = snapshot.CleanupMinConfidence.Value;
            }
            if (!options.CleanupAllowedEditsSet && snapshot.CleanupAllowedEdits is not null) {
                settings.CleanupAllowedEdits = snapshot.CleanupAllowedEdits;
            }
            if (!options.CleanupPostEditCommentSet && snapshot.CleanupPostEditComment.HasValue) {
                settings.CleanupPostEditComment = snapshot.CleanupPostEditComment.Value;
            }
        }

        return settings;
    }

    private static ConfigSettings ResolveConfigSettings(SetupOptions options, string? existingContent, out bool parsed) {
        parsed = false;
        var settings = ConfigSettings.FromOptions(options);
        if (string.IsNullOrWhiteSpace(existingContent)) {
            NormalizeOpenAiAccountRouting(settings);
            return settings;
        }

        if (!TryReadConfigSnapshot(existingContent, out var snapshot)) {
            return settings;
        }

        parsed = true;
        if (!options.ProviderSet && !string.IsNullOrWhiteSpace(snapshot.Provider)) {
            settings.Provider = snapshot.Provider!;
        }
        if (!options.OpenAITransportSet && !string.IsNullOrWhiteSpace(snapshot.OpenAITransport)) {
            settings.OpenAITransport = snapshot.OpenAITransport!;
        }
        if (!options.OpenAIModelSet && !string.IsNullOrWhiteSpace(snapshot.OpenAIModel)) {
            settings.OpenAIModel = snapshot.OpenAIModel!;
        }
        // Precedence: CLI arg (--openai-account-id) > existing config snapshot > environment default.
        if (options.OpenAIAccountIdSet && !string.IsNullOrWhiteSpace(options.OpenAIAccountId)) {
            settings.OpenAIAccountId = options.OpenAIAccountId;
        } else if (!string.IsNullOrWhiteSpace(snapshot.OpenAIAccountId)) {
            settings.OpenAIAccountId = snapshot.OpenAIAccountId;
        }
        // Precedence: CLI arg (--openai-account-ids) > existing config snapshot > environment default.
        if (options.OpenAIAccountIdsSet) {
            settings.OpenAIAccountIds = (options.OpenAIAccountIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } else if (snapshot.OpenAIAccountIds is { Length: > 0 }) {
            settings.OpenAIAccountIds = snapshot.OpenAIAccountIds;
        }
        // Precedence: CLI arg (--openai-account-rotation) > existing config snapshot > environment default.
        if (options.OpenAIAccountRotationSet && !string.IsNullOrWhiteSpace(options.OpenAIAccountRotation)) {
            settings.OpenAIAccountRotation = NormalizeOpenAiAccountRotationStrict(
                options.OpenAIAccountRotation!,
                "--openai-account-rotation");
        } else if (!string.IsNullOrWhiteSpace(snapshot.OpenAIAccountRotation)) {
            settings.OpenAIAccountRotation = NormalizeOpenAiAccountRotationStrict(
                snapshot.OpenAIAccountRotation!,
                "openaiAccountRotation in existing config");
        }
        // Precedence: CLI arg (--openai-account-failover) > existing config snapshot > environment default.
        if (options.OpenAIAccountFailoverSet) {
            settings.OpenAIAccountFailover = options.OpenAIAccountFailover;
        } else if (snapshot.OpenAIAccountFailover.HasValue) {
            settings.OpenAIAccountFailover = snapshot.OpenAIAccountFailover.Value;
        }
        NormalizeOpenAiAccountRouting(settings);
        if (!options.ReviewProfileSet && !string.IsNullOrWhiteSpace(snapshot.Profile)) {
            settings.Profile = snapshot.Profile!;
        }
        if (!options.ReviewModeSet && !string.IsNullOrWhiteSpace(snapshot.Mode)) {
            settings.Mode = snapshot.Mode!;
        }
        if (!options.ReviewCommentModeSet && !string.IsNullOrWhiteSpace(snapshot.CommentMode)) {
            settings.CommentMode = snapshot.CommentMode!;
        }
        if (!options.IncludeIssueCommentsSet && snapshot.IncludeIssueComments.HasValue) {
            settings.IncludeIssueComments = snapshot.IncludeIssueComments.Value;
        }
        if (!options.IncludeReviewCommentsSet && snapshot.IncludeReviewComments.HasValue) {
            settings.IncludeReviewComments = snapshot.IncludeReviewComments.Value;
        }
        if (!options.IncludeRelatedPullRequestsSet && snapshot.IncludeRelatedPullRequests.HasValue) {
            settings.IncludeRelatedPullRequests = snapshot.IncludeRelatedPullRequests.Value;
        }
        if (!options.ProgressUpdatesSet && snapshot.ProgressUpdates.HasValue) {
            settings.ProgressUpdates = snapshot.ProgressUpdates.Value;
        }
        if (!options.DiagnosticsSet && snapshot.Diagnostics.HasValue) {
            settings.Diagnostics = snapshot.Diagnostics.Value;
        }
        if (!options.PreflightSet && snapshot.Preflight.HasValue) {
            settings.Preflight = snapshot.Preflight.Value;
        }
        if (!options.PreflightTimeoutSecondsSet && snapshot.PreflightTimeoutSeconds.HasValue) {
            settings.PreflightTimeoutSeconds = snapshot.PreflightTimeoutSeconds.Value;
        }
        if (!options.AnalysisEnabledSet && snapshot.AnalysisEnabled.HasValue) {
            settings.AnalysisEnabled = snapshot.AnalysisEnabled.Value;
        }
        if (!options.AnalysisGateEnabledSet && snapshot.AnalysisGateEnabled.HasValue) {
            settings.AnalysisGateEnabled = snapshot.AnalysisGateEnabled.Value;
        }
        if (!options.AnalysisRunStrictSet && snapshot.AnalysisRunStrict.HasValue) {
            settings.AnalysisRunStrict = snapshot.AnalysisRunStrict.Value;
        }
        if (!options.AnalysisPacksSet && snapshot.AnalysisPacks is { Length: > 0 }) {
            settings.AnalysisPacks = snapshot.AnalysisPacks;
        }

        return settings;
    }

    private static void NormalizeOpenAiAccountRouting(ConfigSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
            return;
        }
        settings.OpenAIAccountId = settings.OpenAIAccountId!.Trim();
        if (settings.OpenAIAccountId.Length == 0) {
            settings.OpenAIAccountId = null;
            return;
        }

        if (settings.OpenAIAccountIds.Length == 0) {
            return;
        }

        settings.OpenAIAccountIds = new[] { settings.OpenAIAccountId! }
            .Concat(settings.OpenAIAccountIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeOpenAiAccountRotationStrict(string value, string sourceLabel) {
        var normalized = value.Trim().ToLowerInvariant() switch {
            "first" or "first-available" or "first_available" or "ordered" => "first-available",
            "round-robin" or "round_robin" or "rr" or "rotate" => "round-robin",
            "sticky" or "pin" or "pinned" => "sticky",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(normalized)) {
            return normalized;
        }
        throw new InvalidOperationException(
            $"Invalid {sourceLabel} value. Use first-available, round-robin, or sticky.");
    }

    private static string BuildConfigJson(ConfigSettings settings) {
        var root = new JsonObject {
            ["review"] = new JsonObject {
                ["provider"] = settings.Provider,
                ["openaiTransport"] = settings.OpenAITransport,
                ["model"] = settings.OpenAIModel,
                ["profile"] = settings.Profile,
                ["mode"] = settings.Mode,
                ["commentMode"] = settings.CommentMode,
                ["includeIssueComments"] = settings.IncludeIssueComments,
                ["includeReviewComments"] = settings.IncludeReviewComments,
                ["includeRelatedPrs"] = settings.IncludeRelatedPullRequests,
                ["progressUpdates"] = settings.ProgressUpdates,
                ["diagnostics"] = settings.Diagnostics,
                ["preflight"] = settings.Preflight,
                ["preflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds
            }
        };
        if (!string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
            ((JsonObject)root["review"]!)["openaiAccountId"] = settings.OpenAIAccountId;
        }
        if (settings.OpenAIAccountIds.Length > 0) {
            var accountIds = new JsonArray();
            foreach (var accountId in settings.OpenAIAccountIds) {
                accountIds.Add(accountId);
            }
            var review = (JsonObject)root["review"]!;
            review["openaiAccountIds"] = accountIds;
            review["openaiAccountRotation"] = settings.OpenAIAccountRotation;
            review["openaiAccountFailover"] = settings.OpenAIAccountFailover;
        } else if (!string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
            var review = (JsonObject)root["review"]!;
            review["openaiAccountRotation"] = settings.OpenAIAccountRotation;
            review["openaiAccountFailover"] = settings.OpenAIAccountFailover;
        }
        if (settings.AnalysisEnabledSet || settings.AnalysisGateEnabledSet || settings.AnalysisPacksSet ||
            settings.AnalysisRunStrictSet) {
            SetupAnalysisConfig.Apply(root,
                enabledSet: settings.AnalysisEnabledSet, enabled: settings.AnalysisEnabled,
                gateEnabledSet: settings.AnalysisGateEnabledSet, gateEnabled: settings.AnalysisGateEnabled,
                packsSet: settings.AnalysisPacksSet, packs: settings.AnalysisPacks,
                runStrictSet: settings.AnalysisRunStrictSet, runStrict: settings.AnalysisRunStrict);
        } else if (settings.AnalysisEnabled) {
            // Backwards-compatible behavior for future defaults where analysis might be enabled without a set-flag.
            root["analysis"] = SetupAnalysisConfig.Build(enabled: true, gateEnabled: settings.AnalysisGateEnabled,
                packs: settings.AnalysisPacks, runStrict: settings.AnalysisRunStrict);
        }

        return root.ToJsonString(CliJson.Indented);
    }

}
