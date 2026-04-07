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

        var plainProseOnly = string.Join("\n", new[] {
            "## Todo List ✅",
            "None.",
            "## Critical Issues ⚠️ (if any)",
            "Looks good."
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(plainProseOnly), "merge blockers plain prose");

        var checkedOnly = string.Join("\n", new[] {
            "## Todo List ✅",
            "- [x] Addressed in this PR.",
            "## Critical Issues ⚠️ (if any)",
            "None."
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(checkedOnly), "merge blockers checked checklist is non-blocking");
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionCompactDefaults() {
        var settings = new ReviewSettings {
            OutputStyle = "compact"
        };
        var todoOnlyNone = string.Join("\n", new[] {
            "## Todo List ✅",
            "None."
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(todoOnlyNone, settings),
            "merge blockers compact todo-only defaults");
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionCompactAliases() {
        var aliases = new[] { "compact", "compact-like", "compact_style", "compact-style" };
        var todoOnlyNone = string.Join("\n", new[] {
            "## Todo List ✅",
            "None."
        });
        foreach (var alias in aliases) {
            var settings = new ReviewSettings {
                OutputStyle = alias
            };
            AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(todoOnlyNone, settings),
                $"merge blockers compact alias {alias}");
        }
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionCustomSections() {
        var settings = new ReviewSettings {
            MergeBlockerSections = new[] { "Blocking Items", "Release Risk" },
            MergeBlockerRequireAllSections = false
        };
        var body = string.Join("\n", new[] {
            "## Blocking Items",
            "None."
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(body, settings),
            "merge blockers custom sections no blockers");
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionAllowNoSectionMatch() {
        var settings = new ReviewSettings {
            MergeBlockerSections = new[] { "Blocking Items" },
            MergeBlockerRequireSectionMatch = false
        };
        var body = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good."
        });
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(body, settings),
            "merge blockers allow missing section match");
    }

    private static void TestConversationResolutionPermissionBlockerSection() {
        var body = string.Join("\n", new[] {
            "## Todo List ✅",
            "None.",
            "## Critical Issues ⚠️ (if any)",
            "None."
        });

        var merged = CallAppendConversationResolutionPermissionBlocker(body, 2, true, "GITHUB_TOKEN",
            "INTELLIGENCEX_GITHUB_TOKEN");

        AssertContainsText(merged, "requires resolved review conversations before merge",
            "conversation resolution blocker rationale");
        AssertContainsText(merged, "`GITHUB_TOKEN` and `INTELLIGENCEX_GITHUB_TOKEN`",
            "conversation resolution blocker token labels");
        AssertEqual(1, CountOccurrences(merged, "## Critical Issues ⚠️"), "conversation resolution blocker keeps one critical section");
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(merged), "conversation resolution blocker triggers merge blocker");

        var notRequired = CallAppendConversationResolutionPermissionBlocker(body, 2, false, "GITHUB_TOKEN");
        AssertEqual(body, notRequired, "conversation resolution blocker omitted when branch rule disabled");
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

    private static void TestReviewFormatterNormalizesInlineSectionLabels() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Inline Sections", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "Summary 📝 This PR fixes the section layout regression.",
            "Todo List ✅ None.",
            "Other Issues 🧯 - Consider a parser-side fallback too.",
            "Tests / Coverage 🧪 - Snapshot coverage looks sufficient."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "## Summary 📝", "normalized summary heading");
        AssertContainsText(normalizedComment, "\n## Todo List ✅\n\nNone.", "normalized todo heading");
        AssertContainsText(normalizedComment, "\n## Other Issues 🧯\n\n- Consider a parser-side fallback too.", "normalized other issues heading");
        AssertContainsText(normalizedComment, "\n## Tests / Coverage 🧪\n\n- Snapshot coverage looks sufficient.", "normalized tests heading");
    }

    private static void TestReviewFormatterNormalizesMalformedHeadingInlineSectionLabels() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Heading Inline Sections", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝Looks good overall.",
            "## Todo List ✅- [ ] Fix the portable bundle cleanup.",
            "## Other Issues 🧯- Keep the formatter/parser in sync."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "\n## Summary 📝\n\nLooks good overall.", "normalized malformed summary heading");
        AssertContainsText(normalizedComment, "\n## Todo List ✅\n\n- [ ] Fix the portable bundle cleanup.", "normalized malformed todo heading");
        AssertContainsText(normalizedComment, "\n## Other Issues 🧯\n\n- Keep the formatter/parser in sync.", "normalized malformed other issues heading");
    }

    private static void TestReviewFormatterNormalizesNoSeparatorSectionLabels() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter No Separator Sections", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "Next Steps 🚀Ship as-is after the final rerun.",
            "##Todo List ✅- [ ] Fix the remaining parser edge case."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "\n## Next Steps 🚀\n\nShip as-is after the final rerun.", "normalized next steps heading without separator");
        AssertContainsText(normalizedComment, "\n## Todo List ✅\n\n- [ ] Fix the remaining parser edge case.", "normalized no-space hash heading");
    }

    private static void TestReviewFormatterDoesNotNormalizeSectionLabelsInsideCodeBlocks() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Code Blocks", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "Summary 📝 Actual summary line.",
            "```md",
            "Todo List ✅ - [ ] Example only.",
            "## Critical Issues ⚠️ None.",
            "```",
            "    Other Issues 🧯 - Still example text."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "```md\nTodo List ✅ - [ ] Example only.\n## Critical Issues ⚠️ None.\n```",
            "fenced code block preserved");
        AssertContainsText(normalizedComment, "\n    Other Issues 🧯 - Still example text.", "indented code line preserved");
    }

    private static void TestReviewFormatterPreservesSectionLabelsInsideLongFenceCodeBlocks() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Long Fence Code Blocks", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "Summary 📝 Actual summary line.",
            "````md",
            "```",
            "Todo List ✅ - [ ] Example only.",
            "````",
            "Critical Issues ⚠️ None."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "````md\n```\nTodo List ✅ - [ ] Example only.\n````",
            "long fenced code block preserved");
        AssertContainsText(normalizedComment, "\n## Critical Issues ⚠️\n\nNone.", "post-fence normalization preserved");
    }

    private static void TestReviewFormatterAllowsLongerFenceClosers() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Longer Fence Closers", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "Summary 📝 Actual summary line.",
            "````md",
            "Todo List ✅ - [ ] Example only.",
            "`````",
            "Todo List ✅ None."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "````md\nTodo List ✅ - [ ] Example only.\n`````",
            "longer fence closer preserved");
        AssertContainsText(normalizedComment, "\n## Todo List ✅\n\nNone.", "post-longer-closer normalization preserved");
    }

    private static void TestReviewFormatterIgnoresIndentedFenceLikeLines() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Indented Fence Like Lines", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "    ```md",
            "    Todo List ✅ - [ ] Example only.",
            "Summary 📝 Actual summary line.",
            "Todo List ✅ None."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        var normalizedComment = comment.Replace("\r\n", "\n").Replace('\r', '\n');

        AssertContainsText(normalizedComment, "    ```md\n    Todo List ✅ - [ ] Example only.", "indented fence-like block preserved");
        AssertContainsText(normalizedComment, "\n## Summary 📝\n\nActual summary line.", "post-indented-code summary normalized");
        AssertContainsText(normalizedComment, "\n## Todo List ✅\n\nNone.", "post-indented-code todo normalized");
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionInlineSectionLabels() {
        var body = string.Join("\n", new[] {
            "Summary 📝 Looks good overall.",
            "Todo List ✅ - [ ] Fix the failing portable bundle cleanup.",
            "Critical Issues ⚠️ None."
        });

        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(body), "merge blockers inline section labels");
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionHeadingInlineSectionLabels() {
        var body = string.Join("\n", new[] {
            "## Summary 📝Looks good overall.",
            "## Todo List ✅- [ ] Fix the failing portable bundle cleanup.",
            "## Critical Issues ⚠️None."
        });

        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(body), "merge blockers heading inline section labels");
    }

    private static void TestReviewSummaryParserMergeBlockerDetectionNoSpaceHeadingPrefixes() {
        var body = string.Join("\n", new[] {
            "##Summary 📝Looks good overall.",
            "##Todo List ✅- [ ] Fix the failing portable bundle cleanup.",
            "##Critical Issues ⚠️None."
        });

        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(body), "merge blockers no-space heading prefixes");
    }

    private static void TestReviewSummaryParserIgnoresChecklistInsideLongFenceCodeBlocks() {
        var body = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good overall.",
            "````md",
            "```",
            "Todo List ✅ - [ ] Example only.",
            "````",
            "## Todo List ✅",
            "None.",
            "## Critical Issues ⚠️",
            "None."
        });

        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(body), "merge blockers ignore long fence checklist");
    }

    private static void TestReviewSummaryParserIgnoresChecklistInsideLongFenceCodeBlocksWithLongerCloser() {
        var body = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good overall.",
            "````md",
            "Todo List ✅ - [ ] Example only.",
            "`````",
            "## Todo List ✅",
            "None.",
            "## Critical Issues ⚠️",
            "None."
        });

        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(body), "merge blockers ignore long fence checklist with longer closer");
    }

    private static void TestReviewUsageIntegrationDisplay() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
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
    }

    private static void TestReviewUsageSummaryLine() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
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

    private static void TestReviewUsageSummaryIncludesWeeklyWindow() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":20.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120},"
            + "\"secondary_window\":{\"used_percent\":61.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary weekly json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(3, parts.Count, "usage part count weekly");
        AssertContains(parts, "5h limit: 80% remaining", "primary label");
        AssertContains(parts, "weekly limit: 39% remaining", "weekly label");
    }

    private static void TestReviewUsageSummaryUsesSecondaryFallbackLabel() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"secondary_window\":{\"used_percent\":26.0,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary secondary fallback json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(2, parts.Count, "usage part count secondary fallback");
        AssertContains(parts, "rate limit (secondary): 74% remaining", "secondary fallback label");
    }

    private static void TestReviewUsageBudgetGuardBlocksWhenCreditsAndWeeklyExhausted() {
        const string json = "{"
            + "\"rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
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

    private static void TestReviewClaudeUsageSummaryLine() {
        var snapshot = new IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot(
            "claude",
            "Claude",
            "Claude OAuth usage API",
            "Claude Max",
            "user@example.com",
            new[] {
                new IntelligenceX.Telemetry.Limits.ProviderLimitWindow("session", "Session", 12.5d, DateTimeOffset.UtcNow.AddHours(4)),
                new IntelligenceX.Telemetry.Limits.ProviderLimitWindow("weekly", "Weekly", 26.0d, DateTimeOffset.UtcNow.AddDays(6)),
                new IntelligenceX.Telemetry.Limits.ProviderLimitWindow("opus", "Opus weekly", 61.0d, DateTimeOffset.UtcNow.AddDays(6))
            },
            "Extra 2.00 / 20.00 USD",
            null,
            DateTimeOffset.UtcNow);

        var line = CallFormatUsageSummary(snapshot);
        AssertContainsText(line, "Usage:", "claude usage summary prefix");
        AssertContainsText(line, "session: 87.5% remaining", "claude usage session");
        AssertContainsText(line, "weekly: 74% remaining", "claude usage weekly");
        AssertContainsText(line, "opus weekly: 39% remaining", "claude usage opus");
        AssertContainsText(line, "Extra 2.00 / 20.00 USD", "claude usage extra");
    }

    private static void TestReviewClaudeUsageBudgetGuardBlocksWhenWeeklyExhausted() {
        var snapshot = new IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot(
            "claude",
            "Claude",
            "Claude OAuth usage API",
            "Claude Max",
            null,
            new[] {
                new IntelligenceX.Telemetry.Limits.ProviderLimitWindow("weekly", "Weekly", 100d, DateTimeOffset.UtcNow.AddMinutes(30), windowDuration: TimeSpan.FromDays(7)),
                new IntelligenceX.Telemetry.Limits.ProviderLimitWindow("sonnet", "Sonnet weekly", 100d, DateTimeOffset.UtcNow.AddMinutes(30), windowDuration: TimeSpan.FromDays(7))
            },
            null,
            null,
            DateTimeOffset.UtcNow);

        var settings = new ReviewSettings {
            ReviewUsageBudgetAllowCredits = false,
            ReviewUsageBudgetAllowWeeklyLimit = true
        };
        var failure = CallEvaluateUsageBudgetGuardFailure(settings, snapshot);
        AssertNotNull(failure, "claude usage budget guard block message");
        AssertContainsText(failure!, "weekly exhausted", "claude usage budget weekly exhausted");
        AssertContainsText(failure!, "sonnet weekly exhausted", "claude usage budget sonnet exhausted");
    }

    private static void TestReviewClaudeUsageBudgetGuardAllowsRemainingWeekly() {
        var snapshot = new IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot(
            "claude",
            "Claude",
            "Claude OAuth usage API",
            "Claude Max",
            null,
            new[] {
                new IntelligenceX.Telemetry.Limits.ProviderLimitWindow("weekly", "Weekly", 64d, DateTimeOffset.UtcNow.AddHours(3), windowDuration: TimeSpan.FromDays(7))
            },
            null,
            null,
            DateTimeOffset.UtcNow);

        var settings = new ReviewSettings {
            ReviewUsageBudgetAllowCredits = false,
            ReviewUsageBudgetAllowWeeklyLimit = true
        };
        var failure = CallEvaluateUsageBudgetGuardFailure(settings, snapshot);
        AssertEqual(null, failure, "claude usage budget allows remaining weekly");
    }
    #endif
}
