using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Telemetry.Usage;

#pragma warning disable CS1591

/// <summary>
/// One estimated API pricing driver aggregated from usage telemetry.
/// </summary>
public sealed class UsageTelemetryApiCostDriver {
    public UsageTelemetryApiCostDriver(string model, decimal estimatedCostUsd, double sharePercent) {
        Model = string.IsNullOrWhiteSpace(model) ? "unknown-model" : model.Trim();
        EstimatedCostUsd = estimatedCostUsd < 0m ? 0m : estimatedCostUsd;
        SharePercent = Math.Max(0d, sharePercent);
    }

    public string Model { get; }
    public decimal EstimatedCostUsd { get; }
    public double SharePercent { get; }
}

/// <summary>
/// Estimated API pricing summary for a set of telemetry events.
/// </summary>
public sealed class UsageTelemetryApiCostEstimate {
    public UsageTelemetryApiCostEstimate(
        decimal totalEstimatedCostUsd,
        long coveredTokens,
        long uncoveredTokens,
        IReadOnlyList<UsageTelemetryApiCostDriver> topDrivers) {
        TotalEstimatedCostUsd = totalEstimatedCostUsd < 0m ? 0m : totalEstimatedCostUsd;
        CoveredTokens = Math.Max(0L, coveredTokens);
        UncoveredTokens = Math.Max(0L, uncoveredTokens);
        TopDrivers = topDrivers ?? Array.Empty<UsageTelemetryApiCostDriver>();
    }

    public decimal TotalEstimatedCostUsd { get; }
    public long CoveredTokens { get; }
    public long UncoveredTokens { get; }
    public IReadOnlyList<UsageTelemetryApiCostDriver> TopDrivers { get; }
}

/// <summary>
/// Event-level pricing estimate derived from token telemetry.
/// </summary>
public sealed class UsageTelemetryApiEventCostEstimate {
    public UsageTelemetryApiEventCostEstimate(string model, long totalTokens, decimal estimatedCostUsd, bool hasKnownPricing) {
        Model = string.IsNullOrWhiteSpace(model) ? "unknown-model" : model.Trim();
        TotalTokens = Math.Max(0L, totalTokens);
        EstimatedCostUsd = estimatedCostUsd < 0m ? 0m : estimatedCostUsd;
        HasKnownPricing = hasKnownPricing;
    }

    public string Model { get; }
    public long TotalTokens { get; }
    public decimal EstimatedCostUsd { get; }
    public bool HasKnownPricing { get; }
}

/// <summary>
/// Shared "best available" display cost rollup that blends exact and estimated values.
/// </summary>
public sealed class UsageTelemetryDisplayCost {
    public UsageTelemetryDisplayCost(decimal exactCostUsd, decimal estimatedFallbackCostUsd, long coveredTokens, long uncoveredTokens) {
        ExactCostUsd = exactCostUsd < 0m ? 0m : exactCostUsd;
        EstimatedFallbackCostUsd = estimatedFallbackCostUsd < 0m ? 0m : estimatedFallbackCostUsd;
        CoveredTokens = Math.Max(0L, coveredTokens);
        UncoveredTokens = Math.Max(0L, uncoveredTokens);
    }

    public decimal ExactCostUsd { get; }
    public decimal EstimatedFallbackCostUsd { get; }
    public long CoveredTokens { get; }
    public long UncoveredTokens { get; }
    public decimal TotalCostUsd => ExactCostUsd + EstimatedFallbackCostUsd;
    public bool UsesEstimatedFallback => EstimatedFallbackCostUsd > 0m;
    public bool HasAnyCost => TotalCostUsd > 0m;
}

/// <summary>
/// Shared API-route pricing helper used by reports, tray, and future telemetry projections.
/// </summary>
public static class UsageTelemetryApiPricing {
    private sealed record UsageTelemetryApiPrice(decimal InputUsdPerMillion, decimal? CachedInputUsdPerMillion, decimal OutputUsdPerMillion);

    private static readonly IReadOnlyDictionary<string, UsageTelemetryApiPrice> ApiPricingByModel =
        new Dictionary<string, UsageTelemetryApiPrice>(StringComparer.OrdinalIgnoreCase) {
            ["gpt-5.5"] = new(5m, 0.50m, 30m),
            ["gpt-5.4"] = new(2.50m, 0.25m, 15m),
            ["gpt-5.4-codex"] = new(2.50m, 0.25m, 15m),
            ["gpt-5-mini"] = new(0.25m, 0.025m, 2m),
            ["gpt-5-nano"] = new(0.05m, 0.005m, 0.40m),
            ["gpt-5.3"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.3-codex"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.2"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.2-codex"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.1"] = new(1.25m, 0.125m, 10m),
            ["gpt-5.1-codex"] = new(1.25m, 0.125m, 10m),
            ["gpt-5-codex"] = new(1.25m, 0.125m, 10m),
            ["gpt-5.1-codex-max"] = new(1.25m, 0.125m, 10m),
            ["gpt-5.1-codex-mini"] = new(0.25m, 0.025m, 2m)
        };

