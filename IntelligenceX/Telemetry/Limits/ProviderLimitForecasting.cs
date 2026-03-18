using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Telemetry.Limits;

#pragma warning disable CS1591

public sealed class ProviderLimitWindowForecast {
    public ProviderLimitWindowForecast(
        string key,
        double paceMultiple,
        double projectedUsedPercentAtReset,
        bool exhaustsBeforeReset,
        DateTimeOffset? estimatedExhaustionAtUtc,
        string? summary) {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        PaceMultiple = Math.Max(0d, paceMultiple);
        ProjectedUsedPercentAtReset = Math.Max(0d, projectedUsedPercentAtReset);
        ExhaustsBeforeReset = exhaustsBeforeReset;
        EstimatedExhaustionAtUtc = estimatedExhaustionAtUtc;
        Summary = NormalizeOptional(summary);
    }

    public string Key { get; }
    public double PaceMultiple { get; }
    public double ProjectedUsedPercentAtReset { get; }
    public bool ExhaustsBeforeReset { get; }
    public DateTimeOffset? EstimatedExhaustionAtUtc { get; }
    public string? Summary { get; }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

public sealed class ProviderLimitAccountAdvisory {
    public ProviderLimitAccountAdvisory(
        string? accountId,
        string displayLabel,
        string? planLabel,
        string? hottestWindowLabel,
        string? statusLabel,
        string? summary,
        double riskScore,
        bool isRecommended,
        bool isSelected) {
        AccountId = NormalizeOptional(accountId);
        DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? "Unknown account" : displayLabel.Trim();
        PlanLabel = NormalizeOptional(planLabel);
        HottestWindowLabel = NormalizeOptional(hottestWindowLabel);
        StatusLabel = NormalizeOptional(statusLabel);
        Summary = NormalizeOptional(summary);
        RiskScore = riskScore;
        IsRecommended = isRecommended;
        IsSelected = isSelected;
    }

    public string? AccountId { get; }
    public string DisplayLabel { get; }
    public string? PlanLabel { get; }
    public string? HottestWindowLabel { get; }
    public string? StatusLabel { get; }
    public string? Summary { get; }
    public double RiskScore { get; }
    public bool IsRecommended { get; }
    public bool IsSelected { get; }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Builds reusable live-limit pace and runway forecasts from provider windows.
/// </summary>
public static class ProviderLimitForecasting {
    public static IReadOnlyDictionary<string, ProviderLimitWindowForecast> BuildForecasts(
        ProviderLimitSnapshot? snapshot,
        DateTimeOffset? nowUtc = null) {
        var results = new Dictionary<string, ProviderLimitWindowForecast>(StringComparer.OrdinalIgnoreCase);
        if (snapshot is null || snapshot.Windows.Count == 0) {
            return results;
        }

        var effectiveNow = nowUtc ?? snapshot.RetrievedAtUtc;
        foreach (var window in snapshot.Windows) {
            var forecast = BuildForecast(window, effectiveNow);
            if (forecast is not null) {
                results[window.Key] = forecast;
            }
        }

        return results;
    }

    public static ProviderLimitWindowForecast? BuildForecast(ProviderLimitWindow? window, DateTimeOffset nowUtc) {
        if (window is null
            || !window.UsedPercent.HasValue
            || !window.ResetsAt.HasValue
            || !window.WindowDuration.HasValue
            || window.WindowDuration.Value <= TimeSpan.Zero) {
            return null;
        }

        var remaining = window.ResetsAt.Value - nowUtc;
        if (remaining <= TimeSpan.Zero || remaining > window.WindowDuration.Value) {
            return null;
        }

        var elapsed = window.WindowDuration.Value - remaining;
        if (elapsed <= TimeSpan.Zero) {
            return null;
        }

        var usedPercent = Math.Max(0d, window.UsedPercent.Value);
        var elapsedFraction = elapsed.TotalSeconds / window.WindowDuration.Value.TotalSeconds;
        if (elapsedFraction <= 0d) {
            return null;
        }

        var projectedUsedPercent = usedPercent <= 0d ? 0d : usedPercent / elapsedFraction;
        var paceMultiple = projectedUsedPercent / 100d;
        var percentPerSecond = usedPercent <= 0d ? 0d : usedPercent / elapsed.TotalSeconds;
        var remainingPercent = Math.Max(0d, 100d - usedPercent);
        var secondsToExhaust = percentPerSecond <= 0d ? double.PositiveInfinity : remainingPercent / percentPerSecond;
        var exhaustsBeforeReset = secondsToExhaust < (remaining.TotalSeconds - 1d);
        var timeToExhaust = exhaustsBeforeReset && !double.IsInfinity(secondsToExhaust)
            ? TimeSpan.FromSeconds(secondsToExhaust)
            : (TimeSpan?)null;
        var estimatedExhaustionAtUtc = timeToExhaust.HasValue ? nowUtc.Add(timeToExhaust.Value) : (DateTimeOffset?)null;

        return new ProviderLimitWindowForecast(
            window.Key,
            paceMultiple,
            projectedUsedPercent,
            exhaustsBeforeReset,
            estimatedExhaustionAtUtc,
            BuildSummary(paceMultiple, projectedUsedPercent, timeToExhaust));
    }

