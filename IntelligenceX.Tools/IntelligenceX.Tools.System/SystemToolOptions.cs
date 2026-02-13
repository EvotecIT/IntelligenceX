using System;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Safety and output limits for system tools.
/// </summary>
public sealed class SystemToolOptions {
    /// <summary>
    /// Maximum number of items returned by list operations.
    /// </summary>
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// Validates this options instance.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when option values are invalid.</exception>
    public void Validate() {
        if (MaxResults <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), "MaxResults must be positive.");
        }
    }
}

