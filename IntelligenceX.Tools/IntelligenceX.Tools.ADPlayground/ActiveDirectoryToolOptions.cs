using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Safety and connection options for Active Directory tools.
/// </summary>
public sealed class ActiveDirectoryToolOptions {
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
}
