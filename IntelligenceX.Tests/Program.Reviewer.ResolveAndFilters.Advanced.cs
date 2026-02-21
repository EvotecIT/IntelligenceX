namespace IntelligenceX.Tests;

internal static partial class Program {
    #if INTELLIGENCEX_REVIEWER
    private static void TestContextDenyInvalidRegex() {
        var matched = ContextDenyMatcher.Matches("hello world", new[] { "[", "poem" });
        AssertEqual(false, matched, "invalid regex match");
        var matchedAllowed = ContextDenyMatcher.Matches("please write a poem", new[] { "poem" });
        AssertEqual(true, matchedAllowed, "valid regex match");
    }

    private static void TestContextDenyTimeout() {
        var input = new string('a', 20000) + "!";
        var matched = ContextDenyMatcher.Matches(input, new[] { "(a+)+$" });
        AssertEqual(false, matched, "timeout match");
    }

    private static void TestReviewSummaryParser() {
        var body = string.Join("\n", new[] {
            "## IntelligenceX Review",
            $"Reviewing PR #1: **Test**",
            $"{ReviewFormatter.ReviewedCommitMarker} `abc1234`",
            "",
            "Summary text"
        });

        var ok = ReviewSummaryParser.TryGetReviewedCommit(body, out var commit);
        AssertEqual(true, ok, "commit parse ok");
        AssertEqual("abc1234", commit, "commit value");

        var noBacktick = $"{ReviewFormatter.ReviewedCommitMarker} abc1234";
        ok = ReviewSummaryParser.TryGetReviewedCommit(noBacktick, out _);
        AssertEqual(false, ok, "no backtick");

        ok = ReviewSummaryParser.TryGetReviewedCommit("No marker here", out _);
        AssertEqual(false, ok, "missing marker");

        var malformedThenValid = string.Join("\n", new[] {
            $"{ReviewFormatter.ReviewedCommitMarker} abc1234",
            $"{ReviewFormatter.ReviewedCommitMarker} `deadbeef`"
        });
        ok = ReviewSummaryParser.TryGetReviewedCommit(malformedThenValid, out commit);
        AssertEqual(true, ok, "malformed then valid");
        AssertEqual("deadbeef", commit, "malformed then valid commit");
    }

    private static void TestReviewSummaryParserMergeBlockerDetection() {
        var noBlockers = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good.",
            "## Todo List ✅",
            "None.",
            "## Critical Issues ⚠️ (if any)",
            "None."
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(noBlockers), "merge blockers none");