    public static UsageTelemetryApiCostEstimate? Estimate(IEnumerable<UsageEventRecord> events, int driverLimit = 5) {
        var eventList = (events ?? Array.Empty<UsageEventRecord>())
            .Where(static record => record is not null)
            .ToArray();
        if (eventList.Length == 0) {
            return null;
        }

        var estimates = eventList
            .Select(EstimateEvent)
            .ToArray();
        var totalEstimatedCost = estimates.Sum(static estimate => estimate.EstimatedCostUsd);
        var coveredTokens = estimates
            .Where(static estimate => estimate.HasKnownPricing)
            .Sum(static estimate => estimate.TotalTokens);
        var uncoveredTokens = estimates
            .Where(static estimate => !estimate.HasKnownPricing)
            .Sum(static estimate => estimate.TotalTokens);

        var topDrivers = estimates
            .Where(static estimate => estimate.HasKnownPricing)
            .GroupBy(static estimate => estimate.Model, StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var cost = group.Sum(static estimate => estimate.EstimatedCostUsd);
                var share = totalEstimatedCost <= 0m ? 0d : (double)(cost / totalEstimatedCost * 100m);
                return new UsageTelemetryApiCostDriver(group.Key, cost, share);
            })
            .Where(static driver => driver.EstimatedCostUsd > 0m)
            .OrderByDescending(static driver => driver.EstimatedCostUsd)
            .ThenBy(static driver => driver.Model, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, driverLimit))
            .ToArray();

        if (totalEstimatedCost <= 0m && coveredTokens <= 0L && uncoveredTokens <= 0L) {
            return null;
        }

        return new UsageTelemetryApiCostEstimate(
            totalEstimatedCost,
            coveredTokens,
            uncoveredTokens,
            topDrivers);
    }

    public static UsageTelemetryDisplayCost BuildDisplayCost(IEnumerable<UsageEventRecord> events) {
        var exactCost = 0m;
        var estimatedFallbackCost = 0m;
        long coveredTokens = 0;
        long uncoveredTokens = 0;

        foreach (var record in events ?? Array.Empty<UsageEventRecord>()) {
            if (record is null) {
                continue;
            }

            if (record.CostUsd is > 0m) {
                exactCost += record.CostUsd.Value;
                continue;
            }

            var estimate = EstimateEvent(record);
            if (estimate.HasKnownPricing) {
                estimatedFallbackCost += estimate.EstimatedCostUsd;
                coveredTokens += estimate.TotalTokens;
            } else {
                uncoveredTokens += estimate.TotalTokens;
            }
        }

        return new UsageTelemetryDisplayCost(exactCost, estimatedFallbackCost, coveredTokens, uncoveredTokens);
    }

    public static UsageTelemetryDisplayCost BuildDisplayCost(UsageEventRecord record) {
        return BuildDisplayCost(record is null ? Array.Empty<UsageEventRecord>() : new[] { record });
    }

    public static UsageTelemetryApiEventCostEstimate EstimateEvent(UsageEventRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        var normalizedModel = NormalizePricingModelId(record.ProviderId, record.Model);
        var totalTokens = record.TotalTokens ?? 0L;
        if (!TryResolveApiPrice(normalizedModel, out var rate)) {
            return new UsageTelemetryApiEventCostEstimate(
                normalizedModel,
                totalTokens,
                0m,
                hasKnownPricing: false);
        }

        var inputTokens = Math.Max(0L, record.InputTokens ?? 0L);
        var cachedInputTokens = Math.Max(0L, record.CachedInputTokens ?? 0L);
        var outputTokens = Math.Max(0L, record.OutputTokens ?? 0L);
        var reasoningTokens = Math.Max(0L, record.ReasoningTokens ?? 0L);
        var effectiveOutputTokens = outputTokens + reasoningTokens;

        var estimatedCostUsd =
            ComputePerMillionCost(inputTokens, rate.InputUsdPerMillion) +
            ComputePerMillionCost(cachedInputTokens, rate.CachedInputUsdPerMillion ?? rate.InputUsdPerMillion) +
            ComputePerMillionCost(effectiveOutputTokens, rate.OutputUsdPerMillion);

        return new UsageTelemetryApiEventCostEstimate(
            normalizedModel,
            totalTokens,
            estimatedCostUsd,
            hasKnownPricing: true);
    }

    private static decimal ComputePerMillionCost(long tokens, decimal usdPerMillion) {
        if (tokens <= 0L || usdPerMillion <= 0m) {
            return 0m;
        }

        return tokens / 1_000_000m * usdPerMillion;
    }

    private static string NormalizePricingModelId(string? providerId, string? model) {
        var provider = NormalizeOptional(providerId)?.ToLowerInvariant();
        if (provider is "codex" or "openai" or "ix" or "openai-codex" or "chatgpt-codex") {
            return OpenAIModelCatalog.NormalizeModelId(model, "unknown-model").Trim().ToLowerInvariant();
        }

        return NormalizeOptional(model)?.ToLowerInvariant() ?? "unknown-model";
    }

    private static bool TryResolveApiPrice(string normalizedModelId, out UsageTelemetryApiPrice rate) {
        if (ApiPricingByModel.TryGetValue(normalizedModelId, out rate!)) {
            return true;
        }

        if (normalizedModelId.StartsWith("claude-opus-4-6", StringComparison.OrdinalIgnoreCase) ||
            normalizedModelId.StartsWith("claude-opus-4-5", StringComparison.OrdinalIgnoreCase) ||
            normalizedModelId.StartsWith("claude-opus-4", StringComparison.OrdinalIgnoreCase)) {
            rate = new UsageTelemetryApiPrice(5m, null, 25m);
            return true;
        }

        rate = null!;
        return false;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

#pragma warning restore CS1591
