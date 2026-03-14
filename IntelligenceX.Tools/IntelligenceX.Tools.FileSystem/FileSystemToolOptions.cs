using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Safety and output limits for file system tools.
/// </summary>
public sealed class FileSystemToolOptions : IToolPackRuntimeConfigurable, IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "filesystem",
        "fs"
    };

    /// <summary>
    /// Allowed root directories for all operations. When empty, operations are denied.
    /// </summary>
    public List<string> AllowedRoots { get; } = new();

    /// <summary>
    /// Maximum bytes returned by read operations.
    /// </summary>
    public long MaxReadBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Maximum number of entries returned for listing/search.
    /// </summary>
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// Maximum file size that search will scan.
    /// </summary>
    public long MaxSearchFileBytes { get; set; } = 2 * 1024 * 1024;

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

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
    /// Validates options.
    /// </summary>
    public void Validate() {
        if (MaxReadBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxReadBytes), "MaxReadBytes must be positive.");
        }
        if (MaxResults <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), "MaxResults must be positive.");
        }
        if (MaxSearchFileBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxSearchFileBytes), "MaxSearchFileBytes must be positive.");
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
