using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    private static IEnumerable<object[]> EventLogAndPowerShellSchemaSnapshots() {
        yield return new object[] {
            "eventlog_evtx_find",
            new[] { "query", "log_name", "max_results" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "eventlog_evtx_security_summary",
            new[] { "path", "report_kind", "start_time_utc", "end_time_utc", "max_events_scanned", "top_per_dimension", "include_samples", "sample_size", "columns", "sort_by", "sort_direction", "top" },
            new[] { "path", "report_kind" }
        };

        yield return new object[] {
            "eventlog_evtx_query",
            new[] { "path", "event_ids", "provider_name", "start_time_utc", "end_time_utc", "level", "keywords", "user_id", "event_record_ids", "named_data_filter", "named_data_exclude_filter", "max_events", "oldest_first", "include_message", "columns", "sort_by", "sort_direction", "top" },
            new[] { "path" }
        };

        yield return new object[] {
            "eventlog_live_query",
            new[] { "log_name", "machine_name", "xpath", "event_ids", "provider_name", "start_time_utc", "end_time_utc", "level", "keywords", "user_id", "event_record_ids", "named_data_filter", "named_data_exclude_filter", "max_events", "oldest_first", "include_message", "session_timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            new[] { "log_name" }
        };

        yield return new object[] {
            "eventlog_named_events_catalog",
            new[] { "name_contains", "categories", "available_only", "include_event_ids", "max_event_ids_per_row", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "eventlog_named_events_query",
            new[] { "named_events", "categories", "machine_name", "machine_names", "time_period", "start_time_utc", "end_time_utc", "log_name", "event_ids", "max_events_per_named_event", "max_events", "max_threads", "include_payload", "payload_keys", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "eventlog_timeline_query",
            new[] { "named_events", "categories", "machine_name", "machine_names", "time_period", "start_time_utc", "end_time_utc", "log_name", "event_ids", "max_events_per_named_event", "max_events", "max_threads", "correlation_profile", "correlation_keys", "include_uncorrelated", "max_groups", "bucket_minutes", "include_payload", "payload_keys", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "eventlog_timeline_explain",
            new[] { "investigation_goal", "correlation_keys_present", "timeline_count", "groups_count", "filtered_uncorrelated", "prefer_profile", "include_ad_enrichment", "include_payload" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "eventlog_top_events",
            new[] { "log_name", "machine_name", "max_events", "include_message", "session_timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            new[] { "log_name" }
        };

        yield return new object[] {
            "powershell_pack_info",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "powershell_environment_discover",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "powershell_hosts",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "powershell_run",
            new[] { "host", "intent", "allow_write", "command", "script", "working_directory", "timeout_ms", "max_output_chars", "include_error_stream", "write_execution_id", "write_actor_id", "write_change_reason", "write_rollback_plan_id", "write_rollback_provider_id", "write_audit_correlation_id" },
            Array.Empty<string>()
        };
    }
}
