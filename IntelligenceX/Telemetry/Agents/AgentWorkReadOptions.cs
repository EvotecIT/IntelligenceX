using System;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>Controls local history selection and optional reuse of parsed, content-free observations.</summary>
public sealed class AgentWorkReadOptions {
    /// <summary>Includes sessions active on or after this time. File modification times provide a conservative prefilter.</summary>
    public DateTimeOffset? SinceUtc { get; init; }
    /// <summary>Optional existing telemetry artifact store. The caller owns its lifetime; no transcripts are stored.</summary>
    public IRawArtifactStore? Cache { get; init; }
}

/// <summary>Work performed on session logs, excluding title indexes, workspace and database metadata.</summary>
public sealed record AgentWorkScanStatistics {
    /// <summary>Files parsed during this scan.</summary>
    public int FilesRead { get; init; }
    /// <summary>Unchanged files served from the parsed observation cache.</summary>
    public int FilesReused { get; init; }
    /// <summary>Files excluded by the modification-time prefilter.</summary>
    public int FilesSkipped { get; init; }
    /// <summary>Total initial sizes of files parsed, including tool payloads that are discarded.</summary>
    public long BytesRead { get; init; }
}
