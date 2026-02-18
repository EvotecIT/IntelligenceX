using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    private static IEnumerable<object[]> SystemSchemaSnapshots() {
        yield return new object[] {
            "system_installed_applications",
            new[] { "computer_name", "name_contains", "publisher_contains", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_updates_installed",
            new[] { "computer_name", "include_pending_local", "title_contains", "kb_contains", "installed_after_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_bitlocker_status",
            new[] { "computer_name", "protected_only", "encrypted_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_time_sync",
            new[] { "computer_name", "reference_time_utc" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_patch_details",
            new[] { "year", "month", "product_family", "product_version", "product_build", "product_edition", "product_name_contains", "severity", "exploited_only", "publicly_disclosed_only", "cve_contains", "kb_contains", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_patch_compliance",
            new[] { "computer_name", "year", "month", "product_family", "product_version", "product_build", "product_edition", "severity", "exploited_only", "publicly_disclosed_only", "missing_only", "include_pending_local", "cve_contains", "kb_contains", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_rdp_posture",
            new[] { "computer_name", "include_policy" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_smb_posture",
            new[] { "computer_name", "include_netbios_interfaces" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_boot_configuration",
            new[] { "computer_name", "include_reboot_pending" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_bios_summary",
            new[] { "computer_name", "include_baseboard", "timeout_ms" },
            Array.Empty<string>()
        };
    }
}
