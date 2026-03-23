using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Plain-language local-vs-online scope explanation for provider telemetry.
/// </summary>
public sealed class UsageTelemetryScopeSummary {
    /// <summary>
    /// Initializes a new scope summary.
    /// </summary>
    public UsageTelemetryScopeSummary(
        string? localScopeText,
        string? onlineScopeText,
        string? differenceText) {
        LocalScopeText = NormalizeOptional(localScopeText);
        OnlineScopeText = NormalizeOptional(onlineScopeText);
        DifferenceText = NormalizeOptional(differenceText);
    }

    /// <summary>
    /// Gets the local telemetry scope text.
    /// </summary>
    public string? LocalScopeText { get; }

    /// <summary>
    /// Gets the online/live account scope text.
    /// </summary>
    public string? OnlineScopeText { get; }

    /// <summary>
    /// Gets the provider-specific explanation for why local and online views can differ.
    /// </summary>
    public string? DifferenceText { get; }

    /// <summary>
    /// Gets a value indicating whether any scope text is available.
    /// </summary>
    public bool HasAnyText =>
        LocalScopeText is not null
        || OnlineScopeText is not null
        || DifferenceText is not null;

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Builds human-readable local-vs-online scope explanations for usage telemetry providers.
/// </summary>
public static class UsageTelemetryScopeSummaryBuilder {
    /// <summary>
    /// Builds a scope summary for a provider.
    /// </summary>
    public static UsageTelemetryScopeSummary Build(
        string? providerId,
        IReadOnlyList<SourceRootRecord>? sourceRoots,
        ProviderLimitSnapshot? limitSnapshot) {
        var normalizedProviderId = NormalizeOptional(providerId);
        if (string.IsNullOrWhiteSpace(normalizedProviderId)
            || string.Equals(normalizedProviderId, "__all__", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedProviderId, "__github__", StringComparison.OrdinalIgnoreCase)) {
            return new UsageTelemetryScopeSummary(null, null, null);
        }

        var canonicalProviderId = UsageTelemetryProviderCatalog.ResolveCanonicalProviderId(normalizedProviderId)
                                  ?? normalizedProviderId!;
        var providerTitle = UsageTelemetryProviderCatalog.ResolveDisplayTitle(canonicalProviderId);
        var providerRoots = (sourceRoots ?? Array.Empty<SourceRootRecord>())
            .Where(root => UsageTelemetryProviderCatalog.IsProvider(root.ProviderId, canonicalProviderId))
            .ToArray();

        return new UsageTelemetryScopeSummary(
            BuildLocalScopeText(providerTitle, providerRoots),
            BuildOnlineScopeText(providerTitle, limitSnapshot),
            BuildDifferenceText(canonicalProviderId, providerRoots, limitSnapshot));
    }

    private static string? BuildLocalScopeText(string providerTitle, IReadOnlyList<SourceRootRecord> providerRoots) {
        if (providerRoots.Count == 0) {
            return null;
        }

        var rootLabel = providerRoots.Count == 1 ? "root" : "roots";
        var text = providerTitle
                   + " local activity comes from "
                   + providerRoots.Count.ToString(CultureInfo.InvariantCulture)
                   + " discovered "
                   + rootLabel;

        var familyBreakdown = BuildRootFamilyBreakdown(providerRoots);
        if (!string.IsNullOrWhiteSpace(familyBreakdown)) {
            text += " (" + familyBreakdown + ")";
        }

        text += ". Daily activity, models, accounts, surfaces, and recent events only reflect these local artifacts.";

        var machineSummary = BuildMachineSummary(providerRoots);
        if (!string.IsNullOrWhiteSpace(machineSummary)) {
            text += " Machines: " + machineSummary + ".";
        }

        return text;
    }

    private static string? BuildOnlineScopeText(string providerTitle, ProviderLimitSnapshot? limitSnapshot) {
        if (limitSnapshot is null) {
            return null;
        }

        var sourceLabel = NormalizeOptional(limitSnapshot.SourceLabel) ?? (providerTitle + " live limits");
        var text = "LIVE LIMITS come from " + sourceLabel + ".";

        var accountParts = new List<string>();
        var planLabel = NormalizeOptional(limitSnapshot.PlanLabel);
        var accountLabel = NormalizeOptional(limitSnapshot.AccountLabel);
        if (planLabel is not null) {
            accountParts.Add(planLabel);
        }
        if (accountLabel is not null) {
            accountParts.Add(accountLabel);
        }

        if (accountParts.Count > 0) {
            text += " Account view: " + string.Join(" • ", accountParts) + ".";
        }

        if (limitSnapshot.Windows.Count > 0) {
            text += " These windows are account-wide online usage, not just the local roots above.";
        } else if (!string.IsNullOrWhiteSpace(limitSnapshot.DetailMessage)) {
            text += " " + limitSnapshot.DetailMessage!.Trim();
        }

        return text;
    }

    private static string? BuildDifferenceText(
        string canonicalProviderId,
        IReadOnlyList<SourceRootRecord> providerRoots,
        ProviderLimitSnapshot? limitSnapshot) {
        var hasLocalRoots = providerRoots.Count > 0;
        var hasLiveSnapshot = limitSnapshot is not null && (!string.IsNullOrWhiteSpace(limitSnapshot.DetailMessage) || limitSnapshot.Windows.Count > 0);
        if (!hasLocalRoots && !hasLiveSnapshot) {
            return null;
        }

        if (string.Equals(canonicalProviderId, "claude", StringComparison.OrdinalIgnoreCase)) {
            return "Why they can differ: Claude live windows are account-wide and can include claude.ai web/app usage, other machines, WSL profiles, recovered folders, and sessions outside the currently discovered local project logs.";
        }

        return "Why they can differ: live account windows can include activity from other machines or provider surfaces that are not present in the discovered local telemetry roots.";
    }

    private static string? BuildRootFamilyBreakdown(IReadOnlyList<SourceRootRecord> providerRoots) {
        var parts = new List<string>();
        var wslCount = providerRoots.Count(root =>
            string.Equals(root.PlatformHint, "wsl", StringComparison.OrdinalIgnoreCase));
        var recoveredCount = providerRoots.Count(root => root.SourceKind == UsageSourceKind.RecoveredFolder);
        var localCount = Math.Max(0, providerRoots.Count - wslCount - recoveredCount);

        if (localCount > 0) {
            parts.Add(localCount.ToString(CultureInfo.InvariantCulture) + " local");
        }
        if (wslCount > 0) {
            parts.Add(wslCount.ToString(CultureInfo.InvariantCulture) + " WSL");
        }
        if (recoveredCount > 0) {
            parts.Add(recoveredCount.ToString(CultureInfo.InvariantCulture) + " recovered");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? BuildMachineSummary(IReadOnlyList<SourceRootRecord> providerRoots) {
        var machines = providerRoots
            .Select(root => NormalizeOptional(root.MachineLabel))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Cast<string>()
            .ToArray();
        if (machines.Length == 0) {
            return null;
        }

        return string.Join(", ", machines);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
