using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportDiagnosticsBuilder {
    public static UsageTelemetryReportDiagnosticsModel? Build(
        UsageSummarySnapshot summary,
        JsonObject? metadata,
        int providerSectionsCount) {
        if (summary is null) {
            throw new ArgumentNullException(nameof(summary));
        }

        var items = new List<UsageTelemetryReportDiagnosticsItemModel>();

        var reportHealth = metadata?.GetObject("reportHealth");
        var scanContext = metadata?.GetObject("scanContext");

        var sourceValue = BuildSourceValue(reportHealth, scanContext, providerSectionsCount);
        var sourceCopy = BuildSourceCopy(reportHealth, scanContext);
        if (!string.IsNullOrWhiteSpace(sourceValue)) {
            items.Add(new UsageTelemetryReportDiagnosticsItemModel("Source", sourceValue!, sourceCopy));
        }

        items.Add(new UsageTelemetryReportDiagnosticsItemModel(
            "Coverage",
            summary.ActiveDays.ToString(CultureInfo.InvariantCulture) + "/" + summary.TotalDays.ToString(CultureInfo.InvariantCulture) + " active days",
            BuildCoverageCopy(summary)));

        var latestValue = BuildLatestValue(reportHealth, summary);
        var latestCopy = BuildLatestCopy(summary);
        if (!string.IsNullOrWhiteSpace(latestValue)) {
            items.Add(new UsageTelemetryReportDiagnosticsItemModel("Latest data", latestValue!, latestCopy));
        }

        var scopeValue = BuildScopeValue(reportHealth, scanContext, summary, providerSectionsCount);
        var scopeCopy = BuildScopeCopy(reportHealth, scanContext, summary);
        if (!string.IsNullOrWhiteSpace(scopeValue)) {
            items.Add(new UsageTelemetryReportDiagnosticsItemModel("Coverage scope", scopeValue!, scopeCopy));
        }

        if (items.Count == 0) {
            return null;
        }

        return new UsageTelemetryReportDiagnosticsModel(
            "Data health",
            items,
            BuildDiagnosticsNote(reportHealth, scanContext));
    }

    public static UsageTelemetryReportDiagnosticsModel? BuildForProviderSection(
        UsageTelemetryOverviewProviderSection section) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var items = new List<UsageTelemetryReportDiagnosticsItemModel> {
            new(
                "Telemetry mode",
                ResolveSectionTelemetryMode(section),
                BuildSectionTelemetryModeCopy(section)),
            new(
                "Window",
                BuildSectionCoverageValue(section),
                BuildSectionCoverageCopy(section))
        };

        var latestValue = BuildSectionLatestValue(section);
        if (!string.IsNullOrWhiteSpace(latestValue)) {
            items.Add(new UsageTelemetryReportDiagnosticsItemModel(
                "Latest provider day",
                latestValue!,
                BuildSectionLatestCopy(section)));
        }

        var scopeValue = BuildSectionScopeValue(section);
        if (!string.IsNullOrWhiteSpace(scopeValue)) {
            items.Add(new UsageTelemetryReportDiagnosticsItemModel(
                "Coverage scope",
                scopeValue!,
                BuildSectionScopeCopy(section)));
        }

        return new UsageTelemetryReportDiagnosticsModel(
            "Provider health",
            items,
            NormalizeOptional(section.Note));
    }

    private static string? BuildSourceValue(JsonObject? reportHealth, JsonObject? scanContext, int providerSectionsCount) {
        var summary = NormalizeOptional(reportHealth?.GetString("summary"));
        if (!string.IsNullOrWhiteSpace(summary)) {
            return summary;
        }

        var mode = NormalizeOptional(scanContext?.GetString("mode"));
        if (string.Equals(mode, "quick-report", StringComparison.OrdinalIgnoreCase)) {
            return "Quick scan snapshot";
        }

        return providerSectionsCount > 0
            ? "Telemetry overview"
            : null;
    }

    private static string? BuildSourceCopy(JsonObject? reportHealth, JsonObject? scanContext) {
        var parts = new List<string>();

        var detail = NormalizeOptional(reportHealth?.GetString("detail"));
        if (!string.IsNullOrWhiteSpace(detail)) {
            parts.Add(detail!);
        }

        var generatedAt = NormalizeOptional(reportHealth?.GetString("generatedAtLocal"));
        if (!string.IsNullOrWhiteSpace(generatedAt)) {
            parts.Add("generated " + generatedAt);
        }

        if (scanContext is not null) {
            var scanParts = new List<string>();
            var parsed = scanContext.GetInt64("artifactsParsed");
            if (parsed.GetValueOrDefault() > 0) {
                scanParts.Add(parsed!.Value.ToString(CultureInfo.InvariantCulture) + " parsed");
            }
            var reused = scanContext.GetInt64("artifactsReused");
            if (reused.GetValueOrDefault() > 0) {
                scanParts.Add(reused!.Value.ToString(CultureInfo.InvariantCulture) + " cached");
            }
            var roots = scanContext.GetInt64("rootsConsidered");
            if (roots.GetValueOrDefault() > 0) {
                scanParts.Add(roots!.Value.ToString(CultureInfo.InvariantCulture) + " roots");
            }
            if (scanContext.GetBoolean("artifactBudgetReached")) {
                scanParts.Add("partial");
            }
            if (scanParts.Count > 0) {
                parts.Add(string.Join(" • ", scanParts));
            }
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    private static string BuildCoverageCopy(UsageSummarySnapshot summary) {
        var parts = new List<string>();
        if (summary.StartDayUtc.HasValue && summary.EndDayUtc.HasValue) {
            parts.Add(summary.StartDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                      + " to "
                      + summary.EndDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        if (summary.PeakDayUtc.HasValue && summary.PeakValue > 0m) {
            parts.Add("peak " + summary.PeakDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? "Telemetry-backed range summary" : string.Join(" • ", parts);
    }

    private static string? BuildLatestValue(JsonObject? reportHealth, UsageSummarySnapshot summary) {
        var latestEventUtc = TryParseDateTimeOffset(reportHealth?.GetString("latestEventUtc"));
        if (latestEventUtc.HasValue) {
            return latestEventUtc.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
        }

        if (summary.EndDayUtc.HasValue) {
            return summary.EndDayUtc.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        }

        return null;
    }

    private static string? BuildLatestCopy(UsageSummarySnapshot summary) {
        if (summary.PeakDayUtc.HasValue && summary.PeakValue > 0m) {
            return "Peak " + summary.PeakDayUtc.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
                   + " • " + UsageTelemetryOverviewHtmlFragments.FormatCompact((long)Math.Round(summary.PeakValue, MidpointRounding.AwayFromZero));
        }

        return null;
    }

    private static string? BuildScopeValue(
        JsonObject? reportHealth,
        JsonObject? scanContext,
        UsageSummarySnapshot summary,
        int providerSectionsCount) {
        var accountsText = NormalizeOptional(reportHealth?.GetString("accountsText"));
        if (!string.IsNullOrWhiteSpace(accountsText)) {
            return accountsText;
        }

        var accountCount = summary.AccountBreakdown.Count(static entry => entry.Value > 0m);
        var parts = new List<string>();
        if (providerSectionsCount > 0) {
            parts.Add(providerSectionsCount.ToString(CultureInfo.InvariantCulture) + " providers");
        }
        if (accountCount > 0) {
            parts.Add(accountCount.ToString(CultureInfo.InvariantCulture) + " accounts");
        }

        var roots = scanContext?.GetArray("roots");
        if (roots is not null && roots.Count > 0) {
            parts.Add(roots.Count.ToString(CultureInfo.InvariantCulture) + " roots");
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    private static string? BuildScopeCopy(JsonObject? reportHealth, JsonObject? scanContext, UsageSummarySnapshot summary) {
        var parts = new List<string>();

        var duplicateCount = reportHealth?.GetInt64("duplicateRecordsCollapsed") ?? scanContext?.GetInt64("duplicateRecordsCollapsed");
        if (duplicateCount.GetValueOrDefault() > 0) {
            parts.Add(duplicateCount!.Value.ToString(CultureInfo.InvariantCulture) + " deduped");
        }

        var rawEvents = scanContext?.GetInt64("rawEventsCollected");
        if (rawEvents.GetValueOrDefault() > 0) {
            parts.Add(rawEvents!.Value.ToString(CultureInfo.InvariantCulture) + " raw events");
        } else if (summary.ActiveDays > 0) {
            parts.Add(summary.ActiveDays.ToString(CultureInfo.InvariantCulture) + " active days");
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    private static string? BuildDiagnosticsNote(JsonObject? reportHealth, JsonObject? scanContext) {
        var parts = new List<string>();

        var source = NormalizeOptional(reportHealth?.GetString("source"));
        if (!string.IsNullOrWhiteSpace(source)) {
            parts.Add("Source: " + source);
        }

        var cachePath = NormalizeOptional(scanContext?.GetString("cachePath"));
        if (!string.IsNullOrWhiteSpace(cachePath)) {
            parts.Add("Cache: " + cachePath);
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    private static string ResolveSectionTelemetryMode(UsageTelemetryOverviewProviderSection section) {
        return string.Equals(section.MonthlyUsageUnitsLabel, "tokens", StringComparison.OrdinalIgnoreCase)
            ? "Token telemetry"
            : "Activity telemetry";
    }

    private static string? BuildSectionTelemetryModeCopy(UsageTelemetryOverviewProviderSection section) {
        var parts = new List<string>();
        if (section.TotalTokens > 0) {
            parts.Add(UsageTelemetryOverviewHtmlFragments.FormatCompact(section.TotalTokens) + " total tokens");
        }
        if (section.MonthlyUsage.Count > 0) {
            parts.Add(section.MonthlyUsage.Count.ToString(CultureInfo.InvariantCulture) + " monthly points");
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    private static string BuildSectionCoverageValue(UsageTelemetryOverviewProviderSection section) {
        var totalDays = Math.Max(section.TotalDays, 1);
        return section.ActiveDays.ToString(CultureInfo.InvariantCulture)
               + "/"
               + totalDays.ToString(CultureInfo.InvariantCulture)
               + " active days";
    }

    private static string? BuildSectionCoverageCopy(UsageTelemetryOverviewProviderSection section) {
        if (section.RangeStartUtc.HasValue && section.RangeEndUtc.HasValue) {
            return section.RangeStartUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                   + " to "
                   + section.RangeEndUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return NormalizeOptional(section.Subtitle);
    }

    private static string? BuildSectionLatestValue(UsageTelemetryOverviewProviderSection section) {
        if (section.LatestEventUtc.HasValue) {
            return section.LatestEventUtc.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
        }

        if (section.RangeEndUtc.HasValue) {
            return section.RangeEndUtc.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        }

        return null;
    }

    private static string BuildSectionLatestCopy(UsageTelemetryOverviewProviderSection section) {
        if (section.RangeEndUtc.HasValue && section.LatestEventUtc.HasValue) {
            var latestDayUtc = section.LatestEventUtc.Value.UtcDateTime.Date;
            if (latestDayUtc < section.RangeEndUtc.Value.Date) {
                return "Latest provider event landed before the trailing window end.";
            }
        }

        return "Based on the newest event retained in this provider slice.";
    }

    private static string? BuildSectionScopeValue(UsageTelemetryOverviewProviderSection section) {
        var parts = new List<string>();
        if (section.AccountCount > 0) {
            parts.Add(section.AccountCount.ToString(CultureInfo.InvariantCulture) + " accounts");
        }
        if (section.SourceRootCount > 0) {
            parts.Add(section.SourceRootCount.ToString(CultureInfo.InvariantCulture) + " roots");
        }
        if (section.TopModels.Count > 0) {
            parts.Add(section.TopModels.Count.ToString(CultureInfo.InvariantCulture) + " top models");
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    private static string? BuildSectionScopeCopy(UsageTelemetryOverviewProviderSection section) {
        return section.Heatmap.Sections.Count > 0
            ? section.Heatmap.Sections.Count.ToString(CultureInfo.InvariantCulture) + " heatmap bands in this provider view"
            : null;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
