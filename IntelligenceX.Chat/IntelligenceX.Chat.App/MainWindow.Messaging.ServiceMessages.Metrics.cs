using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static string FormatMetricsTrace(ChatMetricsMessage metrics) {
        return "metrics: duration="
               + metrics.DurationMs.ToString(CultureInfo.InvariantCulture)
               + "ms"
               + (metrics.TtftMs is null ? string.Empty : " ttft=" + metrics.TtftMs.Value.ToString(CultureInfo.InvariantCulture) + "ms")
               + (metrics.Usage?.TotalTokens is null ? string.Empty : " tokens=" + metrics.Usage.TotalTokens.Value.ToString(CultureInfo.InvariantCulture))
               + " tools=" + metrics.ToolCallsCount.ToString(CultureInfo.InvariantCulture)
               + " rounds=" + metrics.ToolRounds.ToString(CultureInfo.InvariantCulture)
               + FormatAutonomyCounterTraceSegment(metrics.AutonomyCounters)
               + (string.IsNullOrWhiteSpace(metrics.RequestedModel) ? string.Empty : " requestedModel=" + metrics.RequestedModel.Trim())
               + (string.IsNullOrWhiteSpace(metrics.Model) ? string.Empty : " model=" + metrics.Model.Trim())
               + (string.IsNullOrWhiteSpace(metrics.Transport) ? string.Empty : " transport=" + metrics.Transport.Trim())
               + (string.IsNullOrWhiteSpace(metrics.EndpointHost) ? string.Empty : " endpoint=" + metrics.EndpointHost.Trim())
               + " outcome=" + (metrics.Outcome ?? "unknown");
    }

    private static IReadOnlyList<TurnCounterMetricDto> NormalizeTurnCounterMetrics(IReadOnlyList<TurnCounterMetricDto>? counters) {
        if (counters is null || counters.Count == 0) {
            return Array.Empty<TurnCounterMetricDto>();
        }

        var normalized = new List<TurnCounterMetricDto>(counters.Count);
        for (var i = 0; i < counters.Count; i++) {
            var current = counters[i];
            var name = (current.Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            var count = Math.Max(0, current.Count);
            if (count <= 0) {
                continue;
            }

            normalized.Add(new TurnCounterMetricDto {
                Name = name,
                Count = count
            });
        }

        return normalized.Count == 0 ? Array.Empty<TurnCounterMetricDto>() : normalized;
    }

    private static string FormatAutonomyCounterTraceSegment(IReadOnlyList<TurnCounterMetricDto>? counters) {
        if (counters is null || counters.Count == 0) {
            return string.Empty;
        }

        var parts = new List<string>(counters.Count);
        for (var i = 0; i < counters.Count; i++) {
            var current = counters[i];
            var name = (current.Name ?? string.Empty).Trim();
            if (name.Length == 0 || current.Count <= 0) {
                continue;
            }

            parts.Add(name + "=" + current.Count.ToString(CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? string.Empty : " autonomy={" + string.Join(", ", parts) + "}";
    }
}