        var todoBlocker = string.Join("\n", new[] {
            "## Todo List ✅",
            "- [ ] Fix cancellation race."
        });
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(todoBlocker), "merge blockers todo");

        var criticalBlocker = string.Join("\n", new[] {
            "## Critical Issues ⚠️ (if any)",
            "- Broken reconnect path."
        });
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(criticalBlocker), "merge blockers critical");

        var missingSections = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good."
        });
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(missingSections), "merge blockers missing sections");

        var onlyTodo = string.Join("\n", new[] {
            "## Todo List ✅",
            "None."
        });
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(onlyTodo), "merge blockers missing critical section");

        var placeholderOnly = string.Join("\n", new[] {
            "## Todo List ✅",
            "None.",
            "## Critical Issues ⚠️ (if any)",
            "(if any)"
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(placeholderOnly), "merge blockers placeholder line");
    }

    private static void TestReviewFormatterModelUsageSection() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "head",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Long,
            Mode = "inline"
        };

        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty,
            usageLine: "Usage: 5h limit: 90% remaining | credits: 4.52", findingsBlock: string.Empty);

        AssertContainsText(comment, "### Model & Usage 🤖", "model usage section header");
        AssertContainsText(comment, "- Model: `gpt-5-test`", "model usage model");
        AssertContainsText(comment, "- Usage: 5h limit: 90% remaining | credits: 4.52", "model usage line");
    }

    private static void TestReviewFormatterModelUsageUnavailable() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "head",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings();

        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);

        AssertContainsText(comment, "- Usage: unavailable", "model usage unavailable line");
    }

    private static void TestReviewFormatterGoldenSnapshot() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Golden Snapshot", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var comment = ReviewFormatter.BuildComment(context, "Summary line.", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        MaybeUpdateReviewerFixture("review-summary-golden.md", comment);
        var expected = LoadReviewerFixture("review-summary-golden.md");
        AssertTextBlockEquals(expected, comment, "review formatter golden snapshot");
    }

    private static void TestReviewUsageIntegrationDisplay() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":25.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage integration json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var usageLine = CallFormatUsageSummary(snapshot);
        var context = BuildContext();
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Long,
            Mode = "inline"
        };
        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: usageLine, findingsBlock: string.Empty);

        AssertContainsText(comment, "### Model & Usage 🤖", "usage integration section");
        AssertContainsText(comment, "- Usage: 5h limit: 87.5% remaining", "usage integration 5h");
        AssertContainsText(comment, "code review 5h limit: 75% remaining", "usage integration code review 5h");
    }

    private static void TestReviewUsageSummaryLine() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":25.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        AssertContainsText(line, "Usage:", "usage summary prefix");
        AssertContainsText(line, "5h limit", "usage window label");
        AssertEqual(false, line.IndexOf("\n", StringComparison.Ordinal) >= 0, "usage summary is single line");
    }

    private static void TestReviewUsageSummaryDisambiguatesCodeReviewWeekly() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":20.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120},"
            + "\"secondary_window\":{\"used_percent\":61.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":26.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary disambiguation json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(4, parts.Count, "usage part count weekly");
        AssertContains(parts, "weekly limit: 39% remaining", "weekly label");
        AssertContains(parts, "code review weekly limit: 74% remaining", "code review weekly label");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit: 74% remaining"), "plain duplicate weekly label removed");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit (secondary): 74% remaining"), "plain secondary weekly label removed");
    }

    private static void TestReviewUsageSummaryDisambiguatesCodeReviewWeeklySecondary() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"secondary_window\":{\"used_percent\":61.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"secondary_window\":{\"used_percent\":26.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary secondary disambiguation json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(3, parts.Count, "usage part count weekly secondary");
        AssertContains(parts, "weekly limit: 39% remaining", "weekly label secondary");
        AssertContains(parts, "code review weekly limit (secondary): 74% remaining", "code review weekly secondary label");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit (secondary): 74% remaining"), "plain weekly secondary label removed");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit: 74% remaining"), "plain weekly label removed secondary");
    }

    private static void TestReviewUsageSummaryPrefixesNonWeeklyCodeReview() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":10.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":25.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary non-weekly disambiguation json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(2, parts.Count, "usage part count non-weekly");
        AssertContains(parts, "5h limit: 90% remaining", "general non-weekly label");
        AssertContains(parts, "code review 5h limit: 75% remaining", "code review non-weekly label");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "5h limit: 75% remaining"), "plain non-weekly code review label removed");
    }

    private static void TestReviewUsageBudgetGuardBlocksWhenCreditsAndWeeklyExhausted() {
        const string json = "{"
            + "\"rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
            + "\"secondary_window\":{\"used_percent\":100.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":600}},"
            + "\"code_review_rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
            + "\"secondary_window\":{\"used_percent\":100.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":600}},"
            + "\"credits\":{\"has_credits\":false,\"unlimited\":false,\"balance\":0}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage budget block json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var settings = new ReviewSettings {
            ReviewUsageBudgetAllowCredits = true,
            ReviewUsageBudgetAllowWeeklyLimit = true
        };
        var failure = CallEvaluateUsageBudgetGuardFailure(settings, snapshot);
        AssertNotNull(failure, "usage budget block message");
        AssertContainsText(failure!, "credits exhausted", "usage budget block credits detail");
        AssertContainsText(failure!, "weekly limit", "usage budget block weekly detail");
    }

    private static void TestReviewUsageBudgetGuardAllowsCreditsFallback() {
        const string json = "{"
            + "\"rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
            + "\"secondary_window\":{\"used_percent\":100.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":600}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":1.5}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage budget credits json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var settings = new ReviewSettings {
            ReviewUsageBudgetAllowCredits = true,
            ReviewUsageBudgetAllowWeeklyLimit = true
        };
        var failure = CallEvaluateUsageBudgetGuardFailure(settings, snapshot);
        AssertEqual(null, failure, "usage budget allows credits fallback");
    }

    private static void TestReviewUsageBudgetGuardBlocksWhenNoBudgetSourcesAllowed() {
        var settings = new ReviewSettings {
            ReviewUsageBudgetGuard = true,
            ReviewUsageBudgetAllowCredits = false,
            ReviewUsageBudgetAllowWeeklyLimit = false
        };
        var failure = CallTryBuildUsageBudgetGuardFailure(settings, ReviewProvider.OpenAI);
        AssertNotNull(failure, "usage budget strict mode block message");
        AssertContainsText(failure!, "both credits and weekly budget allowances are disabled",
            "usage budget strict mode block detail");
    }
    #endif
}
