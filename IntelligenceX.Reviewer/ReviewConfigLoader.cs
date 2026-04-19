using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Analysis;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
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
            settings.Provider = ReviewProviderContracts.ParseProviderOrThrow(provider, "review.provider");
        }
        var providerFallback = reviewObj.GetString("providerFallback");
        if (!string.IsNullOrWhiteSpace(providerFallback)) {
            settings.ProviderFallback = ReviewProviderContracts.ParseProviderOrThrow(providerFallback, "review.providerFallback");
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
        ApplyHistory(reviewObj, settings);
        ApplyCiContext(reviewObj, settings);
        ApplySwarm(reviewObj, settings);
        ApplyCodex(root, settings);
        ApplyOpenAiCompatible(reviewObj, settings);
        ApplyAnthropic(reviewObj, settings);
        ApplyCopilot(root, settings);
        ApplyAzureDevOps(reviewObj, settings);
        ApplyCleanup(root, settings);
        AnalysisConfigReader.Apply(root, reviewObj, settings.Analysis);
        ApplyAgentProfiles(reviewObj, settings);
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
        var narrativeMode = obj.GetString("narrativeMode");
        if (!string.IsNullOrWhiteSpace(narrativeMode)) {
            settings.NarrativeMode =
                ReviewSettings.NormalizeNarrativeMode(narrativeMode, settings.NarrativeMode);
        }
        settings.Persona = obj.GetString("persona") ?? settings.Persona;
        settings.Notes = obj.GetString("notes") ?? settings.Notes;
        settings.Model = obj.GetString("model") ?? obj.GetString("openaiModel") ?? obj.GetString("openAiModel") ?? settings.Model;
        var openAiTransport = obj.GetString("openaiTransport") ?? obj.GetString("openAiTransport") ?? obj.GetString("openai_transport");
        if (!string.IsNullOrWhiteSpace(openAiTransport)) {
            settings.OpenAITransport = ParseOpenAiTransport(openAiTransport, settings.OpenAITransport);
        }
        settings.OpenAiAccountId =
            obj.GetString("openaiAccountId")
            ?? obj.GetString("openAiAccountId")
            ?? obj.GetString("authAccountId")
            ?? settings.OpenAiAccountId;
        var openAiAccountRotation = obj.GetString("openaiAccountRotation")
            ?? obj.GetString("openAiAccountRotation");
        if (!string.IsNullOrWhiteSpace(openAiAccountRotation)) {
            settings.OpenAiAccountRotation =
                ReviewSettings.NormalizeOpenAiAccountRotation(openAiAccountRotation, settings.OpenAiAccountRotation);
        }
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
        settings.AgentProfile = obj.GetString("agentProfile") ?? obj.GetString("modelProfile") ?? settings.AgentProfile;
    }

    private static OpenAITransportKind ParseOpenAiTransport(string value, OpenAITransportKind fallback) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => fallback
        };
    }

    private static void ApplyLists(JsonObject obj, ReviewSettings settings) {
        var focus = ReadStringList(obj, "focus");
        if (focus is not null) {
            settings.Focus = focus;
        }

        var mergeBlockerSections = ReadStringList(obj, "mergeBlockerSections");
        if (mergeBlockerSections is not null) {
            settings.MergeBlockerSections = ReviewSettings.NormalizeMergeBlockerSections(mergeBlockerSections);
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

        var generatedFileGlobs = ReadStringList(obj, "generatedFileGlobs");
        if (generatedFileGlobs is not null) {
            settings.GeneratedFileGlobs = generatedFileGlobs;
        }

        var includePaths = ReadStringList(obj, "includePaths");
        if (includePaths is not null) {
            settings.IncludePaths = includePaths;
        }
        var openAiAccountIds = ReadStringList(obj, "openaiAccountIds");
        if (openAiAccountIds is not null) {
            settings.OpenAiAccountIds = ReviewSettings.NormalizeAccountIdList(openAiAccountIds);
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
        settings.MaxFiles = ReadNonNegativeInt(obj, "maxFiles", settings.MaxFiles);
        settings.MaxPatchChars = ReadNonNegativeInt(obj, "maxPatchChars", settings.MaxPatchChars);
        settings.MaxInlineComments = ReadNonNegativeInt(obj, "maxInlineComments", settings.MaxInlineComments);
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
        settings.ProviderHealthCheckTimeoutSeconds = Math.Max(1,
            ReadInt(obj, "providerHealthCheckTimeoutSeconds", settings.ProviderHealthCheckTimeoutSeconds));
        settings.ProviderCircuitBreakerFailures = Math.Max(0,
            ReadInt(obj, "providerCircuitBreakerFailures", settings.ProviderCircuitBreakerFailures));
        settings.ProviderCircuitBreakerOpenSeconds = Math.Max(1,
            ReadInt(obj, "providerCircuitBreakerOpenSeconds", settings.ProviderCircuitBreakerOpenSeconds));
        settings.PreflightTimeoutSeconds = ReadInt(obj, "preflightTimeoutSeconds", settings.PreflightTimeoutSeconds);
        settings.ReviewUsageSummaryCacheMinutes = Math.Max(0,
            ReadInt(obj, "reviewUsageSummaryCacheMinutes", settings.ReviewUsageSummaryCacheMinutes));
        settings.ReviewUsageSummaryTimeoutSeconds = Math.Max(1,
            ReadInt(obj, "reviewUsageSummaryTimeoutSeconds", settings.ReviewUsageSummaryTimeoutSeconds));
    }

    private static void ApplyBooleans(JsonObject obj, ReviewSettings settings) {
        settings.IncludeNextSteps = ReadBool(obj, "includeNextSteps", settings.IncludeNextSteps);
        settings.MergeBlockerRequireAllSections =
            ReadBool(obj, "mergeBlockerRequireAllSections", settings.MergeBlockerRequireAllSections);
        settings.MergeBlockerRequireSectionMatch =
            ReadBool(obj, "mergeBlockerRequireSectionMatch", settings.MergeBlockerRequireSectionMatch);
        settings.IncludeLanguageHints = ReadBool(obj, "languageHints", settings.IncludeLanguageHints);
        settings.ReviewBudgetSummary = ReadBool(obj, "reviewBudgetSummary", settings.ReviewBudgetSummary);
        settings.OverwriteSummary = ReadBool(obj, "overwriteSummary", settings.OverwriteSummary);
        settings.OverwriteSummaryOnNewCommit = ReadBool(obj, "overwriteSummaryOnNewCommit", settings.OverwriteSummaryOnNewCommit);
        settings.SummaryStability = ReadBool(obj, "summaryStability", settings.SummaryStability);
        settings.SkipDraft = ReadBool(obj, "skipDraft", settings.SkipDraft);
        settings.SkipBinaryFiles = ReadBool(obj, "skipBinaryFiles", settings.SkipBinaryFiles);
        settings.SkipGeneratedFiles = ReadBool(obj, "skipGeneratedFiles", settings.SkipGeneratedFiles);
        settings.AllowWorkflowChanges = ReadBool(obj, "allowWorkflowChanges", settings.AllowWorkflowChanges);
        settings.SecretsAudit = ReadBool(obj, "secretsAudit", settings.SecretsAudit);
        settings.RedactPii = ReadBool(obj, "redactPii", settings.RedactPii);
        settings.ProgressUpdates = ReadBool(obj, "progressUpdates", settings.ProgressUpdates);
        settings.Diagnostics = ReadBool(obj, "diagnostics", settings.Diagnostics);
        settings.Preflight = ReadBool(obj, "preflight", settings.Preflight);
        settings.RetryExtraOnResponseEnded = ReadBool(obj, "retryExtraResponseEnded", settings.RetryExtraOnResponseEnded);
        settings.ProviderHealthChecks = ReadBool(obj, "providerHealthChecks", settings.ProviderHealthChecks);
        settings.FailOpen = ReadBool(obj, "failOpen", settings.FailOpen);
        settings.FailOpenTransientOnly = ReadBool(obj, "failOpenTransientOnly", settings.FailOpenTransientOnly);
        settings.ReviewUsageSummary = ReadBool(obj, "reviewUsageSummary", settings.ReviewUsageSummary);
        settings.OpenAiAccountFailover = ReadBool(obj, "openaiAccountFailover", settings.OpenAiAccountFailover);
        settings.ReviewUsageBudgetGuard = ReadBool(obj, "reviewUsageBudgetGuard", settings.ReviewUsageBudgetGuard);
        settings.ReviewUsageBudgetAllowCredits =
            ReadBool(obj, "reviewUsageBudgetAllowCredits", settings.ReviewUsageBudgetAllowCredits);
        settings.ReviewUsageBudgetAllowWeeklyLimit =
            ReadBool(obj, "reviewUsageBudgetAllowWeeklyLimit", settings.ReviewUsageBudgetAllowWeeklyLimit);
        settings.StructuredFindings = ReadBool(obj, "structuredFindings", settings.StructuredFindings);
        settings.TriageOnly = ReadBool(obj, "triageOnly", settings.TriageOnly);
        settings.UntrustedPrAllowSecrets = ReadBool(obj, "untrustedPrAllowSecrets", settings.UntrustedPrAllowSecrets);
        settings.UntrustedPrAllowWrites = ReadBool(obj, "untrustedPrAllowWrites", settings.UntrustedPrAllowWrites);
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
        settings.ReviewThreadsAutoResolveRequireEvidence = ReadBool(obj, "reviewThreadsAutoResolveRequireEvidence",
            settings.ReviewThreadsAutoResolveRequireEvidence);
        settings.ReviewThreadsAutoResolveSweepNoBlockers = ReadBool(obj, "reviewThreadsAutoResolveSweepNoBlockers",
            settings.ReviewThreadsAutoResolveSweepNoBlockers);
        settings.ReviewThreadsAutoResolveAIPostComment = ReadBool(obj, "reviewThreadsAutoResolveAIPostComment", settings.ReviewThreadsAutoResolveAIPostComment);
        settings.ReviewThreadsAutoResolveAIEmbed = ReadBool(obj, "reviewThreadsAutoResolveAIEmbed", settings.ReviewThreadsAutoResolveAIEmbed);
        var embedPlacement = obj.GetString("reviewThreadsAutoResolveAIEmbedPlacement");
        if (!string.IsNullOrWhiteSpace(embedPlacement)) {
            settings.ReviewThreadsAutoResolveAIEmbedPlacement =
                ReviewSettings.NormalizeEmbedPlacement(embedPlacement, settings.ReviewThreadsAutoResolveAIEmbedPlacement);
        }
        settings.ReviewThreadsAutoResolveAISummary = ReadBool(obj, "reviewThreadsAutoResolveAISummary", settings.ReviewThreadsAutoResolveAISummary);
        settings.ReviewThreadsAutoResolveSummaryAlways = ReadBool(obj, "reviewThreadsAutoResolveSummaryAlways",
            settings.ReviewThreadsAutoResolveSummaryAlways);
        settings.ReviewThreadsAutoResolveAIReply = ReadBool(obj, "reviewThreadsAutoResolveAIReply", settings.ReviewThreadsAutoResolveAIReply);
        settings.ReviewThreadsAutoResolveSummaryComment = ReadBool(obj, "reviewThreadsAutoResolveSummaryComment",
            settings.ReviewThreadsAutoResolveSummaryComment);
        settings.MaxCommentChars = ReadNonNegativeInt(obj, "maxCommentChars", settings.MaxCommentChars);
        settings.MaxComments = ReadNonNegativeInt(obj, "maxComments", settings.MaxComments);
        settings.CommentSearchLimit = ReadInt(obj, "commentSearchLimit", settings.CommentSearchLimit);
        settings.GitHubMaxConcurrency = Math.Max(1, ReadInt(obj, "githubMaxConcurrency", settings.GitHubMaxConcurrency));
        settings.ContextDenyEnabled = ReadBool(obj, "contextDenyEnabled", settings.ContextDenyEnabled);
        var contextDenyPatterns = ReadStringList(obj, "contextDenyPatterns");
        if (contextDenyPatterns is not null) {
            settings.ContextDenyPatterns = contextDenyPatterns;
        }
        var includeRelatedPrs = ReadBool(obj, "includeRelatedPullRequests", settings.IncludeRelatedPrs);
        settings.IncludeRelatedPrs = ReadBool(obj, "includeRelatedPrs", includeRelatedPrs);
        settings.RelatedPrsQuery = obj.GetString("relatedPrsQuery") ?? settings.RelatedPrsQuery;
        settings.MaxRelatedPrs = ReadInt(obj, "maxRelatedPrs", settings.MaxRelatedPrs);
    }

    private static void ApplyCiContext(JsonObject reviewObj, ReviewSettings settings) {
        var ciContext = reviewObj.GetObject("ciContext");
        if (ciContext is null) {
            return;
        }

        settings.CiContext.Enabled = ReadBool(ciContext, "enabled", settings.CiContext.Enabled);
        settings.CiContext.IncludeCheckSummary =
            ReadBool(ciContext, "includeCheckSummary", settings.CiContext.IncludeCheckSummary);
        settings.CiContext.IncludeFailedRuns =
            ReadBool(ciContext, "includeFailedRuns", settings.CiContext.IncludeFailedRuns);
        settings.CiContext.IncludeFailureSnippets = ReviewSettings.NormalizeCiContextFailureSnippets(
            ciContext.GetString("includeFailureSnippets"),
            settings.CiContext.IncludeFailureSnippets);
        settings.CiContext.MaxFailedRuns =
            ReadNonNegativeInt(ciContext, "maxFailedRuns", settings.CiContext.MaxFailedRuns);
        settings.CiContext.MaxSnippetCharsPerRun =
            ReadNonNegativeInt(ciContext, "maxSnippetCharsPerRun", settings.CiContext.MaxSnippetCharsPerRun);
        settings.CiContext.ClassifyInfraFailures =
            ReadBool(ciContext, "classifyInfraFailures", settings.CiContext.ClassifyInfraFailures);
    }

    private static void ApplyHistory(JsonObject reviewObj, ReviewSettings settings) {
        var history = reviewObj.GetObject("history");
        if (history is null) {
            return;
        }

        settings.History.Enabled = ReadBool(history, "enabled", settings.History.Enabled);
        settings.History.IncludeIxSummaryHistory =
            ReadBool(history, "includeIxSummaryHistory", settings.History.IncludeIxSummaryHistory);
        settings.History.IncludeReviewThreads =
            ReadBool(history, "includeReviewThreads", settings.History.IncludeReviewThreads);
        settings.History.IncludeExternalBotSummaries =
            ReadBool(history, "includeExternalBotSummaries", settings.History.IncludeExternalBotSummaries);
        var externalBotLogins = ReadStringList(history, "externalBotLogins");
        if (externalBotLogins is not null) {
            settings.History.ExternalBotLogins = externalBotLogins;
        }
        settings.History.Artifacts = ReadBool(history, "artifacts", settings.History.Artifacts);
        settings.History.MaxRounds = Math.Max(0, ReadNonNegativeInt(history, "maxRounds", settings.History.MaxRounds));
        settings.History.MaxItems = Math.Max(0, ReadNonNegativeInt(history, "maxItems", settings.History.MaxItems));
    }

    private static void ApplySwarm(JsonObject reviewObj, ReviewSettings settings) {
        var swarm = reviewObj.GetObject("swarm");
        if (swarm is null) {
            return;
        }

        settings.Swarm.Enabled = ReadBool(swarm, "enabled", settings.Swarm.Enabled);
        settings.Swarm.ShadowMode = ReadBool(swarm, "shadowMode", settings.Swarm.ShadowMode);
        ApplySwarmReviewers(swarm, settings);
        settings.Swarm.MaxParallel = Math.Max(1, ReadInt(swarm, "maxParallel", settings.Swarm.MaxParallel));
        settings.Swarm.PublishSubreviews =
            ReadBool(swarm, "publishSubreviews", settings.Swarm.PublishSubreviews);
        settings.Swarm.AggregatorModel = swarm.GetString("aggregatorModel") ?? settings.Swarm.AggregatorModel;
        if (!string.IsNullOrWhiteSpace(settings.Swarm.AggregatorModel)) {
            settings.Swarm.Aggregator.Model = settings.Swarm.AggregatorModel;
        }
        ApplySwarmAggregator(swarm, settings);
        settings.Swarm.FailOpenOnPartial =
            ReadBool(swarm, "failOpenOnPartial", settings.Swarm.FailOpenOnPartial);
        settings.Swarm.Metrics = ReadBool(swarm, "metrics", settings.Swarm.Metrics);
    }

    private static void ApplySwarmReviewers(JsonObject swarm, ReviewSettings settings) {
        if (!swarm.TryGetValue("reviewers", out var reviewersValue) || reviewersValue is null) {
            return;
        }

        var reviewersArray = reviewersValue.AsArray();
        if (reviewersArray is null) {
            return;
        }

        var reviewerIds = new List<string>();
        var reviewerSettings = new List<ReviewSwarmReviewerSettings>();
        foreach (var item in reviewersArray) {
            var stringValue = item.AsString();
            if (!string.IsNullOrWhiteSpace(stringValue)) {
                reviewerIds.Add(stringValue!);
                reviewerSettings.Add(new ReviewSwarmReviewerSettings {
                    Id = stringValue!.Trim()
                });
                continue;
            }

            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }

            var reviewer = ParseSwarmReviewerObject(obj);
            if (reviewer is null) {
                continue;
            }

            reviewerIds.Add(reviewer.Id);
            reviewerSettings.Add(reviewer);
        }

        settings.Swarm.Reviewers = ReviewSettings.NormalizeSwarmReviewers(reviewerIds, settings.Swarm.Reviewers);
        settings.Swarm.ReviewerSettings =
            ReviewSettings.NormalizeSwarmReviewerSettings(reviewerSettings, settings.Swarm.ReviewerSettings);
    }

    private static ReviewSwarmReviewerSettings? ParseSwarmReviewerObject(JsonObject obj) {
        var id = obj.GetString("id");
        if (string.IsNullOrWhiteSpace(id)) {
            return null;
        }

        return new ReviewSwarmReviewerSettings {
            Id = id!,
            AgentProfile = obj.GetString("agentProfile") ?? obj.GetString("modelProfile"),
            Provider = ParseOptionalSwarmProvider(obj.GetString("provider")),
            Model = obj.GetString("model"),
            ReasoningEffort = ParseOptionalReasoningEffort(obj.GetString("reasoningEffort"))
        };
    }

    private static void ApplySwarmAggregator(JsonObject swarm, ReviewSettings settings) {
        var aggregator = swarm.GetObject("aggregator");
        if (aggregator is null) {
            return;
        }

        settings.Swarm.Aggregator.Provider =
            ParseOptionalSwarmProvider(aggregator.GetString("provider")) ?? settings.Swarm.Aggregator.Provider;
        settings.Swarm.Aggregator.AgentProfile =
            aggregator.GetString("agentProfile") ?? aggregator.GetString("modelProfile") ?? settings.Swarm.Aggregator.AgentProfile;
        settings.Swarm.Aggregator.Model = aggregator.GetString("model") ?? settings.Swarm.Aggregator.Model;
        settings.Swarm.Aggregator.ReasoningEffort =
            ParseOptionalReasoningEffort(aggregator.GetString("reasoningEffort")) ?? settings.Swarm.Aggregator.ReasoningEffort;
        if (!string.IsNullOrWhiteSpace(settings.Swarm.Aggregator.Model)) {
            settings.Swarm.AggregatorModel = settings.Swarm.Aggregator.Model;
        }
    }

    private static ReviewProvider? ParseOptionalSwarmProvider(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return ReviewProviderContracts.ParseProviderOrThrow(value, "review.swarm.provider");
    }

    private static IntelligenceX.OpenAI.Chat.ReasoningEffort? ParseOptionalReasoningEffort(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseReasoningEffort(value);
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

    private static void ApplyOpenAiCompatible(JsonObject reviewObj, ReviewSettings settings) {
        var openAi = reviewObj.GetObject("openaiCompatible") ?? reviewObj.GetObject("openAiCompatible");
        if (openAi is null) {
            return;
        }
        settings.OpenAICompatibleBaseUrl = openAi.GetString("baseUrl") ?? settings.OpenAICompatibleBaseUrl;
        settings.OpenAICompatibleApiKeyEnv = openAi.GetString("apiKeyEnv") ?? settings.OpenAICompatibleApiKeyEnv;
        settings.OpenAICompatibleApiKey = openAi.GetString("apiKey") ?? settings.OpenAICompatibleApiKey;
        settings.OpenAICompatibleTimeoutSeconds = ReadInt(openAi, "timeoutSeconds", settings.OpenAICompatibleTimeoutSeconds);
        settings.OpenAICompatibleAllowInsecureHttp = ReadBool(openAi, "allowInsecureHttp", settings.OpenAICompatibleAllowInsecureHttp);
        settings.OpenAICompatibleAllowInsecureHttpNonLoopback = ReadBool(openAi, "allowInsecureHttpNonLoopback", settings.OpenAICompatibleAllowInsecureHttpNonLoopback);
        settings.OpenAICompatibleDropAuthorizationOnRedirect = ReadBool(openAi, "dropAuthorizationOnRedirect", settings.OpenAICompatibleDropAuthorizationOnRedirect);
    }

    private static void ApplyAnthropic(JsonObject reviewObj, ReviewSettings settings) {
        var anthropic = reviewObj.GetObject("anthropic") ?? reviewObj.GetObject("claude");
        if (anthropic is null) {
            return;
        }

        settings.AnthropicBaseUrl = anthropic.GetString("baseUrl") ?? settings.AnthropicBaseUrl;
        settings.AnthropicVersion = anthropic.GetString("version") ?? settings.AnthropicVersion;
        settings.AnthropicApiKeyEnv = anthropic.GetString("apiKeyEnv") ?? settings.AnthropicApiKeyEnv;
        settings.AnthropicApiKey = anthropic.GetString("apiKey") ?? settings.AnthropicApiKey;
        settings.AnthropicTimeoutSeconds = ReadInt(anthropic, "timeoutSeconds", settings.AnthropicTimeoutSeconds);
        settings.AnthropicMaxTokens = ReadInt(anthropic, "maxTokens", settings.AnthropicMaxTokens);
    }

    private static void ApplyCopilot(JsonObject root, ReviewSettings settings) {
        var copilot = root.GetObject("copilot");
        if (copilot is null) {
            return;
        }
        settings.CopilotCliPath = copilot.GetString("cliPath") ?? settings.CopilotCliPath;
        settings.CopilotCliUrl = copilot.GetString("cliUrl") ?? settings.CopilotCliUrl;
        settings.CopilotWorkingDirectory = copilot.GetString("workingDirectory") ?? settings.CopilotWorkingDirectory;
        settings.CopilotModel = copilot.GetString("model") ?? settings.CopilotModel;
        settings.CopilotLauncher = ReviewSettings.NormalizeCopilotLauncher(copilot.GetString("launcher"),
            settings.CopilotLauncher);
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

    private static void ApplyAgentProfiles(JsonObject reviewObj, ReviewSettings settings) {
        var profiles = reviewObj.GetObject("agentProfiles")
            ?? reviewObj.GetObject("modelProfiles")
            ?? reviewObj.GetObject("authProfiles");
        if (profiles is not null) {
            var map = new Dictionary<string, ReviewAgentProfileSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in profiles) {
                var obj = entry.Value.AsObject();
                if (obj is null || string.IsNullOrWhiteSpace(entry.Key)) {
                    continue;
                }
                var profile = ReadAgentProfile(entry.Key, obj);
                map[profile.Id] = profile;
            }
            settings.AgentProfiles = map;
        }

        if (!string.IsNullOrWhiteSpace(settings.AgentProfile)) {
            settings.ApplyAgentProfile(settings.AgentProfile);
        }
    }

    private static ReviewAgentProfileSettings ReadAgentProfile(string id, JsonObject obj) {
        var profile = new ReviewAgentProfileSettings {
            Id = id.Trim(),
            Model = obj.GetString("model"),
            Authenticator = obj.GetString("authenticator") ?? obj.GetString("auth"),
            OpenAiAccountId =
                obj.GetString("openaiAccountId")
                ?? obj.GetString("openAiAccountId")
                ?? obj.GetString("authAccountId")
        };

        var provider = obj.GetString("provider");
        if (!string.IsNullOrWhiteSpace(provider)) {
            profile.Provider = ReviewProviderContracts.ParseProviderOrThrow(provider, $"review.agentProfiles.{id}.provider");
        }

        var reasoningEffort = obj.GetString("reasoningEffort");
        if (!string.IsNullOrWhiteSpace(reasoningEffort)) {
            profile.ReasoningEffort = IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseReasoningEffort(reasoningEffort);
        }

        var openAiTransport = obj.GetString("openaiTransport") ?? obj.GetString("openAiTransport");
        if (!string.IsNullOrWhiteSpace(openAiTransport)) {
            profile.OpenAITransport = ParseOpenAiTransport(openAiTransport, OpenAITransportKind.AppServer);
        }

        var resolvedProvider = profile.ResolveProvider();
        ApplyAgentProfileCopilot(obj.GetObject("copilot") ?? obj, profile);
        var openAiCompatible = obj.GetObject("openaiCompatible") ?? obj.GetObject("openAiCompatible");
        if (openAiCompatible is not null || resolvedProvider == ReviewProvider.OpenAICompatible) {
            ApplyAgentProfileOpenAiCompatible(openAiCompatible ?? obj, profile);
        }
        var anthropic = obj.GetObject("anthropic") ?? obj.GetObject("claude");
        if (anthropic is not null || resolvedProvider == ReviewProvider.Claude) {
            ApplyAgentProfileAnthropic(anthropic ?? obj, profile);
        }
        return profile;
    }

    private static void ApplyAgentProfileCopilot(JsonObject obj, ReviewAgentProfileSettings profile) {
        var transport = obj.GetString("copilotTransport") ?? obj.GetString("transport");
        if (!string.IsNullOrWhiteSpace(transport)) {
            profile.CopilotTransport = ParseCopilotTransport(transport, CopilotTransportKind.Cli);
        }
        profile.CopilotModel = obj.GetString("copilotModel") ?? profile.CopilotModel;
        profile.CopilotLauncher = obj.GetString("copilotLauncher") ?? obj.GetString("launcher") ?? profile.CopilotLauncher;
        profile.CopilotCliPath = obj.GetString("copilotCliPath") ?? obj.GetString("cliPath") ?? profile.CopilotCliPath;
        profile.CopilotCliUrl = obj.GetString("copilotCliUrl") ?? obj.GetString("cliUrl") ?? profile.CopilotCliUrl;
        profile.CopilotWorkingDirectory =
            obj.GetString("copilotWorkingDirectory") ?? obj.GetString("workingDirectory") ?? profile.CopilotWorkingDirectory;
        profile.CopilotAutoInstall = ReadNullableBool(obj, "copilotAutoInstall") ?? ReadNullableBool(obj, "autoInstall");
        profile.CopilotAutoInstallMethod =
            obj.GetString("copilotAutoInstallMethod") ?? obj.GetString("autoInstallMethod") ?? profile.CopilotAutoInstallMethod;
        profile.CopilotAutoInstallPrerelease =
            ReadNullableBool(obj, "copilotAutoInstallPrerelease") ?? ReadNullableBool(obj, "autoInstallPrerelease");
        profile.CopilotInheritEnvironment =
            ReadNullableBool(obj, "copilotInheritEnvironment") ?? ReadNullableBool(obj, "inheritEnvironment");
        profile.CopilotEnvAllowlist =
            ReadStringList(obj, "copilotEnvAllowlist") ?? ReadStringList(obj, "envAllowlist") ?? profile.CopilotEnvAllowlist;
        profile.CopilotEnv = ReadStringMap(obj, "copilotEnv") ?? ReadStringMap(obj, "env") ?? profile.CopilotEnv;
        profile.CopilotDirectUrl = obj.GetString("copilotDirectUrl") ?? obj.GetString("directUrl") ?? profile.CopilotDirectUrl;
        profile.CopilotDirectTokenEnv =
            obj.GetString("copilotDirectTokenEnv") ?? obj.GetString("directTokenEnv") ?? profile.CopilotDirectTokenEnv;
        profile.CopilotDirectTimeoutSeconds =
            ReadNullablePositiveInt(obj, "copilotDirectTimeoutSeconds") ?? ReadNullablePositiveInt(obj, "directTimeoutSeconds");
        profile.CopilotDirectHeaders =
            ReadStringMap(obj, "copilotDirectHeaders") ?? ReadStringMap(obj, "directHeaders") ?? profile.CopilotDirectHeaders;
    }

    private static void ApplyAgentProfileOpenAiCompatible(JsonObject obj, ReviewAgentProfileSettings profile) {
        profile.OpenAICompatibleBaseUrl =
            obj.GetString("openaiCompatibleBaseUrl") ?? obj.GetString("baseUrl") ?? profile.OpenAICompatibleBaseUrl;
        profile.OpenAICompatibleApiKeyEnv =
            obj.GetString("openaiCompatibleApiKeyEnv") ?? obj.GetString("apiKeyEnv") ?? profile.OpenAICompatibleApiKeyEnv;
        profile.OpenAICompatibleTimeoutSeconds =
            ReadNullablePositiveInt(obj, "openaiCompatibleTimeoutSeconds") ?? ReadNullablePositiveInt(obj, "timeoutSeconds");
    }

    private static void ApplyAgentProfileAnthropic(JsonObject obj, ReviewAgentProfileSettings profile) {
        profile.AnthropicApiKeyEnv =
            obj.GetString("anthropicApiKeyEnv") ?? obj.GetString("apiKeyEnv") ?? profile.AnthropicApiKeyEnv;
        profile.AnthropicBaseUrl = obj.GetString("anthropicBaseUrl") ?? obj.GetString("baseUrl") ?? profile.AnthropicBaseUrl;
        profile.AnthropicTimeoutSeconds =
            ReadNullablePositiveInt(obj, "anthropicTimeoutSeconds") ?? ReadNullablePositiveInt(obj, "timeoutSeconds");
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
        // Reject non-finite values to prevent undefined backoff calculations.
        if (value.HasValue && value.Value >= 1 && NumericGuards.IsFinite(value.Value)) {
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

    private static bool? ReadNullableBool(JsonObject obj, string key) {
        if (obj.TryGetValue(key, out var value)) {
            if (value?.Kind == JsonValueKind.Boolean) {
                return value.AsBoolean();
            }
        }
        return null;
    }

    private static int? ReadNullablePositiveInt(JsonObject obj, string key) {
        var value = obj.GetInt64(key);
        if (value.HasValue && value.Value > 0) {
            return (int)value.Value;
        }
        return null;
    }
}
