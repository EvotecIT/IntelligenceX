using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Safety and output limits for event log tools.
/// </summary>
public sealed class EventLogToolOptions : IToolPackRuntimeConfigurable {
    // Hard upper bounds: even if the host misconfigures options, keep filesystem scanning conservative.
    // These are intentionally higher than defaults but low enough to avoid "scan the whole disk" incidents.
    private const int EvtxFindMaxDepthUpper = 32;
    private const int EvtxFindMaxDirsScannedUpper = 50_000;
    private const int EvtxFindMaxFilesScannedUpper = 200_000;

    /// <summary>
    /// Allowed root directories for any file-based log operations (for example: EVTX parsing).
    /// When empty, file-based operations are denied.
    /// </summary>
    public List<string> AllowedRoots { get; } = new();

    /// <summary>
    /// Maximum number of events returned for list/query operations.
    /// </summary>
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// Maximum number of characters returned for formatted messages (when included).
    /// </summary>
    public int MaxMessageChars { get; set; } = 4000;

    /// <summary>
    /// Maximum directory traversal depth for EVTX discovery helper tools.
    /// Set to 0 to scan only the allowed root directory (no recursion).
    /// </summary>
    public int EvtxFindMaxDepth { get; set; } = 6;

    /// <summary>
    /// Maximum number of directories scanned for EVTX discovery helper tools.
    /// </summary>
    public int EvtxFindMaxDirsScanned { get; set; } = 5000;

    /// <summary>
    /// Maximum number of EVTX files enumerated for EVTX discovery helper tools.
    /// </summary>
    public int EvtxFindMaxFilesScanned { get; set; } = 8000;

    /// <inheritdoc />
    public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
        ArgumentNullException.ThrowIfNull(context);

        for (var i = 0; i < context.AllowedRoots.Count; i++) {
            var root = (context.AllowedRoots[i] ?? string.Empty).Trim();
            if (root.Length == 0 || ContainsOrdinalIgnoreCase(AllowedRoots, root)) {
                continue;
            }

            AllowedRoots.Add(root);
        }
    }

    /// <summary>
    /// Validates this options instance.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when option values are invalid.</exception>
    public void Validate() {
        if (MaxResults <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), "MaxResults must be positive.");
        }
        if (MaxMessageChars <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxMessageChars), "MaxMessageChars must be positive.");
        }
        if (EvtxFindMaxDepth < 0) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxDepth), "EvtxFindMaxDepth cannot be negative.");
        }
        if (EvtxFindMaxDepth > EvtxFindMaxDepthUpper) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxDepth), $"EvtxFindMaxDepth must be <= {EvtxFindMaxDepthUpper}.");
        }
        if (EvtxFindMaxDirsScanned <= 0) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxDirsScanned), "EvtxFindMaxDirsScanned must be positive.");
        }
        if (EvtxFindMaxDirsScanned > EvtxFindMaxDirsScannedUpper) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxDirsScanned), $"EvtxFindMaxDirsScanned must be <= {EvtxFindMaxDirsScannedUpper}.");
        }
        if (EvtxFindMaxFilesScanned <= 0) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxFilesScanned), "EvtxFindMaxFilesScanned must be positive.");
        }
        if (EvtxFindMaxFilesScanned > EvtxFindMaxFilesScannedUpper) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxFilesScanned), $"EvtxFindMaxFilesScanned must be <= {EvtxFindMaxFilesScannedUpper}.");
        }
    }

    private static bool ContainsOrdinalIgnoreCase(IReadOnlyList<string> values, string candidate) {
        for (var i = 0; i < values.Count; i++) {
            if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