    public static IReadOnlyList<ProviderLimitAccountAdvisory> BuildAccountAdvisories(
        ProviderLimitSnapshot? snapshot,
        DateTimeOffset? nowUtc = null) {
        if (snapshot is null) {
            return Array.Empty<ProviderLimitAccountAdvisory>();
        }

        var effectiveNow = nowUtc ?? snapshot.RetrievedAtUtc;
        var accounts = snapshot.Accounts.Count > 0
            ? snapshot.Accounts
            : new[] {
                new ProviderLimitAccountSnapshot(
                    accountId: null,
                    accountLabel: snapshot.AccountLabel,
                    planLabel: snapshot.PlanLabel,
                    windows: snapshot.Windows,
                    summary: snapshot.Summary,
                    detailMessage: snapshot.DetailMessage,
                    retrievedAtUtc: snapshot.RetrievedAtUtc,
                    isSelected: true)
            };

        var advisories = accounts
            .Select(account => BuildAccountAdvisory(account, effectiveNow))
            .Where(static advisory => advisory is not null)
            .Cast<ProviderLimitAccountAdvisory>()
            .OrderBy(static advisory => advisory.RiskScore)
            .ThenBy(static advisory => advisory.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (advisories.Count == 0) {
            return Array.Empty<ProviderLimitAccountAdvisory>();
        }

        var recommendedIndex = advisories.FindIndex(static advisory => advisory.RiskScore < double.MaxValue);
        if (recommendedIndex < 0) {
            recommendedIndex = 0;
        }

        var recommended = advisories[recommendedIndex];
        advisories[recommendedIndex] = new ProviderLimitAccountAdvisory(
            recommended.AccountId,
            recommended.DisplayLabel,
            recommended.PlanLabel,
            recommended.HottestWindowLabel,
            recommended.StatusLabel,
            recommended.Summary,
            recommended.RiskScore,
            isRecommended: true,
            recommended.IsSelected);
        return advisories;
    }

    private static ProviderLimitAccountAdvisory? BuildAccountAdvisory(
        ProviderLimitAccountSnapshot account,
        DateTimeOffset nowUtc) {
        if (account is null) {
            return null;
        }

        ProviderLimitWindow? hottestWindow = null;
        ProviderLimitWindowForecast? hottestForecast = null;
        var riskScore = double.MaxValue;

        foreach (var window in account.Windows) {
            var forecast = BuildForecast(window, nowUtc);
            var windowRisk = forecast?.ProjectedUsedPercentAtReset
                             ?? window.UsedPercent
                             ?? double.MaxValue;
            if (hottestWindow is null || windowRisk > riskScore) {
                hottestWindow = window;
                hottestForecast = forecast;
                riskScore = windowRisk;
            }
        }

        var displayLabel = NormalizeOptional(account.AccountLabel)
                           ?? NormalizeOptional(account.AccountId)
                           ?? "Unknown account";
        var statusLabel = BuildAccountStatusLabel(hottestWindow, hottestForecast, riskScore);
        var summary = BuildAccountSummary(account, hottestWindow, hottestForecast, riskScore, nowUtc);

        return new ProviderLimitAccountAdvisory(
            account.AccountId,
            displayLabel,
            account.PlanLabel,
            hottestWindow?.Label,
            statusLabel,
            summary,
            hottestWindow is null ? double.MaxValue : riskScore,
            isRecommended: false,
            account.IsSelected);
    }

    private static string BuildSummary(double paceMultiple, double projectedUsedPercent, TimeSpan? timeToExhaust) {
        if (projectedUsedPercent <= 0.05d) {
            return "No usage yet";
        }

        if (timeToExhaust.HasValue) {
            if (timeToExhaust.Value <= TimeSpan.FromMinutes(1)) {
                return paceMultiple.ToString("0.0", CultureInfo.InvariantCulture)
                       + "x sustainable pace • limit imminent";
            }

            return paceMultiple.ToString("0.0", CultureInfo.InvariantCulture)
                   + "x sustainable pace • limit in "
                   + FormatDuration(timeToExhaust.Value);
        }

        if (Math.Abs(paceMultiple - 1d) <= 0.05d) {
            return "On pace • projects "
                   + projectedUsedPercent.ToString("0.#", CultureInfo.InvariantCulture)
                   + "% by reset";
        }

        return paceMultiple.ToString("0.0", CultureInfo.InvariantCulture)
               + "x sustainable pace • projects "
               + projectedUsedPercent.ToString("0.#", CultureInfo.InvariantCulture)
               + "% by reset";
    }

    private static string FormatDuration(TimeSpan duration) {
        if (duration <= TimeSpan.Zero) {
            return "under 1m";
        }

        if (duration.TotalDays >= 1d) {
            return Math.Floor(duration.TotalDays).ToString("0", CultureInfo.InvariantCulture) + "d";
        }

        if (duration.TotalHours >= 1d) {
            var hours = Math.Floor(duration.TotalHours);
            var minutes = duration.Minutes;
            return minutes <= 0
                ? hours.ToString("0", CultureInfo.InvariantCulture) + "h"
                : hours.ToString("0", CultureInfo.InvariantCulture) + "h " + minutes.ToString("0", CultureInfo.InvariantCulture) + "m";
        }

        if (duration.TotalMinutes >= 1d) {
            return Math.Floor(duration.TotalMinutes).ToString("0", CultureInfo.InvariantCulture) + "m";
        }

        return Math.Max(1d, Math.Ceiling(duration.TotalSeconds)).ToString("0", CultureInfo.InvariantCulture) + "s";
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string BuildAccountStatusLabel(
        ProviderLimitWindow? hottestWindow,
        ProviderLimitWindowForecast? hottestForecast,
        double riskScore) {
        if (hottestWindow is null) {
            return "Unknown";
        }

        if (hottestForecast?.ExhaustsBeforeReset == true || (hottestWindow.UsedPercent ?? 0d) >= 95d) {
            return "Avoid now";
        }

        if (riskScore >= 80d) {
            return "Tight";
        }

        if ((hottestWindow.UsedPercent ?? 0d) <= 0.05d) {
            return "Clear";
        }

        return "Available";
    }

    private static string BuildAccountSummary(
        ProviderLimitAccountSnapshot account,
        ProviderLimitWindow? hottestWindow,
        ProviderLimitWindowForecast? hottestForecast,
        double riskScore,
        DateTimeOffset nowUtc) {
        if (hottestWindow is null) {
            return NormalizeOptional(account.DetailMessage)
                   ?? NormalizeOptional(account.Summary)
                   ?? "No live limit data";
        }

        if ((hottestWindow.UsedPercent ?? 0d) <= 0.05d) {
            return "No usage yet across the tracked windows.";
        }

        if (hottestForecast?.ExhaustsBeforeReset == true) {
            if (hottestForecast.EstimatedExhaustionAtUtc.HasValue) {
                var remaining = hottestForecast.EstimatedExhaustionAtUtc.Value - nowUtc;
                return "If you keep this pace, "
                       + hottestWindow.Label
                       + " runs out in "
                       + FormatDuration(remaining)
                       + ".";
            }

            return "If you keep this pace, "
                   + hottestWindow.Label
                   + " runs out before reset.";
        }

        if (riskScore >= 80d) {
            return "If you keep this pace, "
                   + hottestWindow.Label
                   + " reaches about "
                   + riskScore.ToString("0.#", CultureInfo.InvariantCulture)
                   + "% by reset.";
        }

        return "At this pace, "
               + hottestWindow.Label
               + " reaches about "
               + riskScore.ToString("0.#", CultureInfo.InvariantCulture)
               + "% by reset.";
    }
}

#pragma warning restore CS1591
