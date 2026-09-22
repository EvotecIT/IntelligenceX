using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestProviderLimitWindowsUseReportedDurations() {
        foreach (var slot in new[] { "primary_window", "secondary_window" }) {
            var status = ChatGptRateLimitStatus.FromJson(new JsonObject().Add(slot,
                new JsonObject().Add("used_percent", 43L).Add("limit_window_seconds", 604800L)));
            var windows = new List<ProviderLimitWindow>();
            ProviderLimitSnapshotService.AddOpenAiStatusWindows(windows, "global", "", status);
            AssertEqual(1, windows.Count, "weekly-only response has one window regardless of slot");
            AssertEqual("weekly", windows[0].Label, "weekly duration determines label");
            AssertEqual(TimeSpan.FromDays(7), windows[0].WindowDuration!.Value, "reported weekly duration retained");
        }

        foreach (var duration in new long[] { 14400, 18000, 21600, 1209600, 5400, 45 }) {
            var status = ChatGptRateLimitStatus.FromJson(new JsonObject().Add("primary_window",
                new JsonObject().Add("limit_window_seconds", duration)));
            var windows = new List<ProviderLimitWindow>();
            ProviderLimitSnapshotService.AddOpenAiStatusWindows(windows, "global", "", status);
            var expected = duration switch {
                14400 => "4-hour", 18000 => "5-hour", 21600 => "6-hour",
                1209600 => "14-day", 5400 => "90-minute", _ => "45-second"
            };
            AssertEqual(expected, windows[0].Label, "duration is not rounded to a presumed quota");
        }

        var unknown = ChatGptRateLimitStatus.FromJson(new JsonObject()
            .Add("primary_window", new JsonObject().Add("used_percent", 10L))
            .Add("secondary_window", new JsonObject().Add("used_percent", 20L)));
        var unknownWindows = new List<ProviderLimitWindow>();
        ProviderLimitSnapshotService.AddOpenAiStatusWindows(unknownWindows, "global", "", unknown);
        AssertEqual(2, unknownWindows.Count, "both reported windows retained");
        AssertEqual("Primary window", unknownWindows[0].Label, "unknown primary duration stays neutral");
        AssertEqual("Secondary window", unknownWindows[1].Label, "unknown secondary duration stays neutral");
        AssertEqual(false, unknownWindows[0].WindowDuration.HasValue, "missing duration not invented");
        var absent = new List<ProviderLimitWindow>();
        ProviderLimitSnapshotService.AddOpenAiStatusWindows(absent, "global", "", null);
        AssertEqual(0, absent.Count, "missing status does not manufacture quotas");
    }

    private static void TestProviderLimitSnapshotServiceBatchKeepsHealthyProvidersWhenOneFails() {
        var service = new ProviderLimitSnapshotService((providerId, cancellationToken) => providerId switch {
            "codex" => Task.FromResult(
                new ProviderLimitSnapshot(
                    "codex",
                    "Codex",
                    "test",
                    null,
                    null,
                    Array.Empty<ProviderLimitWindow>(),
                    "healthy",
                    null,
                    DateTimeOffset.UtcNow)),
            "claude" => Task.FromException<ProviderLimitSnapshot>(new InvalidOperationException("boom")),
            _ => Task.FromException<ProviderLimitSnapshot>(new ArgumentOutOfRangeException(nameof(providerId)))
        });

        var snapshots = service.FetchAsync(new[] { "codex", "claude" }).GetAwaiter().GetResult();

        AssertEqual(2, snapshots.Count, "limits batch snapshot count");
        AssertEqual("healthy", snapshots["codex"].Summary ?? string.Empty, "limits batch keeps healthy provider snapshot");
        AssertContainsText(snapshots["claude"].DetailMessage ?? string.Empty, "boom", "limits batch captures failed provider detail");
    }

    private static void TestProviderLimitSnapshotServiceBatchPropagatesCallerCancellation() {
        var canceled = new CancellationToken(canceled: true);
        var service = new ProviderLimitSnapshotService((providerId, cancellationToken) => Task.FromCanceled<ProviderLimitSnapshot>(cancellationToken));

        AssertThrows<OperationCanceledException>(
            () => service.FetchAsync(new[] { "codex" }, canceled).GetAwaiter().GetResult(),
            "limits batch propagates caller cancellation");
    }
}
