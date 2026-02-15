using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ActiveDirectory;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.PowerShell;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolSchemaSnapshotTests {
    [Theory]
    [MemberData(nameof(SchemaSnapshots))]
    public void SelectedToolSchemas_ShouldMatchSnapshot(string toolName, string[] expectedProperties, string[] expectedRequired) {
        var definition = GetDefinition(toolName);
        Assert.NotNull(definition.Parameters);
        var schema = definition.Parameters!;

        Assert.Equal("object", schema.GetString("type"));
        Assert.False(schema.GetBoolean("additionalProperties", defaultValue: true));

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);

        var actualProperties = GetObjectKeys(properties!)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var expectedSortedProperties = expectedProperties
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSortedProperties, actualProperties);

        foreach (var name in expectedProperties) {
            Assert.NotNull(properties!.GetObject(name));
        }

        if (expectedRequired.Length == 0) {
            var requiredOptional = schema.GetArray("required");
            if (requiredOptional is not null) {
                Assert.Empty(ReadArrayStrings(requiredOptional));
            }
            return;
        }

        var required = schema.GetArray("required");
        Assert.NotNull(required);

        var actualRequired = ReadArrayStrings(required!)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var expectedSortedRequired = expectedRequired
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSortedRequired, actualRequired);
    }

    [Fact]
    public void AllRegisteredAdToolSchemas_ShouldBeCoveredBySnapshots() {
        var snapshotNames = new HashSet<string>(
            SchemaSnapshots()
                .Select(static row => row[0] as string)
                .Where(static name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("ad_", StringComparison.OrdinalIgnoreCase))
                .Select(static name => name!),
            StringComparer.OrdinalIgnoreCase);

        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var actualNames = registry.GetDefinitions()
            .Select(static d => d.Name)
            .Where(static n => n.StartsWith("ad_", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            snapshotNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            actualNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> SchemaSnapshots() {
        yield return new object[] {
            "ad_pack_info",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_delegation_audit",
            new[] { "kind", "enabled_only", "include_spns", "include_allowed_to_delegate_to", "max_values_per_attribute", "search_base_dn", "domain_controller", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_admins_summary",
            new[] { "domain_name", "domain_controller", "search_base_dn", "include_members", "include_nested", "max_results", "users_only", "computers_only" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_controllers",
            new[] { "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_info",
            new[] { "domain_controller" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_environment_discover",
            new[] { "domain_controller", "search_base_dn", "include_domain_controllers", "max_domain_controllers" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_group_members",
            new[] { "identity", "search_base_dn", "domain_controller", "max_members" },
            new[] { "identity" }
        };

        yield return new object[] {
            "ad_group_members_resolved",
            new[] { "identity", "search_base_dn", "domain_controller", "include_nested", "max_results", "attributes", "max_values_per_attribute", "columns", "sort_by", "sort_direction", "top" },
            new[] { "identity" }
        };

        yield return new object[] {
            "ad_groups_list",
            new[] { "name_contains", "name_prefix", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "max_results", "page_size", "offset", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_ldap_diagnostics",
            new[] { "servers", "domain_controller", "max_servers", "include_global_catalog", "verify_certificate", "identity", "certificate_include_dns_names", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_monitoring_probe_catalog",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_monitoring_probe_run",
            new[] { "probe_kind", "name", "targets", "domain_controller", "domain_name", "forest_name", "include_domains", "exclude_domains", "include_domain_controllers", "exclude_domain_controllers", "skip_rodc", "include_trusts", "discovery_fallback", "timeout_ms", "retries", "retry_delay_ms", "max_concurrency", "protocol", "split_protocol_results", "dns_queries", "verify_certificate", "include_global_catalog", "include_facts", "identity", "stale_threshold_hours", "include_sysvol", "test_sysvol_shares", "test_ports", "test_ping", "query_mode", "include_children", "columns", "sort_by", "sort_direction", "top" },
            new[] { "probe_kind" }
        };

        yield return new object[] {
            "ad_ldap_query",
            new[] { "ldap_filter", "scope", "search_base_dn", "domain_controller", "attributes", "allow_sensitive_attributes", "max_attributes", "max_values_per_attribute", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "ldap_filter" }
        };

        yield return new object[] {
            "ad_ldap_query_paged",
            new[] { "ldap_filter", "scope", "search_base_dn", "domain_controller", "attributes", "allow_sensitive_attributes", "max_attributes", "max_values_per_attribute", "page_size", "max_pages", "max_results", "cursor", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            new[] { "ldap_filter" }
        };

        yield return new object[] {
            "ad_object_resolve",
            new[] { "identities", "identity_kind", "kind", "search_base_dn", "domain_controller", "attributes", "max_inputs", "max_values_per_attribute", "columns", "sort_by", "sort_direction", "top" },
            new[] { "identities" }
        };

        yield return new object[] {
            "ad_object_get",
            new[] { "identity", "kind", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute" },
            new[] { "identity" }
        };

        yield return new object[] {
            "ad_privileged_groups_summary",
            new[] { "domain_name", "domain_controller", "search_base_dn", "include_member_count", "include_member_sample", "member_sample_size" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_replication_summary",
            new[] { "domain_controller", "domain_name", "outbound", "by_source", "stale_threshold_hours", "bucket_hours", "include_details", "max_details", "max_domain_controllers", "max_errors", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_search",
            new[] { "query", "kind", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "query" }
        };

        yield return new object[] {
            "ad_search_facets",
            new[] { "ldap_filter", "kind", "search_text", "scope", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "page_size", "max_pages", "max_results", "max_facet_values", "facet_by_container", "container_facet_mode", "container_ou_depth", "facet_by_enabled", "facet_uac_flags", "facet_pwd_age_buckets_days", "include_samples", "sample_size", "timeout_ms" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_spn_search",
            new[] { "spn_contains", "spn_exact", "kind", "enabled_only", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_stale_accounts",
            new[] { "kind", "enabled_only", "exclude_critical", "days_since_logon", "days_since_password_set", "match", "search_base_dn", "domain_controller", "max_results" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_spn_stats",
            new[] { "spn_contains", "spn_exact", "kind", "enabled_only", "search_base_dn", "domain_controller", "max_results", "max_service_classes", "max_hosts", "include_examples", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
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

        yield return new object[] {
            "eventlog_evtx_report_user_logons",
            new[] { "path", "start_time_utc", "end_time_utc", "max_events_scanned", "top", "include_samples", "sample_size", "columns", "sort_by", "sort_direction" },
            new[] { "path" }
        };

        yield return new object[] {
            "eventlog_evtx_report_failed_logons",
            new[] { "path", "start_time_utc", "end_time_utc", "max_events_scanned", "top", "include_samples", "sample_size", "columns", "sort_by", "sort_direction" },
            new[] { "path" }
        };

        yield return new object[] {
            "eventlog_evtx_report_account_lockouts",
            new[] { "path", "start_time_utc", "end_time_utc", "max_events_scanned", "top", "include_samples", "sample_size", "columns", "sort_by", "sort_direction" },
            new[] { "path" }
        };

        yield return new object[] {
            "eventlog_evtx_find",
            new[] { "query", "log_name", "max_results" },
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
            new[] { "host", "intent", "allow_write", "command", "script", "working_directory", "timeout_ms", "max_output_chars", "include_error_stream" },
            Array.Empty<string>()
        };
    }

    private static ToolDefinition GetDefinition(string toolName) {
        var registry = new ToolRegistry();
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });

        return registry.GetDefinitions()
            .Single(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadArrayStrings(JsonArray array) {
        var list = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++) {
            var value = array[i].AsString();
            if (!string.IsNullOrWhiteSpace(value)) {
                list.Add(value.Trim());
            }
        }
        return list;
    }

    private static IReadOnlyList<string> GetObjectKeys(JsonObject obj) {
        var keysProperty = obj.GetType().GetProperty("Keys");
        if (keysProperty?.GetValue(obj) is IEnumerable<string> keys) {
            return keys
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .ToArray();
        }

        if (obj is global::System.Collections.IEnumerable enumerable) {
            var list = new List<string>();
            foreach (var item in enumerable) {
                if (item is null) {
                    continue;
                }
                var keyProperty = item.GetType().GetProperty("Key");
                var key = keyProperty?.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(key)) {
                    list.Add(key.Trim());
                }
            }
            if (list.Count > 0) {
                return list;
            }

            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }
}
