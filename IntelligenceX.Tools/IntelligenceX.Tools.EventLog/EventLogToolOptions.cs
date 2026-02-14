using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Safety and output limits for event log tools.
/// </summary>
public sealed class EventLogToolOptions {
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
        if (EvtxFindMaxDepth <= 0) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxDepth), "EvtxFindMaxDepth must be positive.");
        }
        if (EvtxFindMaxDirsScanned <= 0) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxDirsScanned), "EvtxFindMaxDirsScanned must be positive.");
        }
        if (EvtxFindMaxFilesScanned <= 0) {
            throw new ArgumentOutOfRangeException(nameof(EvtxFindMaxFilesScanned), "EvtxFindMaxFilesScanned must be positive.");
        }
    }
}
