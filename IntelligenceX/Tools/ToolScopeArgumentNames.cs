using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared schema argument names used to infer scope, targeting, and planner-facing traits.
/// </summary>
public static class ToolScopeArgumentNames {
    private static readonly string[] HostTargetInputArgumentPriority = {
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

    private static readonly string[] TargetScopeArgumentPriority = {
        "domain_name",
        "forest_name",
        "search_base_dn",
        "path",
        "folder",
        "file_path",
        "evtx_path",
        "source_path",
        "channel",
        "provider_name"
    };

    private static readonly string[] FileScopeArgumentPriority = {
        "path",
        "folder",
        "file_path",
        "evtx_path",
        "source_path"
    };
    private static readonly string[] DomainScopeArgumentPriority = {
        "domain_controller",
        "domain_controllers",
        "search_base_dn",
        "domain_name",
        "forest_name"
    };

    /// <summary>
    /// Ordered schema arguments that target a specific host or server.
    /// </summary>
    public static IReadOnlyList<string> HostTargetInputArguments => HostTargetInputArgumentPriority;

    /// <summary>
    /// Ordered schema arguments that scope a tool to a specific domain, file, or event-log source.
    /// </summary>
    public static IReadOnlyList<string> TargetScopeArguments => TargetScopeArgumentPriority;

    /// <summary>
    /// Ordered schema arguments that scope a tool to Active Directory forest/domain context.
    /// </summary>
    public static IReadOnlyList<string> DomainScopeArguments => DomainScopeArgumentPriority;

    /// <summary>
    /// Ordered schema arguments that scope a tool to host-specific context.
    /// </summary>
    public static IReadOnlyList<string> HostScopeArguments => HostTargetInputArgumentPriority;

    /// <summary>
    /// Ordered schema arguments that scope a tool to file-system-backed inputs.
    /// </summary>
    public static IReadOnlyList<string> FileScopeArguments => FileScopeArgumentPriority;
}
