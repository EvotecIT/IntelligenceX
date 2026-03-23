namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    private static async Task<ThreadTriageResult> MaybeAutoResolveAssessedThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        ReviewRunner runner, PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        ReviewContextExtras extras, bool reviewFailed, string? diffNote, CancellationToken cancellationToken,
        bool force, bool allowCommentPost, bool noMergeBlockers) {
        if ((!settings.ReviewThreadsAutoResolveAI && !force) || reviewFailed) {
            return ThreadTriageResult.Empty;
        }
        if (extras.ReviewThreads.Count == 0) {
            return ThreadTriageResult.Empty;
        }

        var candidates = SelectAssessmentCandidates(extras.ReviewThreads, settings);
        if (candidates.Count == 0) {
            return ThreadTriageResult.Empty;
        }

        var prompt = BuildThreadAssessmentPrompt(context, candidates, files, settings, diffNote);
        if (settings.RedactPii) {
            prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
        }

        var output = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        if (ReviewDiagnostics.IsFailureBody(output)) {
            return ThreadTriageResult.Empty;
        }

        var assessments = ParseThreadAssessments(output);
        if (assessments.Count == 0) {
            Console.Error.WriteLine("Thread assessment returned no usable results.");
            return ThreadTriageResult.Empty;
        }

        var byId = new Dictionary<string, ThreadAssessment>(StringComparer.OrdinalIgnoreCase);
        var missingIdCount = 0;
        var duplicateIdCount = 0;
        var duplicateIdExamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assessment in assessments) {
            var normalizedId = NormalizeThreadAssessmentId(assessment.Id);
            if (normalizedId.Length == 0) {
                missingIdCount++;
                continue;
            }
            if (!byId.TryAdd(normalizedId, assessment)) {
                duplicateIdCount++;
                if (duplicateIdExamples.Count < 3) {
                    duplicateIdExamples.Add(normalizedId);
                }
                // Last occurrence wins to keep deterministic behavior without throwing.
                byId[normalizedId] = assessment;
            }
        }
        if (missingIdCount > 0) {
            Console.Error.WriteLine($"Thread assessment skipped {missingIdCount} item(s) with missing ids.");
        }
        if (duplicateIdCount > 0) {
            var examples = duplicateIdExamples.Count > 0 ? $" (e.g., {string.Join(", ", duplicateIdExamples)})" : string.Empty;
            Console.Error.WriteLine($"Thread assessment contained {duplicateIdCount} duplicate id(s){examples}; using last occurrence.");
        }
        var replyMap = new Dictionary<string, ThreadAssessment>(StringComparer.OrdinalIgnoreCase);
        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, settings.MaxPatchChars);
        var resolved = new List<ThreadAssessment>();
        var kept = new List<ThreadAssessment>();
        var failed = new List<ThreadAssessment>();
        var evidenceRejected = 0;
        var permissionDeniedCount = 0;
        var permissionDeniedCredentialLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolveAttempts = 0;
        var sweepResolved = 0;
        foreach (var assessment in assessments) {
            switch (assessment.Action) {
                case "comment":
                case "keep":
                    kept.Add(assessment);
                    var normalizedReplyId = NormalizeThreadAssessmentId(assessment.Id);
                    if (normalizedReplyId.Length > 0) {
                        replyMap[normalizedReplyId] = assessment;
                    }
                    break;
            }
        }

        var resolvedCount = 0;
        foreach (var thread in candidates) {
            if (resolvedCount >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            var normalizedThreadId = NormalizeThreadAssessmentId(thread.Id);
            if (!byId.TryGetValue(normalizedThreadId, out var assessment) || assessment.Action != "resolve") {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveRequireEvidence &&
                !HasValidResolveEvidence(assessment.Evidence, thread, patchIndex, patchLookup, settings.MaxPatchChars)) {
                evidenceRejected++;
                var missingEvidence = new ThreadAssessment(assessment.Id, "keep",
                    $"{assessment.Reason} (missing diff evidence)", assessment.Evidence);
                kept.Add(missingEvidence);
                replyMap[normalizedThreadId] = missingEvidence;
                continue;
            }
            resolveAttempts++;
            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolvedCount++;
                resolved.Add(assessment);
                continue;
            }
            var error = result.Error ?? "unknown error";
            if (result.PermissionDenied) {
                permissionDeniedCount++;
                foreach (var label in result.PermissionDeniedCredentialLabels) {
                    permissionDeniedCredentialLabels.Add(label);
                }
            }
            var failedAssessment = new ThreadAssessment(assessment.Id, "keep", $"{assessment.Reason} (resolve failed: {error})",
                assessment.Evidence);
            failed.Add(failedAssessment);
            replyMap[normalizedThreadId] = failedAssessment;
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {error}");
        }

        if (failed.Count > 0) {
            kept.AddRange(failed);
        }

        if (noMergeBlockers && settings.ReviewThreadsAutoResolveSweepNoBlockers && kept.Count > 0) {
            var remainingResolveBudget = Math.Max(0, settings.ReviewThreadsAutoResolveMax - resolvedCount);
            var extraResolved = await TryResolveKeptBotThreadsAfterNoBlockersAsync(github, fallbackGithub, candidates, resolved, kept,
                    settings, remainingResolveBudget, cancellationToken)
                .ConfigureAwait(false);
            sweepResolved = extraResolved;
            resolvedCount += extraResolved;
        }

        var commentPosted = false;
        var triageBody = BuildThreadAssessmentComment(resolved, kept, context.HeadSha, diffNote);
        if (settings.ReviewThreadsAutoResolveAIReply && replyMap.Count > 0) {
            await ReplyToKeptThreadsAsync(github, context, candidates, replyMap, context.HeadSha, diffNote, settings, cancellationToken)
                .ConfigureAwait(false);
        }
        if (allowCommentPost && settings.ReviewThreadsAutoResolveAIPostComment &&
            !settings.ReviewThreadsAutoResolveAIEmbed && kept.Count > 0) {
            var body = triageBody;
            await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
                .ConfigureAwait(false);
            commentPosted = true;
        }
        if (settings.ReviewThreadsAutoResolveSummaryComment && !commentPosted && (resolved.Count > 0 || kept.Count > 0)) {
            var body = BuildThreadAutoResolveSummaryComment(resolved, kept, context.HeadSha, diffNote);
            await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
                .ConfigureAwait(false);
            commentPosted = true;
        }

        if (resolvedCount == 0 && kept.Count == 0) {
            return ThreadTriageResult.Empty;
        }
        var summary =
            $"Auto-resolve (AI): {resolvedCount} resolved, {kept.Count} kept " +
            $"(candidates {candidates.Count}, assessments {assessments.Count}, attempts {resolveAttempts}, " +
            $"evidence_rejected {evidenceRejected}, resolve_failed {failed.Count}, sweep_resolved {sweepResolved}, " +
            $"missing_ids {missingIdCount}, duplicate_ids {duplicateIdCount}).";
        if (commentPosted) {
            summary += " Triage comment posted.";
        }
        var fallbackSummary = BuildFallbackTriageSummary(resolved, kept);
        return new ThreadTriageResult(summary, triageBody, fallbackSummary,
            AutoResolvePermissionDiagnostics.From(permissionDeniedCount, permissionDeniedCredentialLabels));
    }

    private static async Task<string> TryBuildUsageLineAsync(ReviewSettings settings, ReviewProvider providerKind) {
        if (!settings.ReviewUsageSummary) {
            return string.Empty;
        }
        var provider = ReviewProviderContracts.Get(providerKind);
        if (!provider.SupportsUsageApi) {
            return string.Empty;
        }

        try {
            if (providerKind == ReviewProvider.OpenAI) {
                var snapshot = await TryGetUsageSnapshotAsync(settings).ConfigureAwait(false);
                if (snapshot is null) {
                    return string.Empty;
                }
                var summary = FormatUsageSummary(snapshot);
                return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary;
            }

            if (providerKind == ReviewProvider.Claude) {
                var snapshot = await TryGetProviderLimitSnapshotAsync("claude", settings).ConfigureAwait(false);
                if (snapshot is null) {
                    return string.Empty;
                }
                var summary = FormatUsageSummary(snapshot);
                return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary;
            }

            return string.Empty;
        } catch (Exception ex) {
            if (settings.Diagnostics) {
                Console.Error.WriteLine($"Usage summary failed: {ex.Message}");
            }
            return string.Empty;
        }
    }

    private static async Task<string?> TryBuildUsageBudgetGuardFailureAsync(ReviewSettings settings, ReviewProvider providerKind) {
        if (!settings.ReviewUsageBudgetGuard) {
            return null;
        }
        if (!settings.ReviewUsageBudgetAllowCredits && !settings.ReviewUsageBudgetAllowWeeklyLimit) {
            return "Usage budget guard blocked review run: both credits and weekly budget allowances are disabled. "
                   + "Enable reviewUsageBudgetAllowCredits or reviewUsageBudgetAllowWeeklyLimit "
                   + "(or REVIEW_USAGE_BUDGET_ALLOW_CREDITS/REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT), "
                   + "or disable REVIEW_USAGE_BUDGET_GUARD.";
        }
        var provider = ReviewProviderContracts.Get(providerKind);
        if (!provider.SupportsUsageApi) {
            return null;
        }

        try {
            if (providerKind == ReviewProvider.OpenAI) {
                var snapshot = await TryGetUsageSnapshotAsync(settings).ConfigureAwait(false);
                if (snapshot is null) {
                    return null;
                }
                return EvaluateUsageBudgetGuardFailure(settings, snapshot);
            }

            if (providerKind == ReviewProvider.Claude) {
                var snapshot = await TryGetProviderLimitSnapshotAsync("claude", settings).ConfigureAwait(false);
                if (snapshot is null) {
                    return null;
                }
                return EvaluateUsageBudgetGuardFailure(settings, snapshot);
            }

            return null;
        } catch (Exception ex) {
            if (settings.Diagnostics) {
                Console.Error.WriteLine($"Usage budget guard skipped: {ex.Message}");
            }
            return null;
        }
    }

    private static string? EvaluateUsageBudgetGuardFailure(ReviewSettings settings, ChatGptUsageSnapshot snapshot) {
        var checks = new List<(BudgetAvailability Availability, string Detail)>();
        if (settings.ReviewUsageBudgetAllowCredits) {
            var credits = EvaluateCreditsBudget(snapshot, out var creditsDetail);
            checks.Add((credits, creditsDetail));
        }
        if (settings.ReviewUsageBudgetAllowWeeklyLimit) {
            var weekly = EvaluateWeeklyBudget(snapshot, out var weeklyDetail);
            checks.Add((weekly, weeklyDetail));
        }
        if (checks.Count == 0) {
            return "Usage budget guard blocked review run: both credits and weekly budget allowances are disabled. "
                   + "Enable reviewUsageBudgetAllowCredits or reviewUsageBudgetAllowWeeklyLimit "
                   + "(or REVIEW_USAGE_BUDGET_ALLOW_CREDITS/REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT), "
                   + "or disable REVIEW_USAGE_BUDGET_GUARD.";
        }
        if (checks.Any(static c => c.Availability == BudgetAvailability.Available)) {
            return null;
        }
        if (checks.Any(static c => c.Availability == BudgetAvailability.Unknown)) {
            return null;
        }

        var detail = string.Join("; ", checks.Select(static c => c.Detail));
        return "Usage budget guard blocked review run: "
               + detail
               + ". Configure reviewUsageBudgetAllowCredits/reviewUsageBudgetAllowWeeklyLimit "
               + "(or REVIEW_USAGE_BUDGET_ALLOW_CREDITS/REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT), "
               + "or disable REVIEW_USAGE_BUDGET_GUARD.";
    }

    private static string? EvaluateUsageBudgetGuardFailure(ReviewSettings settings, IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot snapshot) {
        var checks = new List<(BudgetAvailability Availability, string Detail)>();
        if (settings.ReviewUsageBudgetAllowWeeklyLimit) {
            var weekly = EvaluateProviderWeeklyBudget(snapshot, out var weeklyDetail);
            checks.Add((weekly, weeklyDetail));
        }

        if (checks.Count == 0) {
            return "Usage budget guard blocked review run: both credits and weekly budget allowances are disabled. "
                   + "Enable reviewUsageBudgetAllowCredits or reviewUsageBudgetAllowWeeklyLimit "
                   + "(or REVIEW_USAGE_BUDGET_ALLOW_CREDITS/REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT), "
                   + "or disable REVIEW_USAGE_BUDGET_GUARD.";
        }
        if (checks.Any(static c => c.Availability == BudgetAvailability.Available)) {
            return null;
        }
        if (checks.Any(static c => c.Availability == BudgetAvailability.Unknown)) {
            return null;
        }

        var detail = string.Join("; ", checks.Select(static c => c.Detail));
        return "Usage budget guard blocked review run: "
               + detail
               + ". Configure reviewUsageBudgetAllowCredits/reviewUsageBudgetAllowWeeklyLimit "
               + "(or REVIEW_USAGE_BUDGET_ALLOW_CREDITS/REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT), "
               + "or disable REVIEW_USAGE_BUDGET_GUARD.";
    }

    private enum BudgetAvailability {
        Unknown,
        Available,
        Unavailable
    }

    private static BudgetAvailability EvaluateCreditsBudget(ChatGptUsageSnapshot snapshot, out string detail) {
        var credits = snapshot.Credits;
        if (credits is null) {
            detail = "credits unavailable";
            return BudgetAvailability.Unknown;
        }
        if (credits.Unlimited) {
            detail = "credits available (unlimited)";
            return BudgetAvailability.Available;
        }
        if (credits.Balance.HasValue) {
            var balance = credits.Balance.Value;
            if (balance > 0) {
                detail = $"credits available ({balance.ToString("0.####", CultureInfo.InvariantCulture)})";
                return BudgetAvailability.Available;
            }
            if (!credits.HasCredits) {
                detail = "credits exhausted (balance 0)";
                return BudgetAvailability.Unavailable;
            }
            detail = "credits available";
            return BudgetAvailability.Available;
        }
        if (credits.HasCredits) {
            detail = "credits available";
            return BudgetAvailability.Available;
        }
        detail = "credits exhausted";
        return BudgetAvailability.Unavailable;
    }

    private static BudgetAvailability EvaluateWeeklyBudget(ChatGptUsageSnapshot snapshot, out string detail) {
        var observations = new List<(BudgetAvailability Availability, string Detail)>();
        AddWeeklyObservations(snapshot.RateLimit, string.Empty, observations);
        AddWeeklyObservations(snapshot.CodeReviewRateLimit, CodeReviewPrefix.Trim(), observations);
        if (observations.Count == 0) {
            detail = "weekly limit unavailable";
            return BudgetAvailability.Unknown;
        }
        if (observations.Any(static o => o.Availability == BudgetAvailability.Available)) {
            detail = "weekly limit available";
            return BudgetAvailability.Available;
        }
        if (observations.All(static o => o.Availability == BudgetAvailability.Unavailable)) {
            detail = string.Join("; ", observations.Select(static o => o.Detail));
            return BudgetAvailability.Unavailable;
        }
        detail = "weekly limit unavailable";
        return BudgetAvailability.Unknown;
    }

    private static BudgetAvailability EvaluateProviderWeeklyBudget(
        IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot snapshot,
        out string detail) {
        if (snapshot is null) {
            detail = "weekly limit unavailable";
            return BudgetAvailability.Unknown;
        }

        var weeklyWindows = snapshot.Windows
            .Where(static window => IsWeeklyWindow(window))
            .ToArray();
        if (weeklyWindows.Length == 0) {
            detail = "weekly limit unavailable";
            return BudgetAvailability.Unknown;
        }

        if (weeklyWindows.Any(window => ResolveRemainingPercent(window).GetValueOrDefault() > 0d)) {
            detail = "weekly limit available";
            return BudgetAvailability.Available;
        }

        if (weeklyWindows.All(static window => window.UsedPercent.HasValue)) {
            detail = string.Join("; ", weeklyWindows.Select(window =>
                $"{FormatProviderWindowLabel(window.Label)} exhausted{FormatWindowResetSuffix(window)}"));
            return BudgetAvailability.Unavailable;
        }

        detail = "weekly limit unavailable";
        return BudgetAvailability.Unknown;
    }

    private static void AddWeeklyObservations(ChatGptRateLimitStatus? status, string prefix,
        List<(BudgetAvailability Availability, string Detail)> observations) {
        if (status is null) {
            return;
        }

        AddWeeklyWindowObservation(status, status.PrimaryWindow, prefix, observations);
        AddWeeklyWindowObservation(status, status.SecondaryWindow, prefix, observations);
    }

    private static void AddWeeklyWindowObservation(ChatGptRateLimitStatus status, ChatGptRateLimitWindow? window, string prefix,
        List<(BudgetAvailability Availability, string Detail)> observations) {
        if (window is null || !IsWeeklyWindow(window)) {
            return;
        }

        var label = string.IsNullOrWhiteSpace(prefix) ? "weekly limit" : $"{prefix} weekly limit";
        var remaining = ResolveRemainingPercent(window);
        if (remaining.HasValue) {
            if (remaining.Value > 0) {
                observations.Add((BudgetAvailability.Available, $"{label} available ({remaining.Value:0.#}% remaining)"));
                return;
            }
            observations.Add((BudgetAvailability.Unavailable, $"{label} exhausted{FormatWindowResetSuffix(window)}"));
            return;
        }

        if (status.Allowed && !status.LimitReached) {
            observations.Add((BudgetAvailability.Available, $"{label} available"));
            return;
        }
        if (!status.Allowed || status.LimitReached) {
            observations.Add((BudgetAvailability.Unavailable, $"{label} exhausted{FormatWindowResetSuffix(window)}"));
            return;
        }
        observations.Add((BudgetAvailability.Unknown, $"{label} unavailable"));
    }

    private static bool IsWeeklyWindow(ChatGptRateLimitWindow window) {
        return window.LimitWindowSeconds.HasValue && IsWithin(window.LimitWindowSeconds.Value, 7 * 24 * 3600, 3600);
    }

    private static bool IsWeeklyWindow(IntelligenceX.Telemetry.Limits.ProviderLimitWindow window) {
        if (window.WindowDuration.HasValue) {
            return Math.Abs((window.WindowDuration.Value - TimeSpan.FromDays(7)).TotalHours) <= 1d;
        }

        var label = window.Label ?? string.Empty;
        return label.IndexOf("weekly", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static double? ResolveRemainingPercent(ChatGptRateLimitWindow window) {
        if (!window.UsedPercent.HasValue) {
            return null;
        }
        return Math.Max(0, 100 - window.UsedPercent.Value);
    }

    private static double? ResolveRemainingPercent(IntelligenceX.Telemetry.Limits.ProviderLimitWindow window) {
        if (!window.UsedPercent.HasValue) {
            return null;
        }

        return Math.Max(0, 100 - window.UsedPercent.Value);
    }

    private static string FormatWindowResetSuffix(ChatGptRateLimitWindow window) {
        var resetIn = ResolveWindowResetIn(window);
        if (resetIn.HasValue) {
            return $" (resets in {FormatDuration((long)Math.Round(resetIn.Value.TotalSeconds))})";
        }
        if (window.ResetAtUnixSeconds.HasValue) {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value).ToUniversalTime();
            return $" (resets at {resetAt.ToString("u", CultureInfo.InvariantCulture)})";
        }
        return string.Empty;
    }

    private static string FormatWindowResetSuffix(IntelligenceX.Telemetry.Limits.ProviderLimitWindow window) {
        if (window.ResetsAt.HasValue) {
            var delta = window.ResetsAt.Value - DateTimeOffset.UtcNow;
            var bounded = delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            return $" (resets in {FormatDuration((long)Math.Round(bounded.TotalSeconds))})";
        }

        return string.Empty;
    }

    private static TimeSpan? ResolveWindowResetIn(ChatGptRateLimitWindow window) {
        if (window.ResetAfterSeconds.HasValue) {
            return TimeSpan.FromSeconds(Math.Max(0, window.ResetAfterSeconds.Value));
        }
        if (window.ResetAtUnixSeconds.HasValue) {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value).ToUniversalTime();
            var delta = resetAt - DateTimeOffset.UtcNow;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }
        return null;
    }

    private static Task<ChatGptUsageSnapshot?> TryGetUsageSnapshotAsync(ReviewSettings settings) {
        return TryGetUsageSnapshotAsync(settings, settings.OpenAiAccountId);
    }

    private static async Task<IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot?> TryGetProviderLimitSnapshotAsync(
        string providerId,
        ReviewSettings settings) {
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(1, settings.ReviewUsageSummaryTimeoutSeconds)));
        var service = new IntelligenceX.Telemetry.Limits.ProviderLimitSnapshotService();
        var snapshot = await service.FetchAsync(providerId, cts.Token).ConfigureAwait(false);
        return snapshot.IsAvailable ? snapshot : null;
    }

    private static async Task<ChatGptUsageSnapshot?> TryGetUsageSnapshotAsync(ReviewSettings settings, string? accountId) {
        var configuredAccountId = NormalizeAccountId(accountId);
        var cachePath = ChatGptUsageCache.ResolveCachePath(configuredAccountId);
        var cacheMinutes = Math.Max(0, settings.ReviewUsageSummaryCacheMinutes);
        if (cacheMinutes > 0 && ChatGptUsageCache.TryLoad(out var entry, cachePath) && entry is not null) {
            var age = DateTimeOffset.UtcNow - entry.UpdatedAt;
            if (age <= TimeSpan.FromMinutes(cacheMinutes)) {
                return entry.Snapshot;
            }
        }

        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(1, settings.ReviewUsageSummaryTimeoutSeconds)));
        var options = new OpenAINativeOptions {
            AuthStore = new FileAuthBundleStore(),
            AuthAccountId = configuredAccountId
        };
        using var service = new ChatGptUsageService(options);
        var snapshot = await service.GetUsageSnapshotAsync(cts.Token).ConfigureAwait(false);
        try {
            var snapshotAccountId = NormalizeAccountId(snapshot.AccountId);
            var savePath = ChatGptUsageCache.ResolveCachePath(configuredAccountId is null
                ? null
                : snapshotAccountId ?? configuredAccountId);
            ChatGptUsageCache.Save(snapshot, savePath);
        } catch {
            // Best-effort cache.
        }
        return snapshot;
    }

    private static string? NormalizeAccountId(string? accountId) {
        if (string.IsNullOrWhiteSpace(accountId)) {
            return null;
        }
        return accountId.Trim();
    }

    private static string FormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var lines = new List<string>();

        var primary = FormatRateLimitLine(snapshot.RateLimit?.PrimaryWindow, UsageLimitLineKind.GeneralPrimary);
        if (!string.IsNullOrWhiteSpace(primary)) {
            lines.Add(primary);
        }

        var secondary = FormatRateLimitLine(snapshot.RateLimit?.SecondaryWindow, UsageLimitLineKind.GeneralSecondary);
        if (!string.IsNullOrWhiteSpace(secondary)) {
            lines.Add(secondary);
        }

        var codePrimary = FormatRateLimitLine(snapshot.CodeReviewRateLimit?.PrimaryWindow, UsageLimitLineKind.CodeReviewPrimary);
        if (!string.IsNullOrWhiteSpace(codePrimary)) {
            lines.Add(codePrimary);
        }

        var codeSecondary = FormatRateLimitLine(snapshot.CodeReviewRateLimit?.SecondaryWindow, UsageLimitLineKind.CodeReviewSecondary);
        if (!string.IsNullOrWhiteSpace(codeSecondary)) {
            lines.Add(codeSecondary);
        }

        if (snapshot.Credits is not null) {
            if (snapshot.Credits.Unlimited) {
                lines.Add("credits: unlimited");
            } else if (snapshot.Credits.Balance.HasValue) {
                lines.Add($"credits: {snapshot.Credits.Balance.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            } else if (!snapshot.Credits.HasCredits) {
                lines.Add("credits: none");
            }
        }

        if (lines.Count == 0) {
            return string.Empty;
        }
        return UsageSummaryPrefix + string.Join(UsageSummarySeparator, lines);
    }

    private static string FormatUsageSummary(IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot snapshot) {
        var parts = new List<string>();
        foreach (var window in snapshot.Windows) {
            var remaining = ResolveRemainingPercent(window);
            if (!remaining.HasValue) {
                continue;
            }

            parts.Add($"{FormatProviderWindowLabel(window.Label)}: {remaining.Value:0.#}% remaining");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Summary)) {
            parts.Add(snapshot.Summary!.Trim());
        }

        if (parts.Count == 0) {
            return string.Empty;
        }

        return UsageSummaryPrefix + string.Join(UsageSummarySeparator, parts);
    }

    private static string FormatProviderWindowLabel(string? label) {
        var value = (label ?? string.Empty).Trim();
        if (value.Length == 0) {
            return "limit";
        }

        return value.ToLowerInvariant();
    }

    private enum UsageLimitLineKind {
        GeneralPrimary,
        GeneralSecondary,
        CodeReviewPrimary,
        CodeReviewSecondary
    }

    private static string? FormatRateLimitLine(ChatGptRateLimitWindow? window, UsageLimitLineKind lineKind) {
        if (window is null) {
            return null;
        }
        var remaining = FormatRemainingPercent(window.UsedPercent);
        if (string.IsNullOrWhiteSpace(remaining)) {
            return null;
        }
        var fallbackLabel = GetUsageLimitFallbackLabel(lineKind);
        var windowLabel = FormatWindowLabel(window);
        var label = fallbackLabel;
        if (!string.IsNullOrWhiteSpace(windowLabel)) {
            label = IsCodeReviewWindow(lineKind)
                ? FormatCodeReviewLabel(windowLabel, IsSecondaryWindow(lineKind))
                : windowLabel;
        }
        return $"{label}: {remaining}% remaining";
    }

    private static string FormatCodeReviewLabel(string windowLabel, bool isSecondaryWindow) {
        var label = windowLabel.Trim();
        // Secondary suffix ownership lives here for code-review labels.
        // If upstream window labels ever include a secondary suffix, this guard avoids double-appending.
        if (isSecondaryWindow && !label.EndsWith(SecondaryWindowSuffix, StringComparison.OrdinalIgnoreCase)) {
            label += SecondaryWindowSuffix;
        }
        return CodeReviewPrefix + label;
    }

    private static string GetUsageLimitFallbackLabel(UsageLimitLineKind lineKind) {
        return lineKind switch {
            UsageLimitLineKind.GeneralPrimary => "rate limit",
            UsageLimitLineKind.GeneralSecondary => "rate limit (secondary)",
            UsageLimitLineKind.CodeReviewPrimary => CodeReviewPrefix + "limit",
            UsageLimitLineKind.CodeReviewSecondary => CodeReviewPrefix + "limit (secondary)",
            _ => throw new ArgumentOutOfRangeException(nameof(lineKind), lineKind, "Unsupported usage limit line kind")
        };
    }

    private static bool IsCodeReviewWindow(UsageLimitLineKind lineKind) {
        return lineKind == UsageLimitLineKind.CodeReviewPrimary
            || lineKind == UsageLimitLineKind.CodeReviewSecondary;
    }

    private static bool IsSecondaryWindow(UsageLimitLineKind lineKind) {
        return lineKind == UsageLimitLineKind.GeneralSecondary
            || lineKind == UsageLimitLineKind.CodeReviewSecondary;
    }

    private static string? FormatWindowLabel(ChatGptRateLimitWindow window) {
        if (!window.LimitWindowSeconds.HasValue) {
            return null;
        }
        var seconds = Math.Max(0, window.LimitWindowSeconds.Value);
        if (IsWithin(seconds, 5 * 3600, 600)) {
            return "5h limit";
        }
        if (IsWithin(seconds, 7 * 24 * 3600, 3600)) {
            return "weekly limit";
        }
        if (IsWithin(seconds, 24 * 3600, 3600)) {
            return "daily limit";
        }
        if (IsWithin(seconds, 3600, 120)) {
            return "hourly limit";
        }
        return $"{FormatDuration(seconds)} limit";
    }

    private static bool IsWithin(long value, long target, long tolerance) {
        return Math.Abs(value - target) <= tolerance;
    }

    private static string FormatDuration(long seconds) {
        if (seconds <= 0) {
            return "0s";
        }
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1 && span.TotalDays % 1 == 0) {
            return $"{(int)span.TotalDays}d";
        }
        if (span.TotalHours >= 1 && span.TotalHours % 1 == 0) {
            return $"{(int)span.TotalHours}h";
        }
        if (span.TotalMinutes >= 1) {
            return $"{(int)Math.Round(span.TotalMinutes)}m";
        }
        return $"{(int)Math.Round(span.TotalSeconds)}s";
    }

    private static string? FormatRemainingPercent(double? usedPercent) {
        if (!usedPercent.HasValue) {
            return null;
        }
        var remaining = Math.Max(0, 100 - usedPercent.Value);
        return remaining.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static async Task<string?> FindOldestSummaryCommitAsync(IReviewCodeHostReader codeHostReader, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var limit = Math.Max(0, settings.CommentSearchLimit);
        var comments = await codeHostReader.ListIssueCommentsAsync(context, limit, cancellationToken)
            .ConfigureAwait(false);
        string? oldest = null;
        foreach (var comment in comments) {
            if (!IsOwnedSummaryComment(comment)) {
                continue;
            }
            var commit = ExtractReviewedCommit(comment.Body);
            if (!string.IsNullOrWhiteSpace(commit)) {
                oldest = commit;
            }
        }
        return oldest;
    }

}

