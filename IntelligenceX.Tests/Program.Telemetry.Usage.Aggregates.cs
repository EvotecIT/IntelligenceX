using System;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageDailyAggregateBuilderGroupsByProviderAccountModelAndSurface() {
        var builder = new UsageDailyAggregateBuilder();
        var day = new DateTimeOffset(2026, 03, 11, 8, 0, 0, TimeSpan.Zero);
        var events = new[] {
            new UsageEventRecord("evt-1", "codex", "codex.session-log", "src-1", day) {
                ProviderAccountId = "acct-1",
                AccountLabel = "work",
                Model = "gpt-5.4-codex",
                Surface = "cli",
                InputTokens = 100,
                CachedInputTokens = 20,
                OutputTokens = 40,
                ReasoningTokens = 5,
                TotalTokens = 160,
                DurationMs = 1200,
                CostUsd = 0.42m,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-2", "codex", "codex.session-log", "src-1", day.AddHours(1)) {
                ProviderAccountId = "acct-1",
                AccountLabel = "work",
                Model = "gpt-5.4-codex",
                Surface = "cli",
                InputTokens = 10,
                CachedInputTokens = 5,
                OutputTokens = 8,
                ReasoningTokens = 2,
                TotalTokens = 23,
                DurationMs = 300,
                CostUsd = 0.08m,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-3", "claude", "claude.session-log", "src-2", day) {
                AccountLabel = "personal",
                Model = "claude-sonnet-4-5",
                Surface = "cli",
                InputTokens = 50,
                CachedInputTokens = 4,
                OutputTokens = 12,
                TotalTokens = 66,
                DurationMs = 900,
                TruthLevel = UsageTruthLevel.Inferred
            }
        };

        var aggregates = builder.Build(
            events,
            new UsageDailyAggregateOptions {
                Dimensions = UsageAggregateDimensions.Provider |
                             UsageAggregateDimensions.Account |
                             UsageAggregateDimensions.Model |
                             UsageAggregateDimensions.Surface
            });

        AssertEqual(2, aggregates.Count, "aggregate count");

        var codex = aggregates.Single(aggregate => string.Equals(aggregate.ProviderId, "codex", StringComparison.Ordinal));
        AssertEqual(new DateTime(2026, 03, 11), codex.DayUtc, "codex aggregate day");
        AssertEqual("codex", codex.ProviderId, "codex aggregate provider");
        AssertEqual("acct:acct-1", codex.AccountKey, "codex aggregate account key");
        AssertEqual("acct-1", codex.ProviderAccountId, "codex aggregate provider account");
        AssertEqual("work", codex.AccountLabel, "codex aggregate account label");
        AssertEqual("gpt-5.4-codex", codex.Model, "codex aggregate model");
        AssertEqual("cli", codex.Surface, "codex aggregate surface");
        AssertEqual(2, codex.EventCount, "codex aggregate event count");
        AssertEqual(110L, codex.InputTokens, "codex aggregate input");
        AssertEqual(25L, codex.CachedInputTokens, "codex aggregate cached input");
        AssertEqual(48L, codex.OutputTokens, "codex aggregate output");
        AssertEqual(7L, codex.ReasoningTokens, "codex aggregate reasoning");
        AssertEqual(183L, codex.TotalTokens, "codex aggregate total");
        AssertEqual(1500L, codex.TotalDurationMs, "codex aggregate duration");
        AssertEqual(0.50m, codex.TotalCostUsd, "codex aggregate cost");
        AssertEqual(UsageTruthLevel.Exact, codex.TruthLevel, "codex aggregate truth");

        var claude = aggregates.Single(aggregate => string.Equals(aggregate.ProviderId, "claude", StringComparison.Ordinal));
        AssertEqual("claude", claude.ProviderId, "claude aggregate provider");
        AssertEqual("label:personal", claude.AccountKey, "claude aggregate account key");
        AssertEqual("personal", claude.AccountLabel, "claude aggregate account label");
        AssertEqual("claude-sonnet-4-5", claude.Model, "claude aggregate model");
        AssertEqual(66L, claude.TotalTokens, "claude aggregate total");
        AssertEqual(UsageTruthLevel.Inferred, claude.TruthLevel, "claude aggregate truth");
    }

    private static void TestUsageDailyAggregateBuilderCanCollapseAcrossDimensions() {
        var builder = new UsageDailyAggregateBuilder();
        var events = new[] {
            new UsageEventRecord("evt-a", "codex", "codex.session-log", "src-a", new DateTimeOffset(2026, 03, 10, 23, 30, 0, TimeSpan.FromHours(-5))) {
                TotalTokens = 100,
                TruthLevel = UsageTruthLevel.Estimated
            },
            new UsageEventRecord("evt-b", "claude", "claude.session-log", "src-b", new DateTimeOffset(2026, 03, 11, 3, 0, 0, TimeSpan.Zero)) {
                TotalTokens = 50,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var aggregates = builder.Build(
            events,
            new UsageDailyAggregateOptions {
                Dimensions = UsageAggregateDimensions.None
            });

        AssertEqual(1, aggregates.Count, "collapsed aggregate count");
        AssertEqual(new DateTime(2026, 03, 11), aggregates[0].DayUtc, "collapsed aggregate UTC day");
        AssertEqual(2, aggregates[0].EventCount, "collapsed aggregate event count");
        AssertEqual(150L, aggregates[0].TotalTokens, "collapsed aggregate total");
        AssertEqual(UsageTruthLevel.Exact, aggregates[0].TruthLevel, "collapsed aggregate truth");
        AssertEqual(null, aggregates[0].ProviderId, "collapsed aggregate provider");
        AssertEqual(null, aggregates[0].AccountKey, "collapsed aggregate account key");
    }
}
