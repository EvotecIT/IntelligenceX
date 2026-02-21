using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Theming;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    internal static TimeSpan? ResolveStartupWebViewBudget(bool captureStartupPhaseTelemetry) {
        return ResolveStartupWebViewBudget(
            captureStartupPhaseTelemetry,
            lastEnsureWebViewMs: null,
            consecutiveBudgetExhaustions: 0,
            consecutiveStableCompletions: 0,
            adaptiveCooldownRunsRemaining: 0,
            lastAppliedBudgetMs: null);
    }

    internal static TimeSpan? ResolveStartupWebViewBudget(
        bool captureStartupPhaseTelemetry,
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs) {
        var decision = ResolveStartupWebViewBudgetDecisionIfEnabled(
            captureStartupPhaseTelemetry,
            lastEnsureWebViewMs,
            consecutiveBudgetExhaustions,
            consecutiveStableCompletions,
            adaptiveCooldownRunsRemaining,
            lastAppliedBudgetMs);
        if (decision is null) {
            return null;
        }

        return TimeSpan.FromMilliseconds(decision.BudgetMs);
    }

    internal static string? ResolveStartupWebViewBudgetReason(
        bool captureStartupPhaseTelemetry,
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs) {
        var decision = ResolveStartupWebViewBudgetDecisionIfEnabled(
            captureStartupPhaseTelemetry,
            lastEnsureWebViewMs,
            consecutiveBudgetExhaustions,
            consecutiveStableCompletions,
            adaptiveCooldownRunsRemaining,
            lastAppliedBudgetMs);
        return decision?.Reason;
    }

    internal static int ResolveStartupWebViewBudgetMilliseconds(
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs) {
        return ResolveStartupWebViewBudgetDecision(
            lastEnsureWebViewMs,
            consecutiveBudgetExhaustions,
            consecutiveStableCompletions,
            adaptiveCooldownRunsRemaining,
            lastAppliedBudgetMs).BudgetMs;
    }

    private static StartupWebViewBudgetDecision? ResolveStartupWebViewBudgetDecisionIfEnabled(
        bool captureStartupPhaseTelemetry,
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs) {
        if (!captureStartupPhaseTelemetry) {
            return null;
        }

        return ResolveStartupWebViewBudgetDecision(
            lastEnsureWebViewMs,
            consecutiveBudgetExhaustions,
            consecutiveStableCompletions,
            adaptiveCooldownRunsRemaining,
            lastAppliedBudgetMs);
    }

    private static string ResolveStartupWebViewBudgetTierReason(int adaptiveBudgetMs) {
        if (adaptiveBudgetMs == StartupWebViewBudgetFastMs) {
            return StartupWebViewBudgetReasonFastTier;
        }

        if (adaptiveBudgetMs == StartupWebViewBudgetMediumMs) {
            return StartupWebViewBudgetReasonMediumTier;
        }

        if (adaptiveBudgetMs == StartupWebViewBudgetSlowMs) {
            return StartupWebViewBudgetReasonSlowTier;
        }

        return StartupWebViewBudgetReasonConservativeTier;
    }

    private static string ComposeStartupWebViewBudgetReason(string tierReason, string reasonSuffix) {
        return string.Concat(tierReason, reasonSuffix);
    }

    private static StartupWebViewBudgetDecision ResolveStartupWebViewBudgetDecision(
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs) {
        var conservativeBudgetMs = (int)Math.Round(StartupWebViewBudget.TotalMilliseconds);
        var normalizedExhaustions = Math.Max(0, consecutiveBudgetExhaustions);
        var normalizedStableCompletions = Math.Max(0, consecutiveStableCompletions);
        var normalizedCooldownRuns = Math.Max(0, adaptiveCooldownRunsRemaining);

        if (normalizedCooldownRuns > 0) {
            return new StartupWebViewBudgetDecision(conservativeBudgetMs, StartupWebViewBudgetReasonCooldownConservative);
        }

        if (normalizedExhaustions > 0 && normalizedStableCompletions < StartupWebViewBudgetAdaptiveMinStableCompletions) {
            return new StartupWebViewBudgetDecision(conservativeBudgetMs, StartupWebViewBudgetReasonExhaustionConservative);
        }

        if (normalizedStableCompletions < StartupWebViewBudgetAdaptiveMinStableCompletions) {
            return new StartupWebViewBudgetDecision(conservativeBudgetMs, StartupWebViewBudgetReasonInsufficientStability);
        }

        if (!lastEnsureWebViewMs.HasValue || lastEnsureWebViewMs.Value <= 0) {
            return new StartupWebViewBudgetDecision(conservativeBudgetMs, StartupWebViewBudgetReasonMissingLastEnsure);
        }

        var measuredEnsureMs = lastEnsureWebViewMs.Value;
        var adaptiveBudgetMs = measuredEnsureMs switch {
            <= StartupWebViewBudgetFastEnsureThresholdMs => StartupWebViewBudgetFastMs,
            <= StartupWebViewBudgetMediumEnsureThresholdMs => StartupWebViewBudgetMediumMs,
            <= StartupWebViewBudgetSlowEnsureThresholdMs => StartupWebViewBudgetSlowMs,
            _ => conservativeBudgetMs
        };
        var tierReason = ResolveStartupWebViewBudgetTierReason(adaptiveBudgetMs);

        if (adaptiveBudgetMs >= conservativeBudgetMs) {
            return new StartupWebViewBudgetDecision(conservativeBudgetMs, StartupWebViewBudgetReasonConservativeTier);
        }

        var marginAwareBudgetMs = Math.Max(
            adaptiveBudgetMs,
            measuredEnsureMs + StartupWebViewBudgetAdaptiveHeadroomMs);
        var clampedBudgetMs = Math.Clamp(marginAwareBudgetMs, StartupWebViewBudgetMinimumMs, conservativeBudgetMs);
        if (!lastAppliedBudgetMs.HasValue || lastAppliedBudgetMs.Value <= 0) {
            return new StartupWebViewBudgetDecision(clampedBudgetMs, ComposeStartupWebViewBudgetReason(tierReason, StartupWebViewBudgetReasonSuffixNew));
        }

        var previousBudgetMs = Math.Clamp(lastAppliedBudgetMs.Value, StartupWebViewBudgetMinimumMs, conservativeBudgetMs);
        if (clampedBudgetMs >= previousBudgetMs) {
            return new StartupWebViewBudgetDecision(clampedBudgetMs, ComposeStartupWebViewBudgetReason(tierReason, StartupWebViewBudgetReasonSuffixNondecreasing));
        }

        var downshiftFloorMs = Math.Max(
            StartupWebViewBudgetMinimumMs,
            previousBudgetMs - StartupWebViewBudgetAdaptiveMaxDownshiftPerRunMs);
        if (clampedBudgetMs < downshiftFloorMs) {
            return new StartupWebViewBudgetDecision(downshiftFloorMs, ComposeStartupWebViewBudgetReason(tierReason, StartupWebViewBudgetReasonSuffixDownshiftCapped));
        }

        return new StartupWebViewBudgetDecision(clampedBudgetMs, ComposeStartupWebViewBudgetReason(tierReason, StartupWebViewBudgetReasonSuffixDownshiftFull));
    }

    private StartupWebViewBudgetCacheEntry SnapshotStartupWebViewBudgetCache() {
        lock (_startupWebViewBudgetCacheSync) {
            return _startupWebViewBudgetCache;
        }
    }

    private void RecordStartupWebViewBudgetSelection(TimeSpan? startupWebViewBudget) {
        if (!startupWebViewBudget.HasValue) {
            return;
        }

        var budgetMs = (int)Math.Round(Math.Max(0, startupWebViewBudget.Value.TotalMilliseconds));
        if (budgetMs <= 0) {
            return;
        }

        lock (_startupWebViewBudgetCacheSync) {
            _startupWebViewBudgetCache = _startupWebViewBudgetCache with {
                LastAppliedBudgetMs = budgetMs,
                UpdatedUtc = DateTime.UtcNow
            };
            SaveStartupWebViewBudgetCache(_startupWebViewBudgetCache);
        }
    }

    private void MarkStartupWebViewBudgetExhausted() {
        Interlocked.Exchange(ref _startupWebViewBudgetExceededThisRun, 1);

        lock (_startupWebViewBudgetCacheSync) {
            var nextExhaustions = Math.Min(
                StartupWebViewBudgetMaxConsecutiveExhaustions,
                Math.Max(0, _startupWebViewBudgetCache.ConsecutiveBudgetExhaustions) + 1);
            _startupWebViewBudgetCache = _startupWebViewBudgetCache with {
                ConsecutiveBudgetExhaustions = nextExhaustions,
                ConsecutiveStableCompletions = 0,
                AdaptiveCooldownRunsRemaining = Math.Max(
                    StartupWebViewBudgetAdaptiveCooldownRunsAfterExhaustion,
                    _startupWebViewBudgetCache.AdaptiveCooldownRunsRemaining),
                UpdatedUtc = DateTime.UtcNow
            };

            SaveStartupWebViewBudgetCache(_startupWebViewBudgetCache);
        }
    }

    private void RecordStartupWebViewEnsureCompletion(TimeSpan ensureDuration, bool budgetExceeded) {
        var ensureMs = (int)Math.Round(Math.Max(0, ensureDuration.TotalMilliseconds));
        StartupLog.Write("StartupPhase.WebView ensure_ms=" + ensureMs.ToString(CultureInfo.InvariantCulture));

        lock (_startupWebViewBudgetCacheSync) {
            var exhaustionCount = budgetExceeded
                ? Math.Min(
                    StartupWebViewBudgetMaxConsecutiveExhaustions,
                    Math.Max(1, _startupWebViewBudgetCache.ConsecutiveBudgetExhaustions))
                : 0;
            var stableCompletions = budgetExceeded
                ? 0
                : Math.Min(
                    StartupWebViewBudgetAdaptiveMaxStableCompletions,
                    Math.Max(0, _startupWebViewBudgetCache.ConsecutiveStableCompletions) + 1);
            var cooldownRunsRemaining = budgetExceeded
                ? Math.Max(
                    StartupWebViewBudgetAdaptiveCooldownRunsAfterExhaustion,
                    _startupWebViewBudgetCache.AdaptiveCooldownRunsRemaining)
                : Math.Max(0, _startupWebViewBudgetCache.AdaptiveCooldownRunsRemaining - 1);
            _startupWebViewBudgetCache = _startupWebViewBudgetCache with {
                LastEnsureWebViewMs = ensureMs,
                ConsecutiveBudgetExhaustions = exhaustionCount,
                ConsecutiveStableCompletions = stableCompletions,
                AdaptiveCooldownRunsRemaining = cooldownRunsRemaining,
                UpdatedUtc = DateTime.UtcNow
            };

            SaveStartupWebViewBudgetCache(_startupWebViewBudgetCache);
        }
    }

    private static StartupWebViewBudgetCacheEntry LoadStartupWebViewBudgetCache() {
        var path = ResolveStartupWebViewBudgetCachePath();
        if (!File.Exists(path)) {
            return StartupWebViewBudgetCacheEntry.Default;
        }

        try {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<StartupWebViewBudgetCachePayload>(json);
            if (payload is null) {
                return StartupWebViewBudgetCacheEntry.Default;
            }

            DateTime? updatedUtc = null;
            if (!string.IsNullOrWhiteSpace(payload.UpdatedUtc)
                && DateTime.TryParse(
                    payload.UpdatedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedUpdatedUtc)) {
                updatedUtc = parsedUpdatedUtc.ToUniversalTime();
            }

            var lastEnsureMs = payload.LastEnsureWebViewMs;
            if (lastEnsureMs.HasValue && lastEnsureMs.Value <= 0) {
                lastEnsureMs = null;
            }

            var exhaustionCount = Math.Max(0, payload.ConsecutiveBudgetExhaustions);
            exhaustionCount = Math.Min(StartupWebViewBudgetMaxConsecutiveExhaustions, exhaustionCount);
            var stableCompletions = Math.Max(0, payload.ConsecutiveStableCompletions);
            stableCompletions = Math.Min(StartupWebViewBudgetAdaptiveMaxStableCompletions, stableCompletions);
            var cooldownRunsRemaining = Math.Max(0, payload.AdaptiveCooldownRunsRemaining);
            cooldownRunsRemaining = Math.Min(StartupWebViewBudgetAdaptiveMaxStableCompletions, cooldownRunsRemaining);
            var lastAppliedBudgetMs = payload.LastAppliedBudgetMs;
            if (lastAppliedBudgetMs.HasValue && lastAppliedBudgetMs.Value <= 0) {
                lastAppliedBudgetMs = null;
            }
            if (lastAppliedBudgetMs.HasValue) {
                var conservativeBudgetMs = (int)Math.Round(StartupWebViewBudget.TotalMilliseconds);
                lastAppliedBudgetMs = Math.Clamp(lastAppliedBudgetMs.Value, StartupWebViewBudgetMinimumMs, conservativeBudgetMs);
            }

            return new StartupWebViewBudgetCacheEntry(
                lastEnsureMs,
                exhaustionCount,
                stableCompletions,
                cooldownRunsRemaining,
                lastAppliedBudgetMs,
                updatedUtc);
        } catch {
            return StartupWebViewBudgetCacheEntry.Default;
        }
    }

    private static void SaveStartupWebViewBudgetCache(StartupWebViewBudgetCacheEntry cache) {
        try {
            var path = ResolveStartupWebViewBudgetCachePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            var payload = new StartupWebViewBudgetCachePayload {
                LastEnsureWebViewMs = cache.LastEnsureWebViewMs,
                ConsecutiveBudgetExhaustions = Math.Max(0, cache.ConsecutiveBudgetExhaustions),
                ConsecutiveStableCompletions = Math.Max(0, cache.ConsecutiveStableCompletions),
                AdaptiveCooldownRunsRemaining = Math.Max(0, cache.AdaptiveCooldownRunsRemaining),
                LastAppliedBudgetMs = cache.LastAppliedBudgetMs,
                UpdatedUtc = cache.UpdatedUtc?.ToString("O")
            };

            var json = JsonSerializer.Serialize(payload);
            File.WriteAllText(path, json);
        } catch {
            // Startup budget cache is best-effort only.
        }
    }

    private static string ResolveStartupWebViewBudgetCachePath() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) {
            return Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", StartupWebViewBudgetCacheFileName);
        }

        return Path.Combine(localAppData, "IntelligenceX.Chat", StartupWebViewBudgetCacheFileName);
    }

}
