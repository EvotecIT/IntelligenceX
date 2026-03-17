using System;
using System.Collections.Generic;

#pragma warning disable CS1591

namespace IntelligenceX.Telemetry.Limits;

/// <summary>
/// Shared live rate-limit snapshot for a provider.
/// </summary>
public sealed class ProviderLimitSnapshot {
    /// <summary>
    /// Initializes a new provider limit snapshot.
    /// </summary>
    public ProviderLimitSnapshot(
        string providerId,
        string displayName,
        string sourceLabel,
        string? planLabel,
        string? accountLabel,
        IReadOnlyList<ProviderLimitWindow> windows,
        string? summary,
        string? detailMessage,
        DateTimeOffset retrievedAtUtc) {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        SourceLabel = sourceLabel ?? throw new ArgumentNullException(nameof(sourceLabel));
        PlanLabel = planLabel;
        AccountLabel = accountLabel;
        Windows = windows ?? Array.Empty<ProviderLimitWindow>();
        Summary = summary;
        DetailMessage = detailMessage;
        RetrievedAtUtc = retrievedAtUtc;
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string SourceLabel { get; }
    public string? PlanLabel { get; }
    public string? AccountLabel { get; }
    public IReadOnlyList<ProviderLimitWindow> Windows { get; }
    public string? Summary { get; }
    public string? DetailMessage { get; }
    public DateTimeOffset RetrievedAtUtc { get; }
    public bool IsAvailable => Windows.Count > 0;
}

/// <summary>
/// Single rate-limit window for a provider.
/// </summary>
public sealed class ProviderLimitWindow {
    /// <summary>
    /// Initializes a new rate-limit window.
    /// </summary>
    public ProviderLimitWindow(
        string key,
        string label,
        double? usedPercent,
        DateTimeOffset? resetsAt,
        string? detail = null) {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        UsedPercent = usedPercent;
        ResetsAt = resetsAt;
        Detail = detail;
    }

    public string Key { get; }
    public string Label { get; }
    public double? UsedPercent { get; }
    public DateTimeOffset? ResetsAt { get; }
    public string? Detail { get; }
}
