using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Safety and output limits for system tools.
/// </summary>
public sealed class SystemToolOptions : IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "system",
        "computerx"
    };

    /// <summary>
    /// Maximum number of items returned by list operations.
    /// </summary>
    public int MaxResults { get; set; } = 200;

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

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
