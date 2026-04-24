using System;
using System.IO;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryApiPricingBlendsExactAndEstimatedCosts() {
        var events = new[] {
            new UsageEventRecord("evt-priced", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 9, 0, 0, TimeSpan.Zero)) {
                Model = "gpt-5.4",
                InputTokens = 1_000_000,
                CachedInputTokens = 200_000,
                OutputTokens = 100_000,
                TotalTokens = 1_300_000,
                CostUsd = 4.20m,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-estimated", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 10, 0, 0, TimeSpan.Zero)) {
                Model = "gpt-5.3-codex",
                InputTokens = 2_000_000,
                CachedInputTokens = 500_000,
                OutputTokens = 300_000,
                TotalTokens = 2_800_000,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(events);
        AssertEqual(4.20m, displayCost.ExactCostUsd, "api pricing exact cost portion");
        AssertEqual(7.7875m, displayCost.EstimatedFallbackCostUsd, "api pricing estimated fallback portion");
        AssertEqual(11.9875m, displayCost.TotalCostUsd, "api pricing blended total");
        AssertEqual(true, displayCost.UsesEstimatedFallback, "api pricing indicates approximation");

        var estimate = UsageTelemetryApiPricing.Estimate(events);
        AssertNotNull(estimate, "api pricing estimate exists");
        AssertEqual(11.8375m, estimate!.TotalEstimatedCostUsd, "api pricing pure estimate total");
        AssertEqual(4_100_000L, estimate.CoveredTokens, "api pricing covered tokens");
    }

    private static void TestUsageTelemetryApiPricingCoversOpenAiModeSuffixes() {
        var evt = new UsageEventRecord("evt-fast", "openai", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 11, 0, 0, TimeSpan.Zero)) {
            Model = "openai/gpt-5.5/fast/spark",
            InputTokens = 1_000_000,
            CachedInputTokens = 100_000,
            OutputTokens = 100_000,
            ReasoningTokens = 50_000,
            TotalTokens = 1_250_000,
            TruthLevel = UsageTruthLevel.Exact
        };

        var eventEstimate = UsageTelemetryApiPricing.EstimateEvent(evt);
        AssertEqual(true, eventEstimate.HasKnownPricing, "api pricing recognizes openai fast suffix");
        AssertEqual("gpt-5.5/fast/spark", eventEstimate.Model, "api pricing normalizes openai compound mode suffix");
        AssertEqual(9.55m, eventEstimate.EstimatedCostUsd, "api pricing estimates gpt-5.5 compound mode from base model");

        var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(evt);
        AssertEqual(9.55m, displayCost.EstimatedFallbackCostUsd, "api pricing display cost includes gpt-5.5 fast");
        AssertEqual(1_250_000L, displayCost.CoveredTokens, "api pricing display cost covers gpt-5.5 fast tokens");
        AssertEqual(0L, displayCost.UncoveredTokens, "api pricing display cost leaves no gpt-5.5 fast tokens uncovered");
    }

    private static void TestProviderLimitForecastingFlagsOverLimitPace() {
        var now = new DateTimeOffset(2026, 03, 18, 12, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "codex@example.com",
            new[] {
                new ProviderLimitWindow(
                    "global-primary",
                    "Global 5-hour",
                    60d,
                    now.AddHours(2.5),
                    windowDuration: TimeSpan.FromHours(5))
            },
            null,
            null,
            now);

        var forecasts = ProviderLimitForecasting.BuildForecasts(snapshot, now);
        AssertEqual(1, forecasts.Count, "forecast count");
        AssertEqual(true, forecasts.TryGetValue("global-primary", out var forecast), "forecast exists");
        AssertNotNull(forecast, "forecast value");
        AssertEqual(true, forecast!.ExhaustsBeforeReset, "forecast flags exhaustion");
        AssertEqual(1.2d, Math.Round(forecast.PaceMultiple, 1), "forecast pace multiple");
        AssertEqual(120d, Math.Round(forecast.ProjectedUsedPercentAtReset, 0), "forecast projected percent");
        AssertContainsText(forecast.Summary ?? string.Empty, "1.2x sustainable pace", "forecast summary pace");
    }

    private static void TestProviderLimitForecastingRecognizesOnPaceWindow() {
        var now = new DateTimeOffset(2026, 03, 18, 12, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "codex@example.com",
            new[] {
                new ProviderLimitWindow(
                    "global-primary",
                    "Global 5-hour",
                    50d,
                    now.AddHours(2.5),
                    windowDuration: TimeSpan.FromHours(5))
            },
            null,
            null,
            now);

        var forecasts = ProviderLimitForecasting.BuildForecasts(snapshot, now);
        AssertEqual(true, forecasts.TryGetValue("global-primary", out var forecast), "forecast exists on pace");
        AssertNotNull(forecast, "forecast value on pace");
        AssertEqual(false, forecast!.ExhaustsBeforeReset, "forecast avoids false exhaustion");
        AssertEqual(1.0d, Math.Round(forecast.PaceMultiple, 1), "forecast on pace multiple");
        AssertContainsText(forecast.Summary ?? string.Empty, "On pace", "forecast on pace summary");
    }

    private static void TestProviderLimitForecastingRanksBestAccount() {
        var now = new DateTimeOffset(2026, 03, 18, 12, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            new[] {
                new ProviderLimitWindow(
                    "global-primary",
                    "Global 5-hour",
                    70d,
                    now.AddHours(1),
                    windowDuration: TimeSpan.FromHours(5))
            },
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-primary", "Global 5-hour", 95d, now.AddMinutes(30), windowDuration: TimeSpan.FromHours(5))
                    },
                    null,
                    null,
                    now,
                    isSelected: true),
                new ProviderLimitAccountSnapshot(
                    "acct-b",
                    "acct-b@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-primary", "Global 5-hour", 15d, now.AddHours(4), windowDuration: TimeSpan.FromHours(5))
                    },
                    null,
                    null,
                    now)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(2, advisories.Count, "account advisory count");
        AssertEqual(true, advisories[0].IsRecommended, "first advisory recommended");
        AssertEqual("acct-b@example.com", advisories[0].DisplayLabel, "lower-risk account recommended");
        AssertEqual(false, advisories[1].IsRecommended, "second advisory not recommended");
    }

    private static void TestProviderLimitForecastingDescribesAccountRunway() {
        var now = new DateTimeOffset(2026, 03, 18, 12, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            new[] {
                new ProviderLimitWindow(
                    "global-primary",
                    "Global 5-hour",
                    60d,
                    now.AddHours(2.5),
                    windowDuration: TimeSpan.FromHours(5))
            },
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-primary", "Global 5-hour", 60d, now.AddHours(2.5), windowDuration: TimeSpan.FromHours(5))
                    },
                    null,
                    null,
                    now,
                    isSelected: true)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(1, advisories.Count, "account runway advisory count");
        AssertContainsText(advisories[0].Summary ?? string.Empty, "If you keep this pace", "account runway summary prefix");
        AssertContainsText(advisories[0].Summary ?? string.Empty, "runs out in", "account runway summary runout");
    }

    private static void TestProviderLimitForecastingKeepsUnavailableAccountsVisible() {
        var now = new DateTimeOffset(2026, 03, 18, 12, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            new[] {
                new ProviderLimitWindow(
                    "global-primary",
                    "Global 5-hour",
                    60d,
                    now.AddHours(2.5),
                    windowDuration: TimeSpan.FromHours(5))
            },
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-primary", "Global 5-hour", 60d, now.AddHours(2.5), windowDuration: TimeSpan.FromHours(5))
                    },
                    null,
                    null,
                    now,
                    isSelected: true),
                new ProviderLimitAccountSnapshot(
                    "acct-b",
                    "acct-b@example.com",
                    "pro",
                    Array.Empty<ProviderLimitWindow>(),
                    "Detected locally, but live limits are unavailable.",
                    "Local login expired on Mar 17 22:05. Reauthenticate this account to load live limits.",
                    now)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(2, advisories.Count, "unavailable account advisory count");
        AssertEqual("acct-a@example.com", advisories[0].DisplayLabel, "available account stays first");
        AssertEqual("acct-b@example.com", advisories[1].DisplayLabel, "unavailable account remains visible");
        AssertEqual("Unavailable", advisories[1].StatusLabel, "unavailable account status");
        AssertContainsText(advisories[1].Summary ?? string.Empty, "Detected locally", "unavailable account summary keeps detection context");
    }

    private static void TestProviderLimitForecastingUsesWatchCloselyForPaceRisk() {
        var now = new DateTimeOffset(2026, 03, 19, 10, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            Array.Empty<ProviderLimitWindow>(),
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-secondary", "Global Weekly", 80d, now.AddDays(2), windowDuration: TimeSpan.FromDays(7))
                    },
                    null,
                    null,
                    now,
                    isSelected: true)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(1, advisories.Count, "pace-risk advisory count");
        AssertEqual("Watch closely", advisories[0].StatusLabel, "pace-risk status is watch closely");
        AssertContainsText(advisories[0].Summary ?? string.Empty, "runs out in", "pace-risk summary keeps runway wording");
    }

    private static void TestProviderLimitForecastingKeepsEarlyWeeklyPaceAsTight() {
        var now = new DateTimeOffset(2026, 03, 23, 12, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            Array.Empty<ProviderLimitWindow>(),
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-secondary", "Global Weekly", 18d, now.AddDays(6), windowDuration: TimeSpan.FromDays(7))
                    },
                    null,
                    null,
                    now,
                    isSelected: true)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(1, advisories.Count, "early-weekly advisory count");
        AssertEqual("Tight", advisories[0].StatusLabel, "early weekly pace stays tight instead of watch closely");
        AssertContainsText(advisories[0].Summary ?? string.Empty, "Recent pace projects", "early weekly pace uses softer forecast wording");
    }

    private static void TestProviderLimitForecastingKeepsCurrentAccountWhenNotHardAvoid() {
        var now = new DateTimeOffset(2026, 03, 19, 10, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            Array.Empty<ProviderLimitWindow>(),
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-secondary", "Global Weekly", 14d, now.AddDays(6), windowDuration: TimeSpan.FromDays(7))
                    },
                    null,
                    null,
                    now,
                    isSelected: true),
                new ProviderLimitAccountSnapshot(
                    "acct-b",
                    "acct-b@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-secondary", "Global Weekly", 0d, now.AddDays(6), windowDuration: TimeSpan.FromDays(7))
                    },
                    null,
                    null,
                    now)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(2, advisories.Count, "keep-current advisory count");
        AssertEqual("acct-a@example.com", advisories[0].DisplayLabel, "selected account stays first when not hard avoid");
        AssertEqual(true, advisories[0].IsRecommended, "selected account remains recommended");
        AssertEqual("Tight", advisories[0].StatusLabel, "selected account stays in tight state");
    }

    private static void TestProviderLimitForecastingUsesLiveWindowWordingForZeroUsage() {
        var now = new DateTimeOffset(2026, 03, 29, 18, 00, 00, TimeSpan.Zero);
        var snapshot = new ProviderLimitSnapshot(
            "codex",
            "Codex",
            "OpenAI usage API",
            "pro",
            "acct-a@example.com",
            Array.Empty<ProviderLimitWindow>(),
            null,
            null,
            now,
            new[] {
                new ProviderLimitAccountSnapshot(
                    "acct-a",
                    "acct-a@example.com",
                    "pro",
                    new[] {
                        new ProviderLimitWindow("global-secondary", "Global Weekly", 0d, now.AddDays(6), windowDuration: TimeSpan.FromDays(7))
                    },
                    null,
                    null,
                    now,
                    isSelected: true)
            });

        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now);
        AssertEqual(1, advisories.Count, "zero-usage advisory count");
        AssertEqual("Clear", advisories[0].StatusLabel, "zero-usage status remains clear");
        AssertContainsText(advisories[0].Summary ?? string.Empty, "live windows", "zero-usage summary uses generalized live-window wording");
    }

    private static void TestUsageTelemetryOverviewBuilderBuildsCopilotActivitySectionWithoutTokens() {
        var builder = new UsageTelemetryOverviewBuilder();
        var events = new[] {
            new UsageEventRecord("evt-1", "copilot", "copilot.session-state", "src-1", new DateTimeOffset(2026, 03, 13, 22, 29, 29, TimeSpan.Zero)) {
                ProviderAccountId = "octocat",
                Surface = "cli",
                SessionId = "session-a",
                ThreadId = "session-a",
                TurnId = "0",
                Model = "claude-sonnet-4.6",
                DurationMs = 1010,
                TruthLevel = UsageTruthLevel.Inferred
            },
            new UsageEventRecord("evt-2", "copilot", "copilot.session-state", "src-1", new DateTimeOffset(2026, 03, 13, 22, 29, 30, TimeSpan.Zero)) {
                ProviderAccountId = "octocat",
                Surface = "cli-error",
                SessionId = "session-a",
                ThreadId = "session-a",
                Model = "claude-sonnet-4.6",
                TruthLevel = UsageTruthLevel.Inferred
            }
        };

        var overview = builder.Build(
            events,
            new UsageTelemetryOverviewOptions {
                Title = "Copilot Usage",
                Subtitle = "@octocat"
            });

        var section = overview.ProviderSections.Single();
        AssertEqual("GitHub Copilot", section.Title, "copilot section title");
        AssertEqual("Assistant turns", section.Metrics[0].Label, "copilot activity metric label");
        AssertEqual("1", section.Metrics[1].Value, "copilot active days metric value");
        AssertEqual("claude-sonnet-4.6", section.MostUsedModel?.Model, "copilot most used model");
        AssertContainsText(section.MostUsedModel?.ValueLabel ?? string.Empty, "turn", "copilot most used model uses turn label");
        AssertEqual(1, section.TopModels.Count, "copilot top model count");
        AssertContainsText(section.TopModels[0].ValueLabel ?? string.Empty, "turn", "copilot top model uses turn label");
        AssertEqual(null, section.Composition, "copilot token composition omitted without tokens");
        AssertEqual(null, section.ApiCostEstimate, "copilot api estimate omitted without tokens");
        AssertEqual("Monthly activity", section.MonthlyUsageTitle, "copilot monthly activity title");
        AssertEqual("turns", section.MonthlyUsageUnitsLabel, "copilot monthly activity units");
        AssertContainsText(section.Note ?? string.Empty, "quota failure", "copilot section note includes quota failures");
        AssertEqual(1, section.AdditionalInsights.Count, "copilot activity insight count");
        AssertEqual("copilot-cli-activity", section.AdditionalInsights[0].Key, "copilot activity insight key");
    }

    private static void TestUsageTelemetryOverviewBuilderBuildsCardsAndHeatmaps() {
        var builder = new UsageTelemetryOverviewBuilder();
        var events = new[] {
            new UsageEventRecord("evt-1", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 9, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "cli",
                Model = "gpt-5-codex",
                InputTokens = 900,
                OutputTokens = 300,
                TotalTokens = 1200,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-2", "claude", "claude.logs", "src-2", new DateTimeOffset(2026, 03, 11, 11, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "lab",
                PersonLabel = "Przemek",
                Surface = "chat",
                Model = "claude-opus",
                InputTokens = 410,
                OutputTokens = 90,
                TotalTokens = 500,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-3", "ix", "ix.client-turn", "src-3", new DateTimeOffset(2026, 03, 11, 15, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "reviewer",
                Model = "gpt-5.4",
                InputTokens = 240,
                OutputTokens = 60,
                TotalTokens = 300,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var overview = builder.Build(
            events,
            new UsageTelemetryOverviewOptions {
                Title = "Combined Usage",
                Subtitle = "person: Przemek"
            });

        AssertEqual("Combined Usage", overview.Title, "usage overview title");
        AssertContainsText(overview.Subtitle ?? string.Empty, "person: Przemek", "usage overview subtitle prefix");
        AssertContainsText(overview.Subtitle ?? string.Empty, "2000 tokens", "usage overview subtitle total");
        AssertEqual("tokens", overview.Units, "usage overview units");
        AssertEqual(true, overview.Cards.Count >= 5, "usage overview card count");
        AssertEqual("2000", overview.Cards[0].Value, "usage overview total card value");
        AssertEqual("2026-03-10", overview.Cards.Single(card => card.Key == "peak_day").Value, "usage overview peak day card");
        AssertEqual(6, overview.Heatmaps.Count, "usage overview heatmap count");
        AssertEqual(3, overview.ProviderSections.Count, "usage overview provider section count");
        AssertEqual("Codex", overview.ProviderSections[0].Title, "usage overview first provider section title");
        AssertEqual(900L, overview.ProviderSections[0].InputTokens, "usage overview codex input tokens");
        AssertEqual(300L, overview.ProviderSections[0].OutputTokens, "usage overview codex output tokens");
        AssertEqual("2025-03-12 to 2026-03-10", overview.ProviderSections[0].Subtitle, "usage overview codex trailing year subtitle");
        AssertEqual("gpt-5-codex", overview.ProviderSections[0].MostUsedModel?.Model, "usage overview codex most used model");
        AssertEqual(1, overview.ProviderSections[0].LongestStreakDays, "usage overview codex longest streak");
        AssertEqual("provider-codex", overview.ProviderSections[0].Key, "usage overview codex section key");
        AssertEqual(1, overview.ProviderSections[0].Heatmap.Sections.Count, "usage overview codex single heatmap section");
        AssertEqual(13, overview.ProviderSections[0].MonthlyUsage.Count, "usage overview codex monthly usage count");
        AssertEqual("2025-03", overview.ProviderSections[0].MonthlyUsage[0].Key, "usage overview codex monthly usage first key");
        AssertEqual(1200L, overview.ProviderSections[0].MonthlyUsage[12].TotalValue, "usage overview codex monthly usage last total");
        AssertEqual(1, overview.ProviderSections[0].TopModels.Count, "usage overview codex top model count");
        AssertEqual("gpt-5-codex", overview.ProviderSections[0].TopModels[0].Model, "usage overview codex top model");
        AssertEqual(true, overview.ProviderSections[0].ApiCostEstimate is not null, "usage overview codex api estimate exists");
        AssertEqual(0.004125m, overview.ProviderSections[0].ApiCostEstimate?.TotalEstimatedCostUsd ?? 0m, "usage overview codex api estimate total");
        AssertEqual(new DateTime(2025, 03, 12), overview.ProviderSections[0].Heatmap.Sections[0].Days[0].Date, "usage overview codex heatmap first padded day");
        AssertEqual(new DateTime(2026, 03, 10), overview.ProviderSections[0].Heatmap.Sections[0].Days[overview.ProviderSections[0].Heatmap.Sections[0].Days.Count - 1].Date, "usage overview codex heatmap last padded day");
        AssertEqual("surface", overview.Heatmaps[0].Key, "usage overview first heatmap key");
        AssertContainsText(overview.Heatmaps.Single(heatmap => heatmap.Key == "provider").Document.Title, "By telemetry source", "usage overview provider heatmap title");
        var json = JsonLite.Serialize(JsonValue.From(overview.ToJson()));
        AssertContainsText(json, "\"key\":\"person\"", "usage overview json person heatmap");
        AssertContainsText(json, "\"providerSections\":[", "usage overview json provider sections");
        AssertContainsText(json, "\"title\":\"Codex\"", "usage overview json codex provider section");
        AssertContainsText(json, "\"monthlyUsage\":[", "usage overview json provider monthly usage");
        AssertContainsText(json, "\"topModels\":[", "usage overview json provider top models");
        AssertContainsText(json, "\"apiCostEstimate\":", "usage overview json provider api estimate");
    }

    private static void TestUsageTelemetryOverviewBuilderEstimatesApiCostForMiniAndNanoModels() {
        var builder = new UsageTelemetryOverviewBuilder();
        var events = new[] {
            new UsageEventRecord("evt-mini", "ix", "ix.client-turn", "src-mini", new DateTimeOffset(2026, 03, 14, 10, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "chat",
                Model = "gpt-5-mini",
                InputTokens = 1000,
                OutputTokens = 200,
                TotalTokens = 1200,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-nano", "ix", "ix.client-turn", "src-nano", new DateTimeOffset(2026, 03, 15, 10, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "chat",
                Model = "gpt-5-nano",
                InputTokens = 2000,
                OutputTokens = 100,
                TotalTokens = 2100,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var overview = builder.Build(
            events,
            new UsageTelemetryOverviewOptions {
                Title = "Mini Nano Usage"
            });

        var section = overview.ProviderSections.Single(providerSection => string.Equals(providerSection.ProviderId, "ix", StringComparison.OrdinalIgnoreCase));
        AssertNotNull(section.ApiCostEstimate, "usage overview mini/nano api estimate exists");
        AssertEqual(0.00079m, section.ApiCostEstimate?.TotalEstimatedCostUsd ?? 0m, "usage overview mini/nano api estimate total");
        AssertEqual(2, section.ApiCostEstimate?.TopDrivers.Count ?? 0, "usage overview mini/nano api estimate driver count");
        AssertEqual("gpt-5-mini", section.ApiCostEstimate?.TopDrivers[0].Model, "usage overview mini/nano top cost driver");
        AssertEqual("gpt-5-nano", section.TopModels[0].Model, "usage overview mini/nano top model");
        AssertEqual("gpt-5-nano", section.MostUsedModel?.Model, "usage overview mini/nano most used model");
    }

    private static void TestGitHubWrappedHtmlRendererBuildsShareablePage() {
        var section = CreateSampleGitHubOverviewSection();

        var html = GitHubWrappedHtmlRenderer.Render(section);
        AssertContainsText(html, "GitHub Wrapped", "github wrapped title");
        AssertContainsText(html, "github-wrapped-shared.css", "github wrapped shared css");
        AssertContainsText(html, "github-wrapped.css", "github wrapped external css");
        AssertContainsText(html, "report-runtime.js", "github wrapped shared runtime js");
        AssertContainsText(html, "github-wrapped.js", "github wrapped external js");
        AssertContainsText(html, "provider-github.dark.svg", "github wrapped heatmap image");
        AssertContainsText(html, "11.8K contributions", "github wrapped current year value");
        AssertContainsText(html, "567 contributions", "github wrapped previous year value");
        AssertContainsText(html, "EvotecIT/GPOZaurr", "github wrapped top repo");
        AssertContainsText(html, "badge rising", "github wrapped rising badge");
        AssertContainsText(html, "class=\"hero-fact wrapped-copy\"", "github wrapped hero fact chips");
        AssertContainsText(html, "Profile vs Correlated Scope", "github wrapped correlated scope card");
        AssertContainsText(html, "github-wrapped-card.html", "github wrapped share card link");
        AssertContainsText(html, "data-owner-panel=\"github-owner-evotecit\"", "github wrapped owner chip");
        AssertContainsText(html, "data-owner-panel-content=\"github-owner-przemyslawklys\"", "github wrapped owner panel");
        AssertEqual(false, html.Contains("<style>", StringComparison.Ordinal), "github wrapped no inline style block");
        AssertEqual(false, html.Contains("const ownerChips", StringComparison.Ordinal), "github wrapped no inline js");
    }

    private static void TestGitHubWrappedCardHtmlRendererBuildsCompactCard() {
        var section = CreateSampleGitHubOverviewSection();

        var html = GitHubWrappedCardHtmlRenderer.Render(section);
        AssertContainsText(html, "GitHub Wrapped Card", "github wrapped card title");
        AssertContainsText(html, "github-wrapped-shared.css", "github wrapped card shared css");
        AssertContainsText(html, "github-wrapped-card.css", "github wrapped card external css");
        AssertContainsText(html, "provider-github.dark.svg", "github wrapped card heatmap image");
        AssertContainsText(html, "EvotecIT/GPOZaurr", "github wrapped card top repository");
        AssertContainsText(html, "8.99K stars", "github wrapped card owner scope");
        AssertContainsText(html, "2026 YTD +1989.1% vs 2025", "github wrapped card year comparison");
        AssertEqual(false, html.Contains("<style>", StringComparison.Ordinal), "github wrapped card no inline style block");
    }

    private static void TestUsageTelemetryOverviewHtmlRendererBuildsProviderDiagnostics() {
        var section = new UsageTelemetryOverviewProviderSection(
            key: "provider-codex",
            providerId: "codex",
            title: "Codex",
            subtitle: "2026-02-18 to 2026-03-18",
            heatmap: new HeatmapDocument(
                title: "Codex activity",
                subtitle: null,
                palette: HeatmapPalette.ChatGptDark(),
                sections: new[] {
                    new HeatmapSection(
                        "2026",
                        null,
                        new[] {
                            new HeatmapDay(new DateTime(2026, 03, 17), 12, level: 3),
                            new HeatmapDay(new DateTime(2026, 03, 18), 18, level: 4)
                        })
                }),
            rangeStartUtc: new DateTime(2026, 02, 18),
            rangeEndUtc: new DateTime(2026, 03, 18),
            latestEventUtc: new DateTimeOffset(2026, 03, 18, 14, 23, 0, TimeSpan.Zero),
            activeDays: 17,
            totalDays: 29,
            accountCount: 2,
            sourceRootCount: 3,
            accountLabels: new[] { "work@evotec.pl", "lab@evotec.pl" },
            metrics: new[] {
                new UsageTelemetryOverviewSectionMetric("total", "Total tokens", "42.3B", "30-day window", 1d, "#6268f1")
            },
            composition: null,
            spotlightCards: Array.Empty<UsageTelemetryOverviewCard>(),
            inputTokens: 4200,
            outputTokens: 120,
            totalTokens: 4320,
            monthlyUsageTitle: "Monthly usage",
            monthlyUsageUnitsLabel: "tokens",
            monthlyUsage: new[] {
                new UsageTelemetryOverviewMonthlyUsage(new DateTime(2026, 03, 1, 0, 0, 0, DateTimeKind.Utc), 4320, 17)
            },
            additionalInsights: new[] {
                new UsageTelemetryOverviewInsightSection(
                    "source-roots",
                    "Scanned roots",
                    "3 root(s) included in this quick scan",
                    "Includes current, WSL, and recovered roots.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("Local", "1", ".codex/sessions"),
                        new UsageTelemetryOverviewInsightRow("WSL", "1", "Ubuntu/.codex/sessions"),
                        new UsageTelemetryOverviewInsightRow("Recovered", "1", "Windows.old/.codex/sessions")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "quick-scan-dedupe",
                    "Quick-scan dedupe",
                    "2 duplicate records collapsed before aggregation",
                    "Useful when copied session logs exist across roots.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("Duplicates collapsed", "2", "Cross-root copies merged"),
                        new UsageTelemetryOverviewInsightRow("Raw events", "14", "Before dedupe")
                    })
            },
            topModels: new[] {
                new UsageTelemetryOverviewTopModel("gpt-5.4", 4200, 0.97d)
            },
            apiCostEstimate: null,
            mostUsedModel: new UsageTelemetryOverviewModelHighlight("gpt-5.4", 4200),
            recentModel: new UsageTelemetryOverviewModelHighlight("gpt-5.4", 1200),
            longestStreakDays: 7,
            currentStreakDays: 3,
            note: "Scanned roots: 1 local, 1 wsl, 1 recovered");
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 11),
            PeakValue = 18m
        };

        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section });

        var html = UsageTelemetryOverviewHtmlRenderer.Render(overview);
        AssertContainsText(html, "Provider health", "overview provider diagnostics title");
        AssertContainsText(html, "Telemetry mode", "overview provider diagnostics mode label");
        AssertContainsText(html, "Latest provider day", "overview provider diagnostics latest label");
        AssertContainsText(html, "Inspect roots and account scope", "overview provider diagnostics explorer title");
        AssertContainsText(html, "work@evotec.pl", "overview provider diagnostics account chip");
        AssertContainsText(html, "Scanned roots", "overview provider diagnostics roots insight");
        AssertContainsText(html, "Quick-scan dedupe", "overview provider diagnostics dedupe insight");
    }

    private static void TestUsageTelemetryOverviewHtmlRendererBuildsConversationPulse() {
        var items = new JsonArray()
            .Add(new JsonObject()
                .Add("label", "session-a")
                .Add("title", "Audit usage context")
                .Add("repository", "EvotecIT/IntelligenceX")
                .Add("workspace", "IntelligenceX")
                .Add("account", "work@evotec.pl")
                .Add("startedLocal", "Mar 18 14:00")
                .Add("duration", "1h 05m")
                .Add("activeDuration", "4m 12s")
                .Add("turnCount", 7)
                .Add("totalTokens", 12_345_678)
                .Add("models", new JsonArray().Add("gpt-5.4"))
                .Add("surfaces", new JsonArray().Add("cli")))
            .Add(new JsonObject()
                .Add("label", "session-b")
                .Add("title", "Compact-heavy follow-up")
                .Add("repository", "EvotecIT/IntelligenceX")
                .Add("workspace", "IntelligenceX")
                .Add("account", "work@evotec.pl")
                .Add("startedLocal", "Mar 18 16:00")
                .Add("duration", "18m")
                .Add("activeDuration", "5m")
                .Add("turnCount", 4)
                .Add("compactCount", 2)
                .Add("totalTokens", 2_500_000)
                .Add("apiEquivalentCostUsd", 12.34d)
                .Add("costApproximate", true)
                .Add("models", new JsonArray().Add("gpt-5.4"))
                .Add("surfaces", new JsonArray().Add("cli")));
        var metadata = new JsonObject()
            .Add("conversations", new JsonObject()
                .Add("totalCount", 2)
                .Add("shownCount", 2)
                .Add("tokenTotal", 14_845_678)
                .Add("turnCount", 11)
                .Add("compactCount", 2)
                .Add("items", items));
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "Tray explorer",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: new UsageSummarySnapshot {
                Metric = UsageSummaryMetric.TotalTokens,
                StartDayUtc = new DateTime(2026, 03, 18),
                EndDayUtc = new DateTime(2026, 03, 18),
                TotalValue = 12_345_678m,
                TotalDays = 1,
                ActiveDays = 1,
                PeakDayUtc = new DateTime(2026, 03, 18),
                PeakValue = 12_345_678m
            },
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: Array.Empty<UsageTelemetryOverviewProviderSection>(),
            metadata: metadata);

        var html = UsageTelemetryOverviewHtmlRenderer.Render(overview);
        AssertContainsText(html, "Conversation usage", "overview conversation pulse title");
        AssertContainsText(html, "Raw sessions", "overview conversation pulse feature kicker");
        AssertContainsText(html, "Audit usage context", "overview conversation pulse session title");
        AssertContainsText(html, "EvotecIT/IntelligenceX", "overview conversation pulse repository");
        AssertContainsText(html, "session-a", "overview conversation pulse session label");
        AssertContainsText(html, "12.3M tokens", "overview conversation pulse token value");
        AssertContainsText(html, "span 1h 05m", "overview conversation pulse wall duration");
        AssertContainsText(html, "active 4m 12s", "overview conversation pulse active duration");
        AssertContainsText(html, "7 turns", "overview conversation pulse turn count");
        AssertContainsText(html, "2 compacts", "overview conversation pulse compact count");
        AssertContainsText(html, "work@evotec.pl", "overview conversation pulse account");
        AssertContainsText(html, "gpt-5.4", "overview conversation pulse model");
        AssertContainsText(html, "~$12.34", "overview conversation pulse estimated cost");
        AssertContainsText(html, "Selected conversation", "overview conversation pulse detail card");
        AssertContainsText(html, "data-conversation-button", "overview conversation pulse selectable row");
        AssertContainsText(html, "data-conversation-sort=\"tokens\"", "overview conversation pulse sort tabs");
        AssertContainsText(html, "data-conversation-search", "overview conversation pulse search input");
        AssertContainsText(html, "data-conversation-filter-group=\"named\"", "overview conversation pulse quick filters");
        AssertContainsText(html, "data-conversation-filter-group=\"profile\"", "overview conversation pulse signal filters");
        AssertContainsText(html, "data-conversation-reset", "overview conversation pulse reset control");
        AssertContainsText(html, "data-conversation-active-chips", "overview conversation pulse active filters");
        AssertContainsText(html, "name=\"conversation-search\"", "overview conversation pulse search name");
        AssertContainsText(html, "data-conversation-snapshot-count", "overview conversation pulse snapshot count");
        AssertContainsText(html, "data-conversation-snapshot-cost", "overview conversation pulse snapshot cost");
        AssertContainsText(html, "data-conversation-snapshot-context", "overview conversation pulse snapshot context");
        AssertContainsText(html, "data-conversation-context-list", "overview conversation pulse context breakdown");
        AssertContainsText(html, "data-conversation-context-lens=\"cost\"", "overview conversation pulse context cost lens");
        AssertContainsText(html, "Context Breakdown", "overview conversation pulse context breakdown title");
        AssertContainsText(html, "data-detail-active", "overview conversation pulse detail active metric shell");
        AssertContainsText(html, "data-detail-compacts", "overview conversation pulse detail compact metric shell");
        AssertContainsText(html, "data-detail-cost", "overview conversation pulse detail cost metric shell");
    }

    private static void TestUsageTelemetryOverviewHtmlRendererUsesShownConversationShare() {
        var items = new JsonArray()
            .Add(new JsonObject()
                .Add("label", "session-01")
                .Add("title", "Largest visible session")
                .Add("repository", "EvotecIT/IntelligenceX")
                .Add("workspace", "IntelligenceX")
                .Add("account", "work@evotec.pl")
                .Add("startedLocal", "Mar 20 09:00")
                .Add("duration", "45m")
                .Add("turnCount", 9)
                .Add("totalTokens", 60));

        for (var i = 2; i <= 11; i++) {
            items.Add(new JsonObject()
                .Add("label", "session-" + i.ToString("00"))
                .Add("title", "Visible session " + i)
                .Add("repository", "EvotecIT/IntelligenceX")
                .Add("workspace", "IntelligenceX")
                .Add("account", "work@evotec.pl")
                .Add("startedLocal", "Mar 20 10:00")
                .Add("duration", "12m")
                .Add("turnCount", 2)
                .Add("totalTokens", 5));
        }

        items.Add(new JsonObject()
            .Add("label", "session-12")
            .Add("title", "Visible session 12")
            .Add("repository", "EvotecIT/IntelligenceX")
            .Add("workspace", "IntelligenceX")
            .Add("account", "work@evotec.pl")
            .Add("startedLocal", "Mar 20 10:30")
            .Add("duration", "14m")
            .Add("turnCount", 3)
            .Add("totalTokens", 10));
        items.Add(new JsonObject()
            .Add("label", "session-13")
            .Add("title", "Hidden tail session")
            .Add("repository", "EvotecIT/IntelligenceX")
            .Add("workspace", "IntelligenceX")
            .Add("account", "work@evotec.pl")
            .Add("startedLocal", "Mar 20 11:00")
            .Add("duration", "20m")
            .Add("turnCount", 4)
            .Add("totalTokens", 80));

        var metadata = new JsonObject()
            .Add("conversations", new JsonObject()
                .Add("totalCount", 13)
                .Add("shownCount", 13)
                .Add("tokenTotal", 200)
                .Add("turnCount", 36)
                .Add("compactCount", 0)
                .Add("items", items));
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "Tray explorer",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: new UsageSummarySnapshot {
                Metric = UsageSummaryMetric.TotalTokens,
                StartDayUtc = new DateTime(2026, 03, 20),
                EndDayUtc = new DateTime(2026, 03, 20),
                TotalValue = 200m,
                TotalDays = 1,
                ActiveDays = 1,
                PeakDayUtc = new DateTime(2026, 03, 20),
                PeakValue = 200m
            },
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: Array.Empty<UsageTelemetryOverviewProviderSection>(),
            metadata: metadata);

        var html = UsageTelemetryOverviewHtmlRenderer.Render(overview);
        AssertContainsText(html, "Top 12 cover 60% of conversation tokens", "overview conversation pulse visible coverage headline");
        AssertContainsText(html, "50% of shown total", "overview conversation pulse row share uses displayed subset total");
    }

    private static void TestUsageTelemetryBreakdownHtmlRendererUsesSharedAssets() {
        var heatmap = new HeatmapDocument(
            title: "By telemetry source",
            subtitle: "Detailed breakdown view.",
            palette: HeatmapPalette.ChatGptDark(),
            sections: new[] {
                new HeatmapSection(
                    "2026",
                    null,
                    new[] {
                        new HeatmapDay(new DateTime(2026, 03, 10), 12, level: 3, breakdown: new Dictionary<string, double> {
                            ["codex"] = 8,
                            ["claude"] = 4
                        }),
                        new HeatmapDay(new DateTime(2026, 03, 11), 18, level: 4, breakdown: new Dictionary<string, double> {
                            ["codex"] = 10,
                            ["claude"] = 8
                        })
                    })
            },
            legendItems: new[] {
                new HeatmapLegendItem("codex", "Codex", "#6268f1"),
                new HeatmapLegendItem("claude", "Claude", "#fb8c1d")
            });

        var html = UsageTelemetryBreakdownHtmlRenderer.Render(
            "Usage Overview",
            "provider",
            "By telemetry source",
            "Detailed breakdown view.",
            heatmap);

        AssertContainsText(html, "report-runtime.js", "breakdown shared runtime js");
        AssertContainsText(html, "report-shell.css", "breakdown shared shell css");
        AssertContainsText(html, "breakdown.css", "breakdown external css");
        AssertContainsText(html, "breakdown.js", "breakdown external js");
        AssertContainsText(html, "\"defaultTheme\":\"system\"", "breakdown bootstrap default theme");
        AssertContainsText(html, "Top categories", "breakdown server summary card");
        AssertContainsText(html, "Codex", "breakdown server summary label");
        AssertContainsText(html, "Back to overview", "breakdown back link label");
        AssertContainsText(html, "Compare usage across telemetry sources such as Codex, Claude, LM Studio, and future compatible providers.", "breakdown provider guide copy");
        AssertContainsText(html, "Chart SVG", "breakdown svg asset label");
        AssertContainsText(html, "Data JSON", "breakdown json asset label");
        AssertContainsText(html, "telemetry-source.light.svg", "breakdown telemetry-source light svg file");
        AssertContainsText(html, "telemetry-source.dark.svg", "breakdown telemetry-source dark svg file");
        AssertContainsText(html, "telemetry-source.json", "breakdown telemetry-source json file");
        AssertEqual(false, html.Contains("provider.light.svg", StringComparison.OrdinalIgnoreCase), "breakdown avoids raw provider key in asset file names");
        AssertEqual(false, html.Contains("<style>", StringComparison.Ordinal), "breakdown no inline style block");
        AssertEqual(false, html.Contains("fetch(`${ixBreakdownKey}.json`)", StringComparison.Ordinal), "breakdown no fetch-based summary js");
    }

    private static void TestUsageTelemetryBreakdownHtmlRendererAddsSourceFamilyBadges() {
        var heatmap = new HeatmapDocument(
            title: "By source root",
            subtitle: "Detailed breakdown view.",
            palette: HeatmapPalette.ChatGptDark(),
            sections: new[] {
                new HeatmapSection(
                    "2026",
                    null,
                    new[] {
                        new HeatmapDay(new DateTime(2026, 03, 10), 10, level: 3, breakdown: new Dictionary<string, double> {
                            ["Codex · Current (.codex/sessions)"] = 6,
                            ["Codex · WSL (Ubuntu/.codex/sessions)"] = 4
                        }),
                        new HeatmapDay(new DateTime(2026, 03, 11), 8, level: 2, breakdown: new Dictionary<string, double> {
                            ["Codex · Windows.old (.codex/sessions)"] = 8
                        })
                    })
            },
            legendItems: new[] {
                new HeatmapLegendItem("current", "Current", "#6268f1"),
                new HeatmapLegendItem("wsl", "WSL", "#22c55e"),
                new HeatmapLegendItem("windows.old", "Windows.old", "#f59e0b")
            });

        var html = UsageTelemetryBreakdownHtmlRenderer.Render(
            "Usage Overview",
            "sourceroot",
            "By source root",
            "by source root | 18 tokens | 2 active days | peak 2026-03-11 (8)",
            heatmap);

        AssertContainsText(html, "summary-row-badge tone-current", "source family current badge");
        AssertContainsText(html, "summary-row-badge tone-wsl", "source family wsl badge");
        AssertContainsText(html, "summary-row-badge tone-recovered", "source family recovered badge");
        AssertContainsText(html, "hero-chip-group", "source family hero chips container");
        AssertContainsText(html, "summary-row-badge hero-chip tone-current", "source family detail-page current hero chip");
        AssertContainsText(html, "summary-row-badge hero-chip tone-wsl", "source family detail-page wsl hero chip");
        AssertContainsText(html, "summary-row-badge hero-chip tone-recovered", "source family detail-page recovered hero chip");
        AssertContainsText(html, "Trace activity back to current machines, recovered Windows.old profiles, WSL homes, and imported source roots.", "source-root guide copy");
        AssertContainsText(html, "18 tokens &#183; 2 active days &#183; peak 2026-03-11 (8)", "source-root breakdown subtitle is cleaned");
        AssertContainsText(html, "source-root.light.svg", "source-root breakdown light svg file");
        AssertContainsText(html, "source-root.dark.svg", "source-root breakdown dark svg file");
        AssertContainsText(html, "source-root.json", "source-root breakdown json file");
        AssertEqual(false, html.Contains("by source root |", StringComparison.OrdinalIgnoreCase), "source-root breakdown avoids raw subtitle prefix");
        AssertEqual(false, html.Contains("sourceroot.light.svg", StringComparison.OrdinalIgnoreCase), "source-root breakdown avoids raw breakdown key in asset file names");
    }

    private static void TestUsageTelemetryOverviewHtmlRendererAddsSourceFamilyChipsToSupportingPanel() {
        var heatmap = new HeatmapDocument(
            title: "By source root",
            subtitle: "by source root · 18 tokens · 2 active days · peak 2026-03-11 (8)",
            palette: HeatmapPalette.ChatGptDark(),
            sections: new[] {
                new HeatmapSection(
                    "2026",
                    null,
                    new[] {
                        new HeatmapDay(new DateTime(2026, 03, 10), 10, level: 3, breakdown: new Dictionary<string, double> {
                            ["Codex · Current (.codex/sessions)"] = 6,
                            ["Codex · WSL (Ubuntu/.codex/sessions)"] = 4
                        }),
                        new HeatmapDay(new DateTime(2026, 03, 11), 8, level: 2, breakdown: new Dictionary<string, double> {
                            ["Codex · Windows.old (.codex/sessions)"] = 8
                        })
                    })
            },
            legendItems: new[] {
                new HeatmapLegendItem("current", "Current", "#6268f1"),
                new HeatmapLegendItem("wsl", "WSL", "#22c55e"),
                new HeatmapLegendItem("windows.old", "Windows.old", "#f59e0b")
            });

        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "person: Przemek",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: new UsageSummarySnapshot {
                Metric = UsageSummaryMetric.TotalTokens,
                StartDayUtc = new DateTime(2026, 03, 10),
                EndDayUtc = new DateTime(2026, 03, 11),
                TotalValue = 18m,
                TotalDays = 2,
                ActiveDays = 2,
                PeakDayUtc = new DateTime(2026, 03, 11),
                PeakValue = 8m
            },
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: new[] {
                new UsageTelemetryOverviewHeatmap("sourceroot", "By source root", heatmap)
            },
            providerSections: Array.Empty<UsageTelemetryOverviewProviderSection>());

        var html = UsageTelemetryOverviewHtmlRenderer.Render(overview);
        AssertContainsText(html, "supporting-family-chips", "overview supporting source family chips container");
        AssertContainsText(html, "summary-row-badge supporting-family-chip tone-current", "overview supporting current chip");
        AssertContainsText(html, "summary-row-badge supporting-family-chip tone-wsl", "overview supporting wsl chip");
        AssertContainsText(html, "summary-row-badge supporting-family-chip tone-recovered", "overview supporting recovered chip");
        AssertContainsText(html, "Trace activity back to current machines, recovered Windows.old profiles, WSL homes, and imported source roots.", "overview supporting source-root guide copy");
        AssertContainsText(html, "Breakdown page", "overview supporting detail link label");
        AssertContainsText(html, "Chart SVG", "overview supporting svg link label");
        AssertContainsText(html, "Data JSON", "overview supporting json link label");
        AssertContainsText(html, "source-root.html", "overview supporting source-root detail page href");
        AssertContainsText(html, "source-root.light.svg", "overview supporting source-root preview light svg");
        AssertContainsText(html, "source-root.json", "overview supporting source-root summary json");
        AssertContainsText(html, "18 tokens &#183; 2 active days &#183; peak 2026-03-11 (8)", "overview supporting source-root subtitle is cleaned");
        AssertEqual(false, html.Contains("by source root |", StringComparison.OrdinalIgnoreCase), "overview supporting source-root avoids raw subtitle prefix");
        AssertEqual(false, html.Contains("by source root &#183;", StringComparison.OrdinalIgnoreCase), "overview supporting source-root avoids bullet subtitle prefix");
        AssertEqual(false, html.Contains("sourceroot.html", StringComparison.OrdinalIgnoreCase), "overview supporting source-root avoids raw breakdown key in file names");
    }

    private static void TestUsageTelemetryOverviewHtmlRendererBuildsGitHubOwnerExplorer() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 11),
            PeakValue = 18m
        };

        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section });

        var html = UsageTelemetryOverviewHtmlRenderer.Render(overview);
        AssertContainsText(html, "report-runtime.js", "overview shared runtime js");
        AssertContainsText(html, "report-shell.css", "overview shared shell css");
        AssertContainsText(html, "data-github-lens=\"owners\"", "github main explorer owner lens");
        AssertContainsText(html, "data-github-owner=\"github-owner-evotecit\"", "github main explorer evotecit owner chip");
        AssertContainsText(html, "data-github-owner-content=\"github-owner-przemyslawklys\"", "github main explorer personal owner panel");
        AssertContainsText(html, "Owner lenses available in Impact", "github main explorer summary owner scope pills");
        AssertContainsText(html, "aria-label=\"GitHub owner scope summary\"", "github main summary owner explorer");
        AssertContainsText(html, "data-github-repo-sort=\"forks\"", "github main explorer repo sort forks");
        AssertContainsText(html, "data-github-repo-sort-content=\"health\"", "github main explorer repo sort health");
        AssertContainsText(html, "provider-note provider-note-chips", "github provider note chips container");
        AssertContainsText(html, "provider-note-chip\">Owner scope: EvotecIT, przemyslawklys</span>", "github provider note owner scope chip");
        AssertContainsText(html, "provider-note-chip\">8.99K stars across 118 public repositories</span>", "github provider note repository impact chip");
        AssertContainsText(html, "provider-note-chip\">Auto-correlated owners: EvotecIT</span>", "github provider note auto-correlated owners chip");
        AssertEqual(false, html.Contains("Loading summary", StringComparison.Ordinal), "overview no lazy summary placeholder");
        AssertEqual(false, html.Contains("fetch(`${key}.json`)", StringComparison.Ordinal), "overview no fetch-based supporting summary js");
        AssertEqual(false, html.Contains("color:inherit;text-decoration:none", StringComparison.Ordinal), "overview no inline anchor styling");
    }

    private static void TestUsageTelemetryPresentationHelpersBuildSourceRootLabels() {
        var codexRoot = new SourceRootRecord("src-codex", "codex", UsageSourceKind.LocalLogs, @"C:\Users\me\.codex\sessions");
        var claudeRoot = new SourceRootRecord("src-claude", "claude", UsageSourceKind.LocalLogs, "/home/me/.claude/projects");
        var lmStudioRoot = new SourceRootRecord("src-lmstudio", "lmstudio", UsageSourceKind.LocalLogs, @"C:\Users\me\.lmstudio\conversations");
        var recoveredRoot = new SourceRootRecord("src-old", "codex", UsageSourceKind.RecoveredFolder, @"C:\Windows.old\Users\me\.codex\sessions");
        var internalRoot = new SourceRootRecord("src-ix", "ix", UsageSourceKind.InternalIx, "ix://internal/reviewer");
        var internalChatGptRoot = new SourceRootRecord("src-chatgpt", "chatgpt", UsageSourceKind.InternalIx, "chatgpt://internal/devbox");
        var wslRoot = new SourceRootRecord("src-wsl", "codex", UsageSourceKind.LocalLogs, @"\\wsl$\Ubuntu\home\me\.codex\sessions") {
            PlatformHint = "wsl",
            MachineLabel = "Ubuntu"
        };

        var labels = UsageTelemetryPresentationHelpers.BuildSourceRootLabels(new[] {
            codexRoot,
            claudeRoot,
            lmStudioRoot,
            recoveredRoot,
            internalRoot,
            internalChatGptRoot,
            wslRoot
        });

        AssertEqual("Codex · Current (.codex/sessions)", labels["src-codex"], "source root codex label");
        AssertEqual("Claude · Current (.claude/projects)", labels["src-claude"], "source root claude label");
        AssertEqual("LM Studio · Current (.lmstudio/conversations)", labels["src-lmstudio"], "source root lmstudio label");
        AssertEqual("Codex · Windows.old (.codex/sessions)", labels["src-old"], "source root recovered label");
        AssertEqual("IntelligenceX · Internal", labels["src-ix"], "source root internal label");
        AssertEqual("ChatGPT · Internal", labels["src-chatgpt"], "source root internal chatgpt label");
        AssertEqual("Codex · WSL (Ubuntu/.codex/sessions)", labels["src-wsl"], "source root wsl label");
    }

    private static void TestUsageTelemetryPresentationHelpersDisambiguateDuplicateSourceRootLabels() {
        var primary = new SourceRootRecord("src-lmstudio-a", "lmstudio", UsageSourceKind.LocalLogs, @"C:\Users\me\.lmstudio");
        var secondary = new SourceRootRecord("src-lmstudio-b", "lmstudio", UsageSourceKind.LocalLogs, @"C:\Temp\debug-run\.lmstudio");
        var internalA = new SourceRootRecord("src-chatgpt-a", "chatgpt", UsageSourceKind.InternalIx, "chatgpt://internal/devbox");
        var internalB = new SourceRootRecord("src-chatgpt-b", "chatgpt", UsageSourceKind.InternalIx, "chatgpt://internal/laptop");

        var labels = UsageTelemetryPresentationHelpers.BuildSourceRootLabels(new[] {
            primary,
            secondary,
            internalA,
            internalB
        });

        AssertEqual(false, string.Equals(labels["src-lmstudio-a"], labels["src-lmstudio-b"], StringComparison.OrdinalIgnoreCase), "duplicate source root labels disambiguated");
        AssertContainsText(labels["src-lmstudio-b"], "debug-run/.lmstudio", "duplicate source root label path hint");
        AssertEqual("ChatGPT · Internal (devbox)", labels["src-chatgpt-a"], "duplicate internal source root first label");
        AssertEqual("ChatGPT · Internal (laptop)", labels["src-chatgpt-b"], "duplicate internal source root second label");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderBuildsGitHubRenderModel() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 11),
            PeakValue = 18m
        };

        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section });

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(overview);
        var sectionPage = page.Sections.Single();
        var github = sectionPage.GitHub;

        AssertEqual(true, github is not null, "github page model exists");
        AssertEqual(4, sectionPage.DatasetTabs.Count, "github page model dataset tab count");
        AssertEqual("summary", sectionPage.DatasetTabs[0].Key, "github page model first dataset tab");
        AssertEqual("wrapped", sectionPage.DatasetTabs[3].Key, "github page model wrapped tab");
        AssertEqual("github-wrapped.html", sectionPage.DatasetTabs[3].Href, "github page model wrapped tab href");
        AssertEqual(4, github?.Lenses.Count ?? 0, "github page model lens count");
        AssertEqual("impact", github?.Lenses[0].Key, "github page model first lens");
        AssertEqual(3, github?.RepoSortModes.Count ?? 0, "github page model repo sort count");
        AssertEqual("stars", github?.RepoSortModes[0].Key, "github page model first repo sort");
        AssertEqual(3, github?.OwnerScopes.Count ?? 0, "github page model owner scope count");
        AssertEqual("all", github?.OwnerScopes[0].Key, "github page model default owner scope");
        AssertEqual("github-year-comparison", github?.YearComparison?.Key, "github page model year comparison");
        AssertEqual("github-top-repositories", github?.TopRepositories?.Key, "github page model top repos");
        AssertEqual(2, github?.OwnerSections.Count ?? 0, "github page model owner sections");
    }

    private static void TestUsageTelemetryGitHubWrappedPageModelBuilderBuildsOwnerPanels() {
        var section = CreateSampleGitHubOverviewSection();

        var page = UsageTelemetryReportPageModelBuilders.BuildGitHubWrapped(section);

        AssertEqual("GitHub", page.Title, "github wrapped page title");
        AssertEqual("github-year-comparison", page.YearComparison?.Key, "github wrapped page year comparison");
        AssertEqual(2, page.OwnerPanels.Count, "github wrapped owner panel count");
        AssertEqual("github-owner-evotecit", page.OwnerPanels[0].Key, "github wrapped owner panel ordering");
        AssertContainsText(page.BootstrapJson, "\"defaultOwnerPanel\":\"all\"", "github wrapped bootstrap owner default");
    }

    private static void TestUsageTelemetryGitHubWrappedCardPageModelBuilderBuildsMetrics() {
        var section = CreateSampleGitHubOverviewSection();

        var page = UsageTelemetryReportPageModelBuilders.BuildGitHubWrappedCard(section);

        AssertEqual("GitHub", page.Title, "github wrapped card title");
        AssertEqual(4, page.Metrics.Count, "github wrapped card metric count");
        AssertEqual(4, page.Stats.Count, "github wrapped card stat count");
        AssertEqual(2, page.FooterMetrics.Count, "github wrapped card footer count");
        AssertEqual("Year over year", page.Stats[0].Label, "github wrapped card first stat label");
        AssertContainsText(page.Stats[0].Value, "2026 YTD", "github wrapped card first stat value");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsWatchedMomentumWhenProvided() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 11),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section });

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            CreateSampleGitHubObservabilitySummary());
        var github = page.Sections.Single().GitHub;

        AssertNotNull(github, "github page model exists with watched summary");
        AssertEqual(5, github!.Lenses.Count, "github page model lens count with watched summary");
        AssertEqual("watched", github.Lenses[2].Key, "github page model watched lens key");
        AssertEqual("github-watched-repositories", github.WatchedRepositories?.Key, "github watched repositories insight key");
        AssertEqual("github-watched-correlations", github.WatchedCorrelations?.Key, "github watched correlations insight key");
        AssertEqual("github-watched-star-correlations", github.WatchedStarCorrelations?.Key, "github watched star correlations insight key");
        AssertEqual("github-watched-repo-clusters", github.WatchedRepoClusters?.Key, "github watched repo clusters insight key");
        AssertEqual("github-watched-stargazer-audience", github.WatchedStargazerAudience?.Key, "github watched stargazer audience insight key");
        AssertContainsText(github.WatchedRepositories?.Headline ?? string.Empty, "+5 stars", "github watched repositories headline");
        AssertContainsText(github.WatchedCorrelations?.Headline ?? string.Empty, "Strongest sync", "github watched correlations headline");
        AssertContainsText(github.WatchedStarCorrelations?.Headline ?? string.Empty, "Strongest star sync", "github watched star correlations headline");
        AssertContainsText(github.WatchedRepoClusters?.Headline ?? string.Empty, "Strongest cluster", "github watched repo clusters headline");
        AssertContainsText(github.WatchedStargazerAudience?.Headline ?? string.Empty, "Strongest shared stargazers", "github watched stargazer audience headline");
    }

    private static void TestUsageTelemetryGitHubWrappedCardPageModelBuilderAddsWatchedMomentumWhenProvided() {
        var section = CreateSampleGitHubOverviewSection();

        var page = UsageTelemetryReportPageModelBuilders.BuildGitHubWrappedCard(
            section,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary());

        AssertEqual(9, page.FooterMetrics.Count, "github wrapped card footer count with watched summary");
        AssertEqual("Watched momentum", page.FooterMetrics[2].Label, "github wrapped card watched footer label");
        AssertContainsText(page.FooterMetrics[2].Value, "+5 stars", "github wrapped card watched footer value");
        AssertEqual("Linked movers", page.FooterMetrics[3].Label, "github wrapped card linked movers footer label");
        AssertContainsText(page.FooterMetrics[3].Value, "IntelligenceX", "github wrapped card linked movers footer value");
        AssertEqual("Star sync", page.FooterMetrics[4].Label, "github wrapped card star sync footer label");
        AssertContainsText(page.FooterMetrics[4].Value, "IntelligenceX", "github wrapped card star sync footer value");
        AssertEqual("Related repos", page.FooterMetrics[5].Label, "github wrapped card related repos footer label");
        AssertContainsText(page.FooterMetrics[5].Value, "IntelligenceX", "github wrapped card related repos footer value");
        AssertEqual("Shared stargazers", page.FooterMetrics[6].Label, "github wrapped card shared stargazers footer label");
        AssertContainsText(page.FooterMetrics[6].Value, "IntelligenceX", "github wrapped card shared stargazers footer value");
        AssertEqual("Shared forkers", page.FooterMetrics[7].Label, "github wrapped card shared forkers footer label");
        AssertContainsText(page.FooterMetrics[7].Value, "IntelligenceX", "github wrapped card shared forkers footer value");
        AssertEqual("Rising forks", page.FooterMetrics[8].Label, "github wrapped card rising forks footer label");
        AssertContainsText(page.FooterMetrics[8].Value, "alice/IntelligenceX", "github wrapped card rising forks footer value");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsCodeChurnWhenProvided() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitCodeChurnSummary: CreateSampleGitCodeChurnSummary());

        AssertNotNull(page.CodeChurn, "overview page model code churn exists");
        AssertEqual("Code churn", page.CodeChurn!.Title, "overview page model code churn title");
        AssertContainsText(page.CodeChurn.Headline, "+1.24K", "overview page model code churn headline");
        AssertEqual("git-code-churn", page.CodeChurn.DailyBreakdown.Key, "overview page model code churn key");
        AssertContainsText(page.CodeChurn.DailyBreakdown.Rows[0].Value, "+120", "overview page model code churn first row");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubLocalAlignmentWhenProvided() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2026, 03, 01),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 9_400m,
            TotalDays = 12,
            ActiveDays = 9,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 2_200m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "GitHub local alignment",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] {
                CreateSampleCorrelationOverviewSection(
                    "codex",
                    "Codex",
                    new double[] { 160, 0, 450, 0, 300, 250, 560 }),
                CreateSampleGitHubOverviewSection()
            },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary(),
            gitCodeChurnSummary: CreateSampleGitCodeChurnSummary());

        var githubSection = page.Sections.First(static section => string.Equals(section.ProviderId, "github", StringComparison.OrdinalIgnoreCase));
        AssertNotNull(githubSection.GitHub, "overview page model github section exists");
        AssertNotNull(githubSection.GitHub!.WatchedLocalAlignment, "overview page model github local alignment exists");
        AssertEqual("github-watched-local-alignment", githubSection.GitHub.WatchedLocalAlignment!.Key, "overview page model github local alignment key");
        AssertContainsText(githubSection.GitHub.WatchedLocalAlignment.Headline ?? string.Empty, "Strongest local", "overview page model github local alignment headline");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubForkNetworkWhenProvided() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary());

        var githubSection = page.Sections.Single().GitHub;
        AssertNotNull(githubSection, "github page model exists with fork network");
        AssertNotNull(githubSection!.WatchedForkNetwork, "github page model fork network exists");
        AssertEqual("github-watched-fork-network", githubSection.WatchedForkNetwork!.Key, "github page model fork network key");
        AssertContainsText(githubSection.WatchedForkNetwork.Headline ?? string.Empty, "shared forkers", "github page model fork network headline");
        AssertNotNull(githubSection.WatchedForkMomentum, "github page model fork momentum exists");
        AssertEqual("github-watched-fork-momentum", githubSection.WatchedForkMomentum!.Key, "github page model fork momentum key");
        AssertContainsText(githubSection.WatchedForkMomentum.Headline ?? string.Empty, "Top fork mover", "github page model fork momentum headline");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubStarCorrelationWhenProvided() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary());

        var githubSection = page.Sections.Single().GitHub;
        AssertNotNull(githubSection, "github page model exists with star correlation");
        AssertNotNull(githubSection!.WatchedStarCorrelations, "github page model star correlation exists");
        AssertEqual("github-watched-star-correlations", githubSection.WatchedStarCorrelations!.Key, "github page model star correlation key");
        AssertContainsText(githubSection.WatchedStarCorrelations.Headline ?? string.Empty, "star sync", "github page model star correlation headline");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubRepoClusterWhenProvided() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary());

        var githubSection = page.Sections.Single().GitHub;
        AssertNotNull(githubSection, "github page model exists with repo cluster");
        AssertNotNull(githubSection!.WatchedRepoClusters, "github page model repo cluster exists");
        AssertEqual("github-watched-repo-clusters", githubSection.WatchedRepoClusters!.Key, "github page model repo cluster key");
        AssertContainsText(githubSection.WatchedRepoClusters.Headline ?? string.Empty, "cluster", "github page model repo cluster headline");
        AssertNotNull(page.GitHubRepoClusters, "overview page model top-level repo cluster exists");
        AssertEqual("Related repo clusters", page.GitHubRepoClusters!.Title, "overview page model top-level repo cluster title");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubStargazerAudienceWhenProvided() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { CreateSampleGitHubOverviewSection() },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary());

        var githubSection = page.Sections.Single().GitHub;
        AssertNotNull(githubSection, "github page model exists with stargazer audience");
        AssertNotNull(githubSection!.WatchedStargazerAudience, "github page model stargazer audience exists");
        AssertEqual("github-watched-stargazer-audience", githubSection.WatchedStargazerAudience!.Key, "github page model stargazer audience key");
        AssertContainsText(githubSection.WatchedStargazerAudience.Headline ?? string.Empty, "shared stargazers", "github page model stargazer audience headline");
        AssertContainsText(githubSection.WatchedStargazerAudience.Note ?? string.Empty, "3/3 watched repos captured", "github page model stargazer audience coverage note");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubStargazerCoverageWithoutOverlap() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { CreateSampleGitHubOverviewSection() },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: new GitHubObservabilitySummaryData(
                dbPath: @"C:\telemetry\usage.db",
                enabledWatchCount: 3,
                snapshotRepositoryCount: 3,
                comparableRepositoryCount: 3,
                totalStars: 1_063,
                totalForks: 153,
                totalWatchers: 54,
                positiveStarDelta: 5,
                positiveForkDelta: 1,
                positiveWatcherDelta: 2,
                changedRepositoryCount: 1,
                latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                repositories: Array.Empty<GitHubObservedRepositoryTrendData>(),
                correlations: Array.Empty<GitHubObservedCorrelationData>(),
                observedStargazerCount: 4,
                latestStargazerCaptureAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                stargazerSnapshotRepositoryCount: 2,
                laggingStargazerRepositoryCount: 1));

        var githubSection = page.Sections.Single().GitHub;
        AssertNotNull(githubSection, "github page model exists with stargazer coverage only");
        AssertNotNull(githubSection!.WatchedStargazerAudience, "github page model stargazer coverage insight exists");
        AssertContainsText(githubSection.WatchedStargazerAudience!.Headline ?? string.Empty, "active", "github page model stargazer coverage headline");
        AssertContainsText(githubSection.WatchedStargazerAudience.Note ?? string.Empty, "2/3 watched repos captured", "github page model stargazer coverage note");
        AssertContainsText(githubSection.WatchedStargazerAudience.Note ?? string.Empty, "1 behind latest repo sync", "github page model stargazer coverage lag note");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsGitHubForkCoverageWithoutOverlap() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 18m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "GitHub Usage",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "contributions",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { CreateSampleGitHubOverviewSection() },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: new GitHubObservabilitySummaryData(
                dbPath: @"C:\telemetry\usage.db",
                enabledWatchCount: 3,
                snapshotRepositoryCount: 3,
                comparableRepositoryCount: 3,
                totalStars: 1_063,
                totalForks: 153,
                totalWatchers: 54,
                positiveStarDelta: 5,
                positiveForkDelta: 1,
                positiveWatcherDelta: 2,
                changedRepositoryCount: 1,
                latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                repositories: Array.Empty<GitHubObservedRepositoryTrendData>(),
                correlations: Array.Empty<GitHubObservedCorrelationData>(),
                latestForkCaptureAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                forkSnapshotRepositoryCount: 2,
                laggingForkRepositoryCount: 1));

        var githubSection = page.Sections.Single().GitHub;
        AssertNotNull(githubSection, "github page model exists with fork coverage only");
        AssertNotNull(githubSection!.WatchedForkNetwork, "github page model fork coverage insight exists");
        AssertContainsText(githubSection.WatchedForkNetwork!.Headline ?? string.Empty, "active", "github page model fork coverage headline");
        AssertContainsText(githubSection.WatchedForkNetwork.Note ?? string.Empty, "2/3 watched repos captured", "github page model fork coverage note");
        AssertContainsText(githubSection.WatchedForkNetwork.Note ?? string.Empty, "1 behind latest repo sync", "github page model fork coverage lag note");
        AssertNotNull(githubSection.WatchedForkMomentum, "github page model fork momentum coverage insight exists");
        AssertContainsText(githubSection.WatchedForkMomentum!.Note ?? string.Empty, "2/3 watched repos captured", "github page model fork momentum coverage note");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsTopLevelGitHubLocalAlignmentWhenProvided() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2026, 03, 01),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 9_400m,
            TotalDays = 12,
            ActiveDays = 9,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 2_200m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "GitHub local alignment",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] {
                CreateSampleCorrelationOverviewSection(
                    "codex",
                    "Codex",
                    new double[] { 160, 0, 450, 0, 300, 250, 560 }),
                CreateSampleGitHubOverviewSection()
            },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary(),
            gitCodeChurnSummary: CreateSampleGitCodeChurnSummary());

        AssertNotNull(page.GitHubLocalAlignment, "overview page model top-level github local alignment exists");
        AssertEqual("Watched repo sync", page.GitHubLocalAlignment!.Title, "overview page model top-level github local alignment title");
        AssertEqual("github-watched-local-alignment", page.GitHubLocalAlignment.Repositories.Key, "overview page model top-level github local alignment key");
        AssertContainsText(page.GitHubLocalAlignment.Headline, "Strongest local sync", "overview page model top-level github local alignment headline");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderExcludesGitHubProviderFromLocalAlignmentInput() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2026, 03, 01),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 25_000m,
            TotalDays = 12,
            ActiveDays = 9,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 8_200m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "GitHub local alignment exclusion",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] {
                CreateSampleCorrelationOverviewSection(
                    "codex",
                    "Codex",
                    new double[] { 160, 0, 450, 0, 300, 250, 560 }),
                CreateSampleCorrelationOverviewSection(
                    "github",
                    "GitHub",
                    new double[] { 6000, 5000, 4200, 3800, 4400, 4100, 6200 })
            },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitHubObservabilitySummary: CreateSampleGitHubObservabilitySummary(),
            gitCodeChurnSummary: CreateSampleGitCodeChurnSummary());

        AssertNotNull(page.GitHubLocalAlignment, "overview page model github local alignment exists when github provider section is present");
        AssertContainsText(page.GitHubLocalAlignment!.Note ?? string.Empty, "1.72K recent usage units", "overview page model github local alignment excludes github provider usage");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderAddsChurnUsageCorrelationWhenProvided() {
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2026, 03, 01),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 9_400m,
            TotalDays = 12,
            ActiveDays = 9,
            PeakDayUtc = new DateTime(2026, 03, 12),
            PeakValue = 2_200m
        };
        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "Correlation test",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] {
                CreateSampleCorrelationOverviewSection(
                    "codex",
                    "Codex",
                    new double[] { 160, 0, 450, 0, 300, 250, 560 }),
                CreateSampleCorrelationOverviewSection(
                    "claude",
                    "Claude",
                    new double[] { 560, 460, 140, 420, 120, 130, 80 })
            },
            metadata: new JsonObject());

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(
            overview,
            gitCodeChurnSummary: CreateSampleGitCodeChurnSummary());

        AssertNotNull(page.ChurnUsageCorrelation, "overview page model churn usage correlation exists");
        AssertEqual("Churn x usage", page.ChurnUsageCorrelation!.Title, "overview page model churn usage title");
        AssertContainsText(page.ChurnUsageCorrelation.Headline, "rose together", "overview page model churn usage headline");
        AssertEqual("git-code-usage-correlation", page.ChurnUsageCorrelation.ProviderSignals.Key, "overview page model churn usage key");
        AssertEqual("Codex", page.ChurnUsageCorrelation.ProviderSignals.Rows[0].Label, "overview page model churn usage leading provider");
        AssertContainsText(page.ChurnUsageCorrelation.ProviderSignals.Rows[0].Value, "+", "overview page model churn usage positive correlation value");
    }

    private static void TestUsageTelemetryBreakdownPageModelBuilderBuildsServerSummary() {
        var heatmap = new HeatmapDocument(
            title: "By source root",
            subtitle: "Detailed breakdown view.",
            palette: HeatmapPalette.ChatGptDark(),
            sections: new[] {
                new HeatmapSection(
                    "2026",
                    null,
                    new[] {
                        new HeatmapDay(new DateTime(2026, 03, 10), 12, level: 3, breakdown: new Dictionary<string, double> {
                            ["Codex · Current (.codex/sessions)"] = 8,
                            ["Codex · Windows.old (.codex/sessions)"] = 4
                        }),
                        new HeatmapDay(new DateTime(2026, 03, 11), 18, level: 4, breakdown: new Dictionary<string, double> {
                            ["Claude · Current (.claude/projects)"] = 18
                        })
                    })
            },
            legendItems: new[] {
                new HeatmapLegendItem("current", "Current", "#6268f1"),
                new HeatmapLegendItem("windows.old", "Windows.old", "#98a8ff")
            });

        var page = UsageTelemetryReportPageModelBuilders.BuildBreakdown(
            "Usage Overview",
            "sourceroot",
            "By source root",
            "Detailed breakdown view.",
            heatmap);

        AssertEqual("source-root", page.FileStem, "breakdown page source-root file stem");
        AssertEqual(true, page.Summary.IsSourceRoot, "breakdown page source-root mode");
        AssertEqual("Source coverage", page.Summary.OverviewTitle, "breakdown page overview title");
        AssertEqual("Top source roots", page.Summary.TopRowsTitle, "breakdown page top title");
        AssertEqual("Source families", page.Summary.SecondaryRowsTitle, "breakdown page secondary title");
        AssertEqual(5, page.Summary.Stats.Count, "breakdown page stats count");
        AssertContainsText(page.Summary.Stats[0].Value, "2026-03-10 to 2026-03-11", "breakdown page range stat");
        AssertContainsText(page.Summary.OverviewNotes[1], "3 distinct source roots", "breakdown page source-root count note");
        AssertContainsText(page.Summary.OverviewNotes[2], "2 source families", "breakdown page source-family count note");
        AssertEqual("Claude · Current (.claude/projects)", page.Summary.TopRows[0].Label, "breakdown page top row label");
        AssertEqual("Current machine", page.Summary.SecondaryRows[0].Label, "breakdown page source family");
        AssertContainsText(page.Summary.SecondaryRows[0].Meta ?? string.Empty, "2 roots", "breakdown page source family root count");
        AssertContainsText(page.Summary.SecondaryRows[0].Meta ?? string.Empty, "Claude, Codex", "breakdown page source family provider coverage");
    }

    private static void TestUsageTelemetryOverviewPageModelBuilderBuildsSupportingBreakdownSummaries() {
        var builder = new UsageTelemetryOverviewBuilder();
        var events = new[] {
            new UsageEventRecord("evt-1", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 9, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "cli",
                Model = "gpt-5-codex",
                InputTokens = 900,
                OutputTokens = 300,
                TotalTokens = 1200,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-2", "claude", "claude.logs", "src-2", new DateTimeOffset(2026, 03, 11, 11, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "lab",
                PersonLabel = "Przemek",
                Surface = "chat",
                Model = "claude-opus",
                InputTokens = 410,
                OutputTokens = 90,
                TotalTokens = 500,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var overview = builder.Build(
            events,
            new UsageTelemetryOverviewOptions {
                Title = "Combined Usage",
                Subtitle = "person: Przemek"
            });

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(overview);
        var sourceroot = page.SupportingBreakdowns.Single(item => item.Key == "sourceroot");
        var provider = page.SupportingBreakdowns.Single(item => item.Key == "provider");

        AssertEqual("telemetry-source", provider.FileStem, "overview supporting telemetry-source file stem");
        AssertEqual("source-root", sourceroot.FileStem, "overview supporting source-root file stem");
        AssertEqual(true, sourceroot.Summary.IsSourceRoot, "overview supporting source-root summary mode");
        AssertContainsText(sourceroot.Subtitle ?? string.Empty, "active days", "overview supporting source-root subtitle");
        AssertContainsText(sourceroot.Subtitle ?? string.Empty, " · ", "overview supporting source-root subtitle separators");
        AssertEqual(false, (sourceroot.Subtitle ?? string.Empty).Contains("by source root |", StringComparison.OrdinalIgnoreCase), "overview supporting source-root subtitle removes raw subtitle prefix");
        AssertEqual(false, (sourceroot.Subtitle ?? string.Empty).Contains("sourceroot", StringComparison.OrdinalIgnoreCase), "overview supporting source-root subtitle is humanized");
        AssertEqual("Source coverage", sourceroot.Summary.OverviewTitle, "overview supporting source-root summary title");
        AssertEqual(true, sourceroot.Summary.TopRows.Count > 0, "overview supporting source-root top rows");
    }

    private static void TestUsageTelemetryReportBundleWriterPublishesSharedAssets() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 11),
            PeakValue = 18m
        };

        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: new[] {
                new UsageTelemetryOverviewHeatmap(
                    "provider",
                    "By telemetry source",
                    new HeatmapDocument(
                        title: "By telemetry source",
                        subtitle: "Detailed breakdown view.",
                        palette: HeatmapPalette.ChatGptDark(),
                        sections: new[] {
                            new HeatmapSection(
                                "2026",
                                null,
                                new[] {
                                    new HeatmapDay(new DateTime(2026, 03, 10), 12, level: 3, breakdown: new Dictionary<string, double> {
                                        ["codex"] = 8,
                                        ["claude"] = 4
                                    }),
                                    new HeatmapDay(new DateTime(2026, 03, 11), 18, level: 4, breakdown: new Dictionary<string, double> {
                                        ["codex"] = 10,
                                        ["claude"] = 8
                                    })
                                })
                        },
                        legendItems: new[] {
                            new HeatmapLegendItem("codex", "Codex", "#6268f1"),
                            new HeatmapLegendItem("claude", "Claude", "#fb8c1d")
                        })),
                new UsageTelemetryOverviewHeatmap(
                    "sourceroot",
                    "By source root",
                    new HeatmapDocument(
                        title: "By source root",
                        subtitle: "Detailed breakdown view.",
                        palette: HeatmapPalette.ChatGptDark(),
                        sections: new[] {
                            new HeatmapSection(
                                "2026",
                                null,
                                new[] {
                                    new HeatmapDay(new DateTime(2026, 03, 10), 12, level: 3, breakdown: new Dictionary<string, double> {
                                        ["Codex · Current (.codex/sessions)"] = 8,
                                        ["Codex · Windows.old (.codex/sessions)"] = 4
                                    }),
                                    new HeatmapDay(new DateTime(2026, 03, 11), 18, level: 4, breakdown: new Dictionary<string, double> {
                                        ["Claude · Current (.claude/projects)"] = 18
                                    })
                                })
                        },
                        legendItems: new[] {
                            new HeatmapLegendItem("current", "Current", "#6268f1"),
                            new HeatmapLegendItem("windows.old", "Windows.old", "#98a8ff")
                        }))
            },
            providerSections: new[] { section });

        var tempPath = Path.Combine(Path.GetTempPath(), "ix-report-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try {
            UsageTelemetryReportBundleWriter.WriteOverviewBundle(overview, tempPath);

            var indexPath = Path.Combine(tempPath, "index.html");
            var shellCssPath = Path.Combine(tempPath, "report-shell.css");
            var cssPath = Path.Combine(tempPath, "report.css");
            var runtimeJsPath = Path.Combine(tempPath, "report-runtime.js");
            var jsPath = Path.Combine(tempPath, "report.js");
            var breakdownCssPath = Path.Combine(tempPath, "breakdown.css");
            var breakdownJsPath = Path.Combine(tempPath, "breakdown.js");
            var wrappedCssPath = Path.Combine(tempPath, "github-wrapped.css");
            var wrappedSharedCssPath = Path.Combine(tempPath, "github-wrapped-shared.css");
            var wrappedJsPath = Path.Combine(tempPath, "github-wrapped.js");
            var wrappedCardCssPath = Path.Combine(tempPath, "github-wrapped-card.css");
            var wrappedPath = Path.Combine(tempPath, "github-wrapped.html");
            var manifestPath = Path.Combine(tempPath, "bundle-manifest.json");
            AssertEqual(true, File.Exists(indexPath), "report bundle index exists");
            AssertEqual(true, File.Exists(shellCssPath), "report bundle shell css exists");
            AssertEqual(true, File.Exists(cssPath), "report bundle css exists");
            AssertEqual(true, File.Exists(runtimeJsPath), "report bundle runtime js exists");
            AssertEqual(true, File.Exists(jsPath), "report bundle js exists");
            AssertEqual(true, File.Exists(breakdownCssPath), "report bundle breakdown css exists");
            AssertEqual(true, File.Exists(breakdownJsPath), "report bundle breakdown js exists");
            AssertEqual(true, File.Exists(wrappedSharedCssPath), "report bundle wrapped shared css exists");
            AssertEqual(true, File.Exists(wrappedCssPath), "report bundle wrapped css exists");
            AssertEqual(true, File.Exists(wrappedJsPath), "report bundle wrapped js exists");
            AssertEqual(true, File.Exists(wrappedCardCssPath), "report bundle wrapped card css exists");
            AssertEqual(true, File.Exists(manifestPath), "report bundle manifest exists");

            var html = File.ReadAllText(indexPath);
            var wrappedHtml = File.ReadAllText(wrappedPath);
            var manifest = File.ReadAllText(manifestPath);
            AssertContainsText(html, "report-shell.css", "report bundle shared shell css reference");
            AssertContainsText(html, "report.css", "report bundle external css reference");
            AssertContainsText(html, "report-runtime.js", "report bundle shared runtime reference");
            AssertContainsText(html, "report.js", "report bundle external js reference");
            AssertContainsText(wrappedHtml, "report-runtime.js", "report bundle wrapped shared runtime reference");
            AssertContainsText(manifest, "\"assets\":[", "report bundle manifest assets");
            AssertContainsText(manifest, "\"pages\":[", "report bundle manifest pages");
            AssertContainsText(manifest, "\"dataFiles\":[", "report bundle manifest data files");
            AssertContainsText(manifest, "\"lightSvgFiles\":[", "report bundle manifest light svg files");
            AssertContainsText(manifest, "\"darkSvgFiles\":[", "report bundle manifest dark svg files");
            AssertContainsText(manifest, "\"report-runtime.js\"", "report bundle manifest runtime asset");
            AssertContainsText(manifest, "\"report-shell.css\"", "report bundle manifest shell asset");
            AssertContainsText(manifest, "\"github-wrapped-shared.css\"", "report bundle manifest shared wrapped css");
            AssertContainsText(manifest, "\"index.html\"", "report bundle manifest main page");
            AssertContainsText(manifest, "\"telemetry-source.html\"", "report bundle manifest telemetry-source page");
            AssertContainsText(manifest, "\"telemetry-source.json\"", "report bundle manifest telemetry-source json");
            AssertContainsText(manifest, "\"telemetry-source.light.svg\"", "report bundle manifest telemetry-source light svg");
            AssertContainsText(manifest, "\"telemetry-source.dark.svg\"", "report bundle manifest telemetry-source dark svg");
            AssertContainsText(manifest, "\"github-wrapped.html\"", "report bundle manifest wrapped page");
            AssertContainsText(manifest, "\"source-root.html\"", "report bundle manifest source-root page");
            AssertContainsText(manifest, "\"source-root.json\"", "report bundle manifest source-root json");
            AssertContainsText(manifest, "\"source-root.light.svg\"", "report bundle manifest source-root light svg");
            AssertContainsText(manifest, "\"source-root.dark.svg\"", "report bundle manifest source-root dark svg");
            AssertContainsText(manifest, "\"provider-github.light.svg\"", "report bundle manifest github light svg");
            AssertContainsText(manifest, "\"provider-github.dark.svg\"", "report bundle manifest github dark svg");
            AssertEqual(false, manifest.Contains("\"provider.html\"", StringComparison.OrdinalIgnoreCase), "report bundle manifest avoids raw provider key page");
            AssertContainsText(html, "source-root.html", "report bundle overview source-root detail link");
            AssertContainsText(html, "telemetry-source.html", "report bundle overview telemetry-source detail link");
            AssertEqual(false, html.Contains("provider.html", StringComparison.OrdinalIgnoreCase), "report bundle overview avoids raw provider key detail link");
            AssertEqual(false, html.Contains("sourceroot.html", StringComparison.OrdinalIgnoreCase), "report bundle overview avoids raw source-root key in file names");
            AssertEqual(false, html.Contains("<style>", StringComparison.Ordinal), "report bundle no inline style block");
            AssertEqual(false, html.Contains("const ixThemeKey", StringComparison.Ordinal), "report bundle no inline overview js");
        } finally {
            if (Directory.Exists(tempPath)) {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    private static UsageTelemetryOverviewProviderSection CreateSampleGitHubOverviewSection() {
        return new UsageTelemetryOverviewProviderSection(
            key: "provider-github",
            providerId: "github",
            title: "GitHub",
            subtitle: "@przemyslawklys · 2025-03-14 to 2026-03-12",
            heatmap: new HeatmapDocument(
                title: "@przemyslawklys on GitHub",
                subtitle: "https://github.com/przemyslawklys",
                palette: HeatmapPalette.ChatGptDark(),
                sections: new[] {
                    new HeatmapSection(
                        "GitHub",
                        null,
                        new[] {
                            new HeatmapDay(new DateTime(2026, 03, 10), 12, level: 3),
                            new HeatmapDay(new DateTime(2026, 03, 11), 18, level: 4)
                        })
                }),
            rangeStartUtc: new DateTime(2025, 03, 14),
            rangeEndUtc: new DateTime(2026, 03, 12),
            latestEventUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            activeDays: 71,
            totalDays: 364,
            accountCount: 1,
            sourceRootCount: 1,
            accountLabels: new[] { "przemyslawklys" },
            metrics: new[] {
                new UsageTelemetryOverviewSectionMetric("contributions", "Total contributions", "11.8K", "71 active days", 1d, "#216e39")
            },
            composition: null,
            spotlightCards: new[] {
                new UsageTelemetryOverviewCard("most-active-month", "Most Active Month", "July 2025", "7.6K contributions"),
                new UsageTelemetryOverviewCard("longest-streak", "Longest Streak", "204 days", "Current: 204 days")
            },
            inputTokens: 0,
            outputTokens: 0,
            totalTokens: 0,
            monthlyUsageTitle: "Monthly contributions",
            monthlyUsageUnitsLabel: "contributions",
            monthlyUsage: new[] {
                new UsageTelemetryOverviewMonthlyUsage(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), 4150, 3),
                new UsageTelemetryOverviewMonthlyUsage(new DateTime(2026, 01, 1, 0, 0, 0, DateTimeKind.Utc), 400_600_000, 24)
            },
            additionalInsights: new[] {
                new UsageTelemetryOverviewInsightSection(
                    "github-year-comparison",
                    "Year over year",
                    "2026 YTD +1989.1% vs 2025",
                    "Compared through 03-12; current year is year-to-date.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("2026 YTD", "11.8K contributions", "71 active days · longest streak 204 days", 1d),
                        new UsageTelemetryOverviewInsightRow("2025 YTD", "567 contributions", "19 active days · longest streak 34 days", 0.05d)
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-scope-split",
                    "Profile vs correlated scope",
                    "8.99K stars across selected scope",
                    "Personal profile activity and correlated owner-repository impact are tracked separately here.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("Personal scope", "6 stars", "przemyslawklys · 4 repositories · 1 fork", 0.001d),
                        new UsageTelemetryOverviewInsightRow("Correlated owner scope", "8.99K stars", "EvotecIT · 114 repositories · 1.23K forks", 0.999d)
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-recent-repositories",
                    "Recent repository activity",
                    "EvotecIT/IntelligenceX",
                    "Public repositories sorted by latest push timestamp across the selected owner scope.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/IntelligenceX", "2026-03-12", "Rising · 120 stars · 20 forks · PowerShell", 1d, "https://github.com/EvotecIT/IntelligenceX"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/OfficeIMO", "2026-02-20", "Established · 80 stars · 10 forks · C#", 0.85d, "https://github.com/EvotecIT/OfficeIMO")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-languages",
                    "Top languages",
                    "PowerShell",
                    "Ranked by stars across the selected owner scope.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("PowerShell", "7.06K stars", "68 repositories · 921 forks", 1d),
                        new UsageTelemetryOverviewInsightRow("C#", "1.64K stars", "14 repositories · 217 forks", 0.23d)
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-repositories",
                    "Top repositories",
                    "EvotecIT/GPOZaurr",
                    "Ranked by stars across the selected owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "533 stars", "72 forks · PowerShell", 1d, "https://github.com/EvotecIT/GPOZaurr"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/PSWriteHTML", "410 stars", "61 forks · PowerShell", 0.76d, "https://github.com/EvotecIT/PSWriteHTML")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-repositories-forks",
                    "Top repositories by forks",
                    "EvotecIT/GPOZaurr",
                    "Ranked by forks across the selected owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "72 forks", "533 stars · PowerShell · pushed 2026-03-12", 1d, "https://github.com/EvotecIT/GPOZaurr"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/PSWriteHTML", "61 forks", "410 stars · PowerShell · pushed 2026-02-28", 0.85d, "https://github.com/EvotecIT/PSWriteHTML")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-repositories-health",
                    "Top repositories by health",
                    "EvotecIT/IntelligenceX",
                    "Ranked by recency plus repository impact across the selected owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/IntelligenceX", "Rising", "120 stars · 20 forks · pushed 2026-03-12", 1d, "https://github.com/EvotecIT/IntelligenceX"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "Established", "533 stars · 72 forks · pushed 2026-03-12", 0.91d, "https://github.com/EvotecIT/GPOZaurr")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-owner-przemyslawklys",
                    "przemyslawklys",
                    "6 stars · 1 fork",
                    "4 public repositories in this owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("przemyslawklys/ExampleRepo", "6 stars", "1 fork · PowerShell · pushed 2026-03-01", 1d, "https://github.com/przemyslawklys/ExampleRepo")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-owner-evotecit",
                    "EvotecIT",
                    "8.99K stars · 1.23K forks",
                    "114 public repositories in this owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "533 stars", "72 forks · PowerShell · pushed 2026-03-12", 1d, "https://github.com/EvotecIT/GPOZaurr"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/PSWriteHTML", "410 stars", "61 forks · PowerShell · pushed 2026-02-28", 0.76d, "https://github.com/EvotecIT/PSWriteHTML")
                    })
            },
            topModels: Array.Empty<UsageTelemetryOverviewTopModel>(),
            apiCostEstimate: null,
            mostUsedModel: null,
            recentModel: null,
            longestStreakDays: 204,
            currentStreakDays: 204,
            note: "Owner scope: EvotecIT, przemyslawklys · 8.99K stars across 118 public repositories · Auto-correlated owners: EvotecIT");
    }

    private static GitHubObservabilitySummaryData CreateSampleGitHubObservabilitySummary() {
        var sampleForkChange = GitHubRepositoryForkHistoryAnalytics.CreateChange(
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice/IntelligenceX", new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "alice/IntelligenceX",
                new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                score: 66d,
                tier: "medium",
                stars: 7,
                forks: 1,
                watchers: 2,
                openIssues: 0),
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice/IntelligenceX", new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "alice/IntelligenceX",
                new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                score: 82d,
                tier: "high",
                stars: 11,
                forks: 1,
                watchers: 4,
                openIssues: 0));
        return new GitHubObservabilitySummaryData(
            dbPath: @"C:\telemetry\usage.db",
            enabledWatchCount: 3,
            snapshotRepositoryCount: 3,
            comparableRepositoryCount: 3,
            totalStars: 1_063,
            totalForks: 153,
            totalWatchers: 54,
            positiveStarDelta: 5,
            positiveForkDelta: 1,
            positiveWatcherDelta: 2,
            changedRepositoryCount: 1,
            latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            repositories: new[] {
                new GitHubObservedRepositoryTrendData(
                    repositoryNameWithOwner: "EvotecIT/IntelligenceX",
                    stars: 120,
                    forks: 20,
                    watchers: 12,
                    openIssues: 3,
                    starDelta: 5,
                    forkDelta: 1,
                    watcherDelta: 2,
                    openIssueDelta: -1,
                    currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                    previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                    trendPoints: new[] {
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 0d, 0, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 3d, 1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 6d, 1, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 0d, 0, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 8d, 1, 1, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 6d, 1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 9d, 1, 0, 1)
                    }),
                new GitHubObservedRepositoryTrendData(
                    repositoryNameWithOwner: "EvotecIT/PSWriteHTML",
                    stars: 410,
                    forks: 61,
                    watchers: 20,
                    openIssues: 4,
                    starDelta: 4,
                    forkDelta: 1,
                    watcherDelta: 1,
                    openIssueDelta: 0,
                    currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                    previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                    trendPoints: new[] {
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 3d, 1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 5d, 1, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 7d, 1, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 3d, 0, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 8d, 1, 1, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 6d, 1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 9d, 1, 0, 1)
                    }),
                new GitHubObservedRepositoryTrendData(
                    repositoryNameWithOwner: "EvotecIT/GPOZaurr",
                    stars: 533,
                    forks: 72,
                    watchers: 22,
                    openIssues: 6,
                    starDelta: -2,
                    forkDelta: 0,
                    watcherDelta: -1,
                    openIssueDelta: 1,
                    currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                    previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                    trendPoints: new[] {
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), -2d, -1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), -4d, -1, 0, -1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), -6d, -1, 0, -1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), -2d, 0, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), -7d, -1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), -5d, -1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), -8d, -1, 0, -1)
                    })
            },
            correlations: new[] {
                new GitHubObservedCorrelationData(
                    repositoryANameWithOwner: "EvotecIT/IntelligenceX",
                    repositoryBNameWithOwner: "EvotecIT/PSWriteHTML",
                    correlation: 0.99d,
                    overlapDays: 7,
                    sharedUpDays: 7,
                    sharedDownDays: 0,
                    opposingDays: 0),
                new GitHubObservedCorrelationData(
                    repositoryANameWithOwner: "EvotecIT/IntelligenceX",
                    repositoryBNameWithOwner: "EvotecIT/GPOZaurr",
                    correlation: -0.98d,
                    overlapDays: 7,
                    sharedUpDays: 0,
                    sharedDownDays: 0,
                    opposingDays: 7)
            },
            starCorrelations: new[] {
                new GitHubObservedStarCorrelationData(
                    repositoryANameWithOwner: "EvotecIT/IntelligenceX",
                    repositoryBNameWithOwner: "EvotecIT/PSWriteHTML",
                    correlation: 0.97d,
                    overlapDays: 7,
                    sharedGainDays: 5,
                    sharedDropDays: 0,
                    opposingDays: 0,
                    repositoryARecentStarChange: 5,
                    repositoryBRecentStarChange: 4),
                new GitHubObservedStarCorrelationData(
                    repositoryANameWithOwner: "EvotecIT/IntelligenceX",
                    repositoryBNameWithOwner: "EvotecIT/GPOZaurr",
                    correlation: -0.96d,
                    overlapDays: 7,
                    sharedGainDays: 0,
                    sharedDropDays: 0,
                    opposingDays: 5,
                    repositoryARecentStarChange: 5,
                    repositoryBRecentStarChange: -2)
            },
            stargazerAudienceOverlaps: new[] {
                new GitHubObservedStargazerAudienceOverlapData(
                    repositoryANameWithOwner: "EvotecIT/IntelligenceX",
                    repositoryBNameWithOwner: "EvotecIT/PSWriteHTML",
                    sharedStargazerCount: 2,
                    repositoryAStargazerCount: 5,
                    repositoryBStargazerCount: 4,
                    overlapRatio: 0.50d,
                    sampleSharedStargazers: new[] { "alice", "bob" })
            },
            forkNetworkOverlaps: new[] {
                new GitHubObservedForkNetworkOverlapData(
                    repositoryANameWithOwner: "EvotecIT/IntelligenceX",
                    repositoryBNameWithOwner: "EvotecIT/PSWriteHTML",
                    sharedForkOwnerCount: 2,
                    repositoryAForkOwnerCount: 4,
                    repositoryBForkOwnerCount: 3,
                    overlapRatio: 0.67d,
                    sampleSharedForkOwners: new[] { "alice", "bob" })
            },
            observedForkOwnerCount: 5,
            forkChanges: new[] { sampleForkChange },
            latestForkCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            forkSnapshotRepositoryCount: 3,
            laggingForkRepositoryCount: 0,
            observedStargazerCount: 6,
            latestStargazerCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            stargazerSnapshotRepositoryCount: 3,
            laggingStargazerRepositoryCount: 0);
    }

    private static GitCodeChurnSummaryData CreateSampleGitCodeChurnSummary() {
        return new GitCodeChurnSummaryData(
            repositoryRootPath: @"C:\Support\GitHub\IntelligenceX",
            repositoryName: "IntelligenceX",
            recentAddedLines: 1_240,
            recentDeletedLines: 480,
            recentFilesModified: 37,
            recentCommitCount: 11,
            recentActiveDayCount: 4,
            previousAddedLines: 820,
            previousDeletedLines: 310,
            previousFilesModified: 24,
            previousCommitCount: 8,
            last30DaysAddedLines: 3_920,
            last30DaysDeletedLines: 1_840,
            last30DaysFilesModified: 114,
            last30DaysCommitCount: 36,
            last30DaysActiveDayCount: 14,
            latestCommitAtUtc: new DateTimeOffset(2026, 03, 12, 14, 45, 0, TimeSpan.Zero),
            trendDays: new[] {
                new GitCodeChurnDayData(new DateTime(2026, 03, 06), 120, 40, 6, 2),
                new GitCodeChurnDayData(new DateTime(2026, 03, 07), 0, 0, 0, 0),
                new GitCodeChurnDayData(new DateTime(2026, 03, 08), 340, 110, 12, 3),
                new GitCodeChurnDayData(new DateTime(2026, 03, 09), 0, 0, 0, 0),
                new GitCodeChurnDayData(new DateTime(2026, 03, 10), 210, 90, 8, 2),
                new GitCodeChurnDayData(new DateTime(2026, 03, 11), 180, 70, 5, 1),
                new GitCodeChurnDayData(new DateTime(2026, 03, 12), 390, 170, 14, 3)
            });
    }

    private static UsageTelemetryOverviewProviderSection CreateSampleCorrelationOverviewSection(
        string providerId,
        string title,
        IReadOnlyList<double> recentDailyValues) {
        return new UsageTelemetryOverviewProviderSection(
            key: "provider-" + providerId,
            providerId: providerId,
            title: title,
            subtitle: title + " recent activity",
            heatmap: new HeatmapDocument(
                title: title + " usage",
                subtitle: "Correlation sample",
                palette: HeatmapPalette.ChatGptDark(),
                sections: new[] {
                    new HeatmapSection(
                        "Recent",
                        null,
                        new[] {
                            new HeatmapDay(new DateTime(2026, 02, 27), 80, level: 2),
                            new HeatmapDay(new DateTime(2026, 02, 28), 70, level: 2),
                            new HeatmapDay(new DateTime(2026, 03, 01), 60, level: 1),
                            new HeatmapDay(new DateTime(2026, 03, 02), 75, level: 2),
                            new HeatmapDay(new DateTime(2026, 03, 03), 65, level: 2),
                            new HeatmapDay(new DateTime(2026, 03, 04), 55, level: 1),
                            new HeatmapDay(new DateTime(2026, 03, 05), 50, level: 1),
                            new HeatmapDay(new DateTime(2026, 03, 06), recentDailyValues[0], level: 2),
                            new HeatmapDay(new DateTime(2026, 03, 07), recentDailyValues[1], level: 1),
                            new HeatmapDay(new DateTime(2026, 03, 08), recentDailyValues[2], level: 4),
                            new HeatmapDay(new DateTime(2026, 03, 09), recentDailyValues[3], level: 2),
                            new HeatmapDay(new DateTime(2026, 03, 10), recentDailyValues[4], level: 3),
                            new HeatmapDay(new DateTime(2026, 03, 11), recentDailyValues[5], level: 3),
                            new HeatmapDay(new DateTime(2026, 03, 12), recentDailyValues[6], level: 4)
                        })
                }),
            rangeStartUtc: new DateTime(2026, 02, 27),
            rangeEndUtc: new DateTime(2026, 03, 12),
            latestEventUtc: new DateTimeOffset(2026, 03, 12, 12, 0, 0, TimeSpan.Zero),
            activeDays: 12,
            totalDays: 14,
            accountCount: 1,
            sourceRootCount: 1,
            accountLabels: new[] { providerId + "@example.com" },
            metrics: new[] {
                new UsageTelemetryOverviewSectionMetric("tokens", "Recent usage", "2.1K", "sample", 1d, "#4da3ff")
            },
            composition: null,
            spotlightCards: Array.Empty<UsageTelemetryOverviewCard>(),
            inputTokens: 0,
            outputTokens: 0,
            totalTokens: 0,
            monthlyUsageTitle: "Monthly usage",
            monthlyUsageUnitsLabel: "tokens",
            monthlyUsage: Array.Empty<UsageTelemetryOverviewMonthlyUsage>(),
            additionalInsights: Array.Empty<UsageTelemetryOverviewInsightSection>(),
            topModels: Array.Empty<UsageTelemetryOverviewTopModel>(),
            apiCostEstimate: null,
            mostUsedModel: null,
            recentModel: null,
            longestStreakDays: 0,
            currentStreakDays: 0,
            note: null);
    }
}
