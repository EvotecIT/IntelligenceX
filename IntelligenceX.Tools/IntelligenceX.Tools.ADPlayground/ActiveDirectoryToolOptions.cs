using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Safety and connection options for Active Directory tools.
/// </summary>
public sealed class ActiveDirectoryToolOptions : IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] { "active_directory", "adplayground" };

    /// <summary>
    /// Optional domain controller hostname (if not specified, the underlying implementation may use defaults).
    /// </summary>
    public string? DomainController { get; set; }

    /// <summary>
    /// Optional default search base DN.
    /// </summary>
    public string? DefaultSearchBaseDn { get; set; }

    /// <summary>
    /// Maximum results returned by query tools.
    /// </summary>
    public int MaxResults { get; set; } = 1000;

    /// <summary>
    /// Allowed roots for persisted monitoring snapshot inspection.
    /// </summary>
    public List<string> AllowedMonitoringRoots { get; } = new();

    /// <summary>
    /// Validates this options instance.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when option values are invalid.</exception>
    public void Validate() {
        if (MaxResults <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), "MaxResults must be positive.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;
}
