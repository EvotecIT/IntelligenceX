using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    private static IEnumerable<object[]> ActiveDirectorySchemaSnapshotsTail() {
        yield return new object[] {
            "ad_wmi_filters",
            new[] { "domain_name", "display_name_contains", "author_contains", "query_contains", "include_queries", "max_queries_per_filter", "max_query_chars", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_wsus_configuration",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "include_diagnostics", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_spn_stats",
            new[] { "spn_contains", "spn_exact", "kind", "enabled_only", "search_base_dn", "domain_controller", "max_results", "max_service_classes", "max_hosts", "include_examples", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_spn_hygiene",
            new[] { "domain_name", "forest_name", "allowlist_classes", "blocklist_classes", "dns_resolve_classes", "top_n", "include_invalid_spn_sample", "max_invalid_spn_sample", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_smartcard_posture",
            new[] { "domain_name", "forest_name", "include_details", "max_privileged_rows_per_domain", "max_finding_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_system_state_backup",
            new[] { "domain_name", "forest_name", "threshold_days", "missing_only", "stale_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_monitoring_service_heartbeat_get",
            new[] { "monitoring_directory", "columns", "sort_by", "sort_direction", "top" },
            new[] { "monitoring_directory" }
        };

        yield return new object[] {
            "ad_monitoring_diagnostics_get",
            new[] { "monitoring_directory", "include_slow_probes", "max_slow_probes", "columns", "sort_by", "sort_direction", "top" },
            new[] { "monitoring_directory" }
        };

        yield return new object[] {
            "ad_monitoring_metrics_get",
            new[] { "monitoring_directory", "columns", "sort_by", "sort_direction", "top" },
            new[] { "monitoring_directory" }
        };

        yield return new object[] {
            "ad_monitoring_dashboard_state_get",
            new[] { "monitoring_directory", "columns", "sort_by", "sort_direction", "top" },
            new[] { "monitoring_directory" }
        };

        yield return new object[] {
            "ad_users_expired",
            new[] { "domain_controller", "search_base_dn", "reference_time_utc", "max_results" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_whoami",
            Array.Empty<string>(),
            Array.Empty<string>()
        };
    }
}
