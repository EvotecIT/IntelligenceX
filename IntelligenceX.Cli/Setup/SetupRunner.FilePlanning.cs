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
        var plan = PlanConfigChange(options, existingReviewerContent: null, seedContent: null);
        return plan.Content ?? string.Empty;
    }

    // Test helper kept for backward compatibility in existing tests.
    [Obsolete("Use BuildReviewerConfigJson for production and new call sites.")]
    internal static string BuildReviewerConfigJsonForTests(string[] args) => BuildReviewerConfigJson(args);

    // Test helper for merge coverage against existing reviewer.json content.
    internal static string BuildReviewerConfigJsonFromSeedForTests(string[] args, string seedContent) {
        var options = SetupOptions.Parse(args);
        var plan = PlanConfigChange(options, existingReviewerContent: seedContent, seedContent: seedContent);
        return plan.Content ?? string.Empty;
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
                ["includeRelatedPullRequests"] = settings.IncludeRelatedPullRequests,
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
        if (settings.AnalysisEnabledSet || settings.AnalysisGateEnabledSet || settings.AnalysisPacksSet) {
            SetupAnalysisConfig.Apply(root,
                enabledSet: settings.AnalysisEnabledSet, enabled: settings.AnalysisEnabled,
                gateEnabledSet: settings.AnalysisGateEnabledSet, gateEnabled: settings.AnalysisGateEnabled,
                packsSet: settings.AnalysisPacksSet, packs: settings.AnalysisPacks);
        } else if (settings.AnalysisEnabled) {
            // Backwards-compatible behavior for future defaults where analysis might be enabled without a set-flag.
            root["analysis"] = SetupAnalysisConfig.Build(enabled: true, gateEnabled: settings.AnalysisGateEnabled,
                packs: settings.AnalysisPacks);
        }

        return root.ToJsonString(CliJson.Indented);
    }

    private static string MergeConfigJson(string existingContent, ConfigSettings settings) {
        var node = JsonNode.Parse(existingContent) as JsonObject ?? new JsonObject();
        var review = node["review"] as JsonObject ?? new JsonObject();
        review["provider"] = settings.Provider;
        review["openaiTransport"] = settings.OpenAITransport;
        review["model"] = settings.OpenAIModel;
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
        } else if (settings.OpenAIAccountIdsSet && string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
            review.Remove("openaiAccountIds");
            review.Remove("openaiAccountRotation");
            review.Remove("openaiAccountFailover");
        } else if (!string.IsNullOrWhiteSpace(settings.OpenAIAccountId)) {
            review["openaiAccountRotation"] = settings.OpenAIAccountRotation;
            review["openaiAccountFailover"] = settings.OpenAIAccountFailover;
        }
        review["profile"] = settings.Profile;
        review["mode"] = settings.Mode;
        review["commentMode"] = settings.CommentMode;
        review["includeIssueComments"] = settings.IncludeIssueComments;
        review["includeReviewComments"] = settings.IncludeReviewComments;
        review["includeRelatedPullRequests"] = settings.IncludeRelatedPullRequests;
        review["progressUpdates"] = settings.ProgressUpdates;
        review["diagnostics"] = settings.Diagnostics;
        review["preflight"] = settings.Preflight;
        review["preflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds;
        node["review"] = review;
        if (settings.AnalysisEnabledSet || settings.AnalysisGateEnabledSet || settings.AnalysisPacksSet) {
            SetupAnalysisConfig.Apply(node,
                enabledSet: settings.AnalysisEnabledSet, enabled: settings.AnalysisEnabled,
                gateEnabledSet: settings.AnalysisGateEnabledSet, gateEnabled: settings.AnalysisGateEnabled,
                packsSet: settings.AnalysisPacksSet, packs: settings.AnalysisPacks);
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
            ["ActionsRepo"] = settings.ActionsRepo,
            ["ActionsRef"] = settings.ActionsRef,
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
        var trimmed = string.IsNullOrWhiteSpace(value) ? "[\"self-hosted\",\"ubuntu\"]" : value.Trim();
        if (trimmed.StartsWith("'") && trimmed.EndsWith("'")) {
            return trimmed;
        }
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) {
            return trimmed;
        }
        return $"'{trimmed}'";
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
        var match = Regex.Match(content, @"^\s*uses:\s*([^\s@]+)@([^\s]+)\s*$", RegexOptions.Multiline);
        if (match.Success) {
            snapshot.ActionsRepo = NormalizeActionsRepo(match.Groups[1].Value.Trim());
            snapshot.ActionsRef = match.Groups[2].Value.Trim();
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
                snapshot.Profile = ReadJsonString(review, "profile");
                snapshot.Mode = ReadJsonString(review, "mode");
                snapshot.CommentMode = ReadJsonString(review, "commentMode");
                snapshot.IncludeIssueComments = ReadJsonBool(review, "includeIssueComments");
                snapshot.IncludeReviewComments = ReadJsonBool(review, "includeReviewComments");
                snapshot.IncludeRelatedPullRequests = ReadJsonBool(review, "includeRelatedPullRequests");
                snapshot.ProgressUpdates = ReadJsonBool(review, "progressUpdates");
                snapshot.Diagnostics = ReadJsonBool(review, "diagnostics");
                snapshot.Preflight = ReadJsonBool(review, "preflight");
                snapshot.PreflightTimeoutSeconds = ReadJsonInt(review, "preflightTimeoutSeconds");
            }
            var analysis = root["analysis"] as JsonObject;
            if (analysis is not null) {
                snapshot.AnalysisEnabled = ReadJsonBool(analysis, "enabled");
                snapshot.AnalysisPacks = SetupAnalysisConfig.ReadStringArray(analysis, "packs");
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
