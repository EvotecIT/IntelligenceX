using System;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>Controls local history selection and optional reuse of parsed, content-free observations.</summary>
public sealed class AgentWorkOptions {
    /// <summary>Recent activity window; defaults to two days. Null explicitly requests full history.</summary>
    public TimeSpan? ActiveWithin { get; init; } = TimeSpan.FromDays(2);
    /// <summary>Reconciles missed filesystem events and clock-based state changes; defaults to thirty seconds.</summary>
    public TimeSpan ReconcileInterval { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Cadence for optional Desktop metadata reads; defaults to thirty seconds.</summary>
    public TimeSpan MetadataRefreshInterval { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Cadence for optional conditional GitHub reads; defaults to five minutes.</summary>
    public TimeSpan GitHubRefreshInterval { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Quiet interval after which a known unfinished turn stops being inferred as running.</summary>
    public TimeSpan ActiveTurnWindow { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Inactivity threshold for sessions linked to an open pull request.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromDays(7);
    /// <summary>Optional existing telemetry artifact store. The caller owns its lifetime; no transcripts are stored.</summary>
    public IRawArtifactStore? Cache { get; init; }

    internal DateTimeOffset? Since(DateTimeOffset nowUtc) {
        if (ActiveWithin is not { } window) return null;
        var now = nowUtc.ToUniversalTime();
        return window > now - DateTimeOffset.MinValue ? DateTimeOffset.MinValue : now - window;
    }

    internal void Validate() {
        if (ActiveWithin <= TimeSpan.Zero || ActiveTurnWindow <= TimeSpan.Zero || StaleAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ActiveWithin));
        foreach (var interval in new[] { ReconcileInterval, MetadataRefreshInterval, GitHubRefreshInterval }) {
            if (interval <= TimeSpan.Zero || interval.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(ReconcileInterval), "Refresh intervals must fit a positive timer interval.");
        }
    }
}

/// <summary>Work performed on session logs, excluding title indexes, workspace and database metadata.</summary>
public sealed record AgentWorkScanStatistics {
    /// <summary>Files parsed during this scan.</summary>
    public int FilesRead { get; init; }
    /// <summary>Unchanged files served from the parsed observation cache.</summary>
    public int FilesReused { get; init; }
    /// <summary>Files excluded by the modification-time prefilter.</summary>
    public int FilesSkipped { get; init; }
    /// <summary>Files resumed from a verified append checkpoint.</summary>
    public int FilesTailed { get; init; }
    /// <summary>Bytes read by the JSONL parser, excluding small checkpoint anchors and metadata.</summary>
    public long BytesRead { get; init; }
}
