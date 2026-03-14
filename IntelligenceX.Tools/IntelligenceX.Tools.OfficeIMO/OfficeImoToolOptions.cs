using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Options for OfficeIMO tool pack (safe-by-default).
/// </summary>
public sealed class OfficeImoToolOptions : IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "officeimo"
    };

    /// <summary>
    /// Allowed filesystem roots. Paths outside these roots are rejected.
    /// </summary>
    public List<string> AllowedRoots { get; } = new();

    /// <summary>
    /// Maximum files ingested when a folder is provided. Default: 500.
    /// </summary>
    public int MaxFiles { get; set; } = 500;

    /// <summary>
    /// Maximum total bytes ingested when a folder is provided. Default: 500 MB.
    /// </summary>
    public long MaxTotalBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>
    /// Maximum bytes per single file (best-effort). Default: 250 MB.
    /// </summary>
    public long MaxInputBytes { get; set; } = 250L * 1024 * 1024;

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

    /// <summary>
    /// Validates the configured options.
    /// </summary>
    public void Validate() {
        if (MaxFiles < 1) throw new ArgumentOutOfRangeException(nameof(MaxFiles));
        if (MaxTotalBytes < 1) throw new ArgumentOutOfRangeException(nameof(MaxTotalBytes));
        if (MaxInputBytes < 1) throw new ArgumentOutOfRangeException(nameof(MaxInputBytes));
    }
}
