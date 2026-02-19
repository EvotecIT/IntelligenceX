using System;
using System.Collections.Concurrent;

namespace IntelligenceX.Tools;

/// <summary>
/// Probe metadata used to validate recent authentication/connectivity preflight checks.
/// </summary>
public sealed class ToolAuthenticationProbeRecord {
    /// <summary>
    /// Opaque probe identifier returned to callers.
    /// </summary>
    public string ProbeId { get; set; } = string.Empty;

    /// <summary>
    /// Tool name that produced the probe.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Authentication contract identifier for compatibility checks.
    /// </summary>
    public string AuthenticationContractId { get; set; } = string.Empty;

    /// <summary>
    /// Stable fingerprint of endpoint/auth context probed by the tool.
    /// </summary>
    public string TargetFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when probe completed.
    /// </summary>
    public DateTimeOffset ProbedAtUtc { get; set; }

    /// <summary>
    /// True when probe completed successfully.
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Optional probe error code when <see cref="IsSuccessful"/> is false.
    /// </summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Stores probe results that can be referenced by follow-up tool calls.
/// </summary>
public interface IToolAuthenticationProbeStore {
    /// <summary>
    /// Inserts or replaces a probe record.
    /// </summary>
    /// <param name="record">Probe record to persist.</param>
    void Upsert(ToolAuthenticationProbeRecord record);

    /// <summary>
    /// Looks up a probe record by id.
    /// </summary>
    /// <param name="probeId">Probe identifier.</param>
    /// <param name="record">Resolved record when found.</param>
    /// <returns>True when record exists.</returns>
    bool TryGet(string probeId, out ToolAuthenticationProbeRecord record);
}

/// <summary>
/// Thread-safe in-memory probe store suitable for host-local execution.
/// </summary>
public sealed class InMemoryToolAuthenticationProbeStore : IToolAuthenticationProbeStore {
    private readonly ConcurrentDictionary<string, ToolAuthenticationProbeRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(ToolAuthenticationProbeRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }
        if (string.IsNullOrWhiteSpace(record.ProbeId)) {
            throw new ArgumentException("ProbeId is required.", nameof(record));
        }

        _records[record.ProbeId.Trim()] = record;
    }

    /// <inheritdoc />
    public bool TryGet(string probeId, out ToolAuthenticationProbeRecord record) {
        if (string.IsNullOrWhiteSpace(probeId)) {
            record = new ToolAuthenticationProbeRecord();
            return false;
        }

        return _records.TryGetValue(probeId.Trim(), out record!);
    }
}
