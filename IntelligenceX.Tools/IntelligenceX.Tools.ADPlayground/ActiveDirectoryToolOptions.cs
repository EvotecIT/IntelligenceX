using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Safety and connection options for Active Directory tools.
/// </summary>
public sealed class ActiveDirectoryToolOptions : IToolPackRuntimeConfigurable, IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "active_directory",
        "ad",
        "adplayground"
    };

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

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

    /// <inheritdoc />
    public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
        ArgumentNullException.ThrowIfNull(context);

        DomainController = string.IsNullOrWhiteSpace(context.AdDomainController)
            ? DomainController
            : context.AdDomainController.Trim();
        DefaultSearchBaseDn = string.IsNullOrWhiteSpace(context.AdDefaultSearchBaseDn)
            ? DefaultSearchBaseDn
            : context.AdDefaultSearchBaseDn.Trim();
        if (context.AdMaxResults > 0) {
            MaxResults = context.AdMaxResults;
        }

        for (var i = 0; i < context.AllowedRoots.Count; i++) {
            var root = (context.AllowedRoots[i] ?? string.Empty).Trim();
            if (root.Length == 0 || ContainsOrdinalIgnoreCase(AllowedMonitoringRoots, root)) {
                continue;
            }

            AllowedMonitoringRoots.Add(root);
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
