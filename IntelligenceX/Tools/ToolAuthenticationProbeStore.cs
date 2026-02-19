using System;
using System.Collections.Concurrent;
using System.Linq;

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
    private const int DefaultMaxRecords = 1000;
    private static readonly TimeSpan DefaultMaxRecordAge = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, ToolAuthenticationProbeRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxRecords;
    private readonly TimeSpan _maxRecordAge;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryToolAuthenticationProbeStore"/> class.
    /// </summary>
    /// <param name="maxRecords">Maximum number of records retained in memory.</param>
    /// <param name="maxRecordAge">Maximum age for retained records.</param>
    /// <param name="utcNowProvider">Optional UTC clock provider used for retention checks.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRecords"/> or <paramref name="maxRecordAge"/> is invalid.</exception>
    public InMemoryToolAuthenticationProbeStore(
        int maxRecords = DefaultMaxRecords,
        TimeSpan? maxRecordAge = null,
        Func<DateTimeOffset>? utcNowProvider = null) {
        if (maxRecords <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxRecords), "maxRecords must be positive.");
        }

        var resolvedMaxRecordAge = maxRecordAge ?? DefaultMaxRecordAge;
        if (resolvedMaxRecordAge <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(maxRecordAge), "maxRecordAge must be positive.");
        }

        _maxRecords = maxRecords;
        _maxRecordAge = resolvedMaxRecordAge;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void Upsert(ToolAuthenticationProbeRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }
        if (string.IsNullOrWhiteSpace(record.ProbeId)) {
            throw new ArgumentException("ProbeId is required.", nameof(record));
        }

        _records[record.ProbeId.Trim()] = record;
        Prune(_utcNowProvider());
    }

    /// <inheritdoc />
    public bool TryGet(string probeId, out ToolAuthenticationProbeRecord record) {
        if (string.IsNullOrWhiteSpace(probeId)) {
            record = new ToolAuthenticationProbeRecord();
            return false;
        }

        var nowUtc = _utcNowProvider();
        Prune(nowUtc);

        var normalizedProbeId = probeId.Trim();
        if (!_records.TryGetValue(normalizedProbeId, out record!)) {
            return false;
        }

        if (record.ProbedAtUtc + _maxRecordAge < nowUtc) {
            _records.TryRemove(normalizedProbeId, out _);
            record = new ToolAuthenticationProbeRecord();
            return false;
        }

        return true;
    }

    private void Prune(DateTimeOffset nowUtc) {
        foreach (var pair in _records) {
            if (pair.Value.ProbedAtUtc + _maxRecordAge < nowUtc) {
                _records.TryRemove(pair.Key, out _);
            }
        }

        var count = _records.Count;
        if (count <= _maxRecords) {
            return;
        }

        var overflow = count - _maxRecords;
        var keysToEvict = _records
            .OrderBy(pair => pair.Value.ProbedAtUtc)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(overflow)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in keysToEvict) {
            _records.TryRemove(key, out _);
        }
    }
}
