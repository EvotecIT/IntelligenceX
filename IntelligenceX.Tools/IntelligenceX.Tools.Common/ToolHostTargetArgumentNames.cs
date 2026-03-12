using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared host-target argument aliases used across pack guidance, Chat routing, and handoff helpers.
/// </summary>
public static class ToolHostTargetArgumentNames {
    private static readonly string[] OrderedInputArgumentPriority = {
        "machine_name",
        "machine_names",
        "computer_name",
        "computer_names",
        "domain_controller",
        "domain_controllers",
        "host",
        "hostname",
        "host_name",
        "dns_host_name",
        "dnshostname",
        "server",
        "server_name",
        "target",
        "targets",
        "servers"
    };

    private static readonly HashSet<string> AllKnownFieldNames = new(OrderedInputArgumentPriority, StringComparer.OrdinalIgnoreCase) {
        "domainControllers"
    };

    /// <summary>
    /// Ordered input argument aliases for host-like targeting.
    /// </summary>
    public static IReadOnlyList<string> OrderedInputArguments => OrderedInputArgumentPriority;

    /// <summary>
    /// Returns whether a value is a known host-target argument or field alias.
    /// </summary>
    public static bool IsKnownArgumentOrField(string? value) {
        var normalized = Normalize(value);
        return normalized.Length > 0 && AllKnownFieldNames.Contains(normalized);
    }

    /// <summary>
    /// Returns whether a value is a multi-host array argument.
    /// </summary>
    public static bool IsArrayArgument(string? value) {
        var normalized = Normalize(value);
        return string.Equals(normalized, "machine_names", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "computer_names", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "domain_controllers", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "targets", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "servers", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) {
        return (value ?? string.Empty).Trim();
    }
}
