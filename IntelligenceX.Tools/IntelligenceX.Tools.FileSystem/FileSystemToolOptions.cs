using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Safety and output limits for file system tools.
/// </summary>
public sealed class FileSystemToolOptions {
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
}

