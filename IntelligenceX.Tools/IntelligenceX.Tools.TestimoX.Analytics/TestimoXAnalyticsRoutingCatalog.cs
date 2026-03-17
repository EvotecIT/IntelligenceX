using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsRoutingCatalog {
    public const string DomainIntentFamily = "monitoring_artifacts";
    public const string DomainIntentActionId = "act_domain_scope_monitoring_artifacts";

    private static readonly string[] DiagnosticsFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] DiagnosticsFallbackHintKeys = { "history_directory", "include_slow_probes", "max_slow_probes" };
    private static readonly string[] DashboardStatusFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] DashboardStatusFallbackHintKeys = { "history_directory" };
    private static readonly string[] RollupStatusFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] RollupStatusFallbackHintKeys = { "history_directory" };
    private static readonly string[] ReportSnapshotFallbackSelectionKeys = { "history_directory", "report_key" };
    private static readonly string[] ReportSnapshotFallbackHintKeys = { "history_directory", "report_key", "include_html", "max_chars" };
    private static readonly string[] MaintenanceWindowHistoryFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] MaintenanceWindowHistoryFallbackHintKeys = { "history_directory", "start_utc", "end_utc", "definition_key", "name_contains", "reason_contains", "probe_name_pattern_contains", "target_pattern_contains" };
    private static readonly string[] HistoryQueryFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] HistoryQueryFallbackHintKeys = { "history_directory", "bucket_kind", "start_utc", "end_utc", "root_probe_names", "probe_name_contains" };
    private static readonly string[] ReportJobHistoryFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] ReportJobHistoryFallbackHintKeys = { "history_directory", "job_key", "report_key", "since_utc", "statuses" };
    private static readonly string[] ProbeIndexStatusFallbackSelectionKeys = { "history_directory" };
    private static readonly string[] ProbeIndexStatusFallbackHintKeys = { "history_directory", "probe_names", "since_utc", "probe_name_contains", "statuses" };
    private static readonly string[] ReportDataSnapshotFallbackSelectionKeys = { "history_directory", "report_key" };
    private static readonly string[] ReportDataSnapshotFallbackHintKeys = { "history_directory", "report_key", "include_payload", "max_chars" };

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_analytics_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["testimox_report_job_history"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_history_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_probe_index_status"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_analytics_diagnostics_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_dashboard_autogenerate_status_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_availability_rollup_status_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_maintenance_window_history"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_report_data_snapshot_get"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_report_snapshot_get"] = ToolRoutingTaxonomy.RoleResolver
        };

    public static readonly string[] SetupHintKeys = {
        "history_directory",
        "report_key",
        "job_key",
        "probe_names",
        "since_utc"
    };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "monitoring",
        "history",
        "report",
        "snapshot",
        "maintenance",
        "availability",
        DomainIntentFamily,
        DomainIntentActionId
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "TestimoX Analytics");
    }

    public static string ResolveDomainIntentFamily(string? explicitFamily) {
        return string.IsNullOrWhiteSpace(explicitFamily) ? DomainIntentFamily : explicitFamily!;
    }

    public static string ResolveDomainIntentActionId(string? explicitActionId) {
        return string.IsNullOrWhiteSpace(explicitActionId) ? DomainIntentActionId : explicitActionId!;
    }

    public static IReadOnlyList<string> ResolveFallbackSelectionKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return toolName switch {
            "testimox_analytics_diagnostics_get" => DiagnosticsFallbackSelectionKeys,
            "testimox_dashboard_autogenerate_status_get" => DashboardStatusFallbackSelectionKeys,
            "testimox_availability_rollup_status_get" => RollupStatusFallbackSelectionKeys,
            "testimox_report_snapshot_get" => ReportSnapshotFallbackSelectionKeys,
            "testimox_maintenance_window_history" => MaintenanceWindowHistoryFallbackSelectionKeys,
            "testimox_history_query" => HistoryQueryFallbackSelectionKeys,
            "testimox_report_job_history" => ReportJobHistoryFallbackSelectionKeys,
            "testimox_probe_index_status" => ProbeIndexStatusFallbackSelectionKeys,
            "testimox_report_data_snapshot_get" => ReportDataSnapshotFallbackSelectionKeys,
            _ => Array.Empty<string>()
        };
    }

    public static IReadOnlyList<string> ResolveFallbackHintKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return toolName switch {
            "testimox_analytics_diagnostics_get" => DiagnosticsFallbackHintKeys,
            "testimox_dashboard_autogenerate_status_get" => DashboardStatusFallbackHintKeys,
            "testimox_availability_rollup_status_get" => RollupStatusFallbackHintKeys,
            "testimox_report_snapshot_get" => ReportSnapshotFallbackHintKeys,
            "testimox_maintenance_window_history" => MaintenanceWindowHistoryFallbackHintKeys,
            "testimox_history_query" => HistoryQueryFallbackHintKeys,
            "testimox_report_job_history" => ReportJobHistoryFallbackHintKeys,
            "testimox_probe_index_status" => ProbeIndexStatusFallbackHintKeys,
            "testimox_report_data_snapshot_get" => ReportDataSnapshotFallbackHintKeys,
            _ => Array.Empty<string>()
        };
    }

    public static bool RequiresSelectionForFallback(bool explicitRequiresSelection, IReadOnlyList<string> fallbackSelectionKeys) {
        return explicitRequiresSelection || fallbackSelectionKeys.Count > 0;
    }
}
