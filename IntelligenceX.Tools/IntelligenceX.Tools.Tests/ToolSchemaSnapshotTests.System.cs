using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    private static IEnumerable<object[]> SystemSchemaSnapshots() {
        yield return new object[] {
            "system_connectivity_probe",
            new[] { "computer_name", "timeout_ms", "include_time_sync" },
            Array.Empty<string>()
        };

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

        yield return new object[] {
            "system_hardware_summary",
            new[] { "computer_name", "include_processors", "include_memory_modules", "include_video_controllers", "name_sample_size", "timeout_ms" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_metrics_summary",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_local_identity_inventory",
            new[] { "computer_name", "include_group_members", "only_privileged_groups", "privileged_group_names", "max_entries", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_privacy_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_exploit_protection",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_office_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_browser_posture",
            new[] { "computer_name", "include_extensions", "max_extensions" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_backup_posture",
            new[] { "computer_name", "include_shadow_copies", "max_shadow_copies", "include_restore_points", "max_restore_points" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_tls_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_winrm_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_powershell_logging_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_uac_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_ldap_policy_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_network_client_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_account_policy_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_interactive_logon_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_audit_options",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_builtin_accounts",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_remote_access_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_device_guard_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_defender_asr_posture",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_windows_update_client_status",
            new[] { "computer_name", "include_event_telemetry", "event_lookback_days", "query_timeout_seconds" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_windows_update_telemetry",
            new[] { "computer_name", "include_event_telemetry", "event_lookback_days", "query_timeout_seconds", "detect_stale_warning_after_hours", "detect_stale_down_after_hours" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_certificate_posture",
            new[] { "computer_name", "recent_window_days", "include_certificates", "max_certificates_per_store", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_credential_posture",
            new[] { "computer_name", "include_stored_credentials", "max_stored_credentials" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_info",
            new[] { "computer_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_process_list",
            new[] { "computer_name", "name_contains", "max_processes", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_network_adapters",
            new[] { "computer_name", "name_contains", "max_adapters", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_ports_list",
            new[] { "computer_name", "protocol", "local_port", "state", "process_name_contains", "max_entries", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_service_list",
            new[] { "computer_name", "engine", "name_contains", "status", "max_services", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_service_lifecycle",
            new[] {
                "computer_name",
                "service_name",
                "operation",
                "startup_type",
                "timeout_ms",
                "apply",
                "write_operation_id",
                "write_execution_id",
                "write_actor_id",
                "write_change_reason",
                "write_rollback_plan_id",
                "write_rollback_provider_id",
                "write_audit_correlation_id"
            },
            new[] { "service_name", "operation" }
        };

        yield return new object[] {
            "system_scheduled_tasks_list",
            new[] { "computer_name", "name_contains", "max_tasks", "suspicious", "only_suspicious", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_scheduled_task_lifecycle",
            new[] {
                "computer_name",
                "task_path",
                "operation",
                "apply",
                "write_operation_id",
                "write_execution_id",
                "write_actor_id",
                "write_change_reason",
                "write_rollback_plan_id",
                "write_rollback_provider_id",
                "write_audit_correlation_id"
            },
            new[] { "task_path", "operation" }
        };

        yield return new object[] {
            "system_devices_summary",
            new[] { "computer_name", "include_usb", "include_device_manager", "name_contains", "class_contains", "manufacturer_contains", "status_contains", "problem_only", "max_entries", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_features_list",
            new[] { "computer_name", "source", "name_contains", "optional_state", "max_entries", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_disks_list",
            new[] { "computer_name", "model_contains", "interface_contains", "media_contains", "min_size_bytes", "max_entries", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "system_logical_disks_list",
            new[] { "computer_name", "name_contains", "file_system", "drive_type", "min_size_bytes", "min_free_bytes", "max_entries", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };
    }
}
