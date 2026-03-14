using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    private static IEnumerable<object[]> ActiveDirectorySchemaSnapshotsGpo() {
        yield return new object[] {
            "ad_gpo_list",
            new[] { "forest_name", "domain_name", "name_contains", "consistency", "link_state", "modified_since_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_gpo_changes",
            new[] { "domain_name", "since_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_health",
            new[] { "domain_name", "gpo_ids", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "gpo_ids" }
        };

        yield return new object[] {
            "ad_gpo_inventory_health",
            new[] { "domain_name", "slice", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_duplicates",
            new[] { "domain_name", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_blocked_inheritance",
            new[] { "domain_name", "only_blocked", "max_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_ou_link_summary",
            new[] { "domain_name", "link_count_at_least", "broken_only", "max_gpos", "max_ous", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_integrity",
            new[] { "domain_name", "sysvol_missing_only", "ad_missing_only", "errors_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_read",
            new[] { "domain_name", "include_compliant", "deny_only", "max_gpos", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_administrative",
            new[] { "domain_name", "include_compliant", "errors_only", "max_gpos", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_consistency",
            new[] { "domain_name", "verify_inheritance", "include_consistent", "top_level_inconsistent_only", "inside_inconsistent_only", "max_gpos", "sysvol_scan_cap", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_unknown",
            new[] { "domain_name", "resolution_error_contains", "inherited_only", "max_gpos", "max_findings", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_root",
            new[] { "domain_name", "permission", "deny_only", "inherited_only", "max_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_report",
            new[] { "domain_name", "gpo_id", "gpo_name", "principal_contains", "permission_type", "max_gpos", "max_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_redirect",
            new[] { "domain_name", "gpo_ids", "gpo_names", "actual_path_contains", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };
    }
}
