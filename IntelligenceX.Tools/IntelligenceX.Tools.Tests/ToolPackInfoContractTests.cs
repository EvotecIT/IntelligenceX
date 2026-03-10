using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.DnsClientX;
using IntelligenceX.Tools.DomainDetective;
using IntelligenceX.Tools.Email;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.OfficeIMO;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolPackInfoContractTests {
    [Fact]
    public async Task PackInfoTools_ShouldExposeRegisteredToolCatalogs() {
        var cases = BuildPackCases();
        var knownToolNames = CreateCaseInsensitiveSet(
            cases.SelectMany(static @case => @case.ExpectedTools));

        foreach (var @case in cases) {
            var json = await @case.Tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal(@case.Pack, root.GetProperty("pack").GetString());
            Assert.Equal(@case.Engine, root.GetProperty("engine").GetString());
            Assert.Equal(1, root.GetProperty("guidance_version").GetInt32());

            var outputContract = root.GetProperty("output_contract");
            Assert.Equal("_view", outputContract.GetProperty("view_field_suffix").GetString());
            var viewProjectionPolicy = outputContract.GetProperty("view_projection_policy").GetString() ?? string.Empty;
            Assert.Contains("view-only", viewProjectionPolicy, StringComparison.OrdinalIgnoreCase);
            var rawPayloadPolicy = outputContract.GetProperty("raw_payload_policy").GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(rawPayloadPolicy));

            var actualTools = ReadStringArray(root.GetProperty("tools"));
            var expectedTools = @case.ExpectedTools
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(expectedTools, actualTools);

            var toolCatalog = root.GetProperty("tool_catalog");
            Assert.True(toolCatalog.ValueKind == JsonValueKind.Array);
            Assert.Equal(actualTools.Length, toolCatalog.GetArrayLength());
            var catalogNames = ReadCatalogNames(toolCatalog);
            Assert.Equal(actualTools, catalogNames);

            var expectedCatalogByName = @case.ExpectedCatalog.ToDictionary(
                static x => x.Name,
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in toolCatalog.EnumerateArray()) {
                var name = entry.GetProperty("name").GetString() ?? string.Empty;
                var description = entry.GetProperty("description").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.False(string.IsNullOrWhiteSpace(description));
                Assert.True(entry.TryGetProperty("display_name", out var displayName));
                Assert.True(displayName.ValueKind == JsonValueKind.String || displayName.ValueKind == JsonValueKind.Null);
                Assert.True(entry.TryGetProperty("category", out var category));
                Assert.Equal(JsonValueKind.String, category.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(category.GetString()));
                Assert.True(entry.TryGetProperty("tags", out var tags));
                Assert.Equal(JsonValueKind.Array, tags.ValueKind);
                var serializedTags = ReadStringArrayPreserveOrder(tags);
                var sortedTags = serializedTags
                    .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Assert.True(
                    sortedTags.SequenceEqual(serializedTags, StringComparer.OrdinalIgnoreCase),
                    "Serialized tags should be emitted in deterministic ordinal-ignore-case order.");
                Assert.True(entry.TryGetProperty("routing", out var routing));
                Assert.Equal(JsonValueKind.Object, routing.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(routing.GetProperty("scope").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(routing.GetProperty("operation").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(routing.GetProperty("entity").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(routing.GetProperty("risk").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(routing.GetProperty("source").GetString()));
                Assert.Contains(
                    routing.GetProperty("risk").GetString() ?? string.Empty,
                    ToolRoutingTaxonomy.AllowedRisks,
                    StringComparer.Ordinal);
                Assert.Contains(
                    routing.GetProperty("source").GetString() ?? string.Empty,
                    ToolRoutingTaxonomy.AllowedSources,
                    StringComparer.Ordinal);
                Assert.True(entry.TryGetProperty("required_arguments", out var requiredArguments));
                Assert.Equal(JsonValueKind.Array, requiredArguments.ValueKind);
                Assert.True(entry.TryGetProperty("arguments", out var arguments));
                Assert.Equal(JsonValueKind.Array, arguments.ValueKind);
                Assert.True(entry.TryGetProperty("supports_table_view_projection", out var supportsProjection));
                Assert.True(supportsProjection.ValueKind == JsonValueKind.True || supportsProjection.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("is_pack_info_tool", out var isPackInfo));
                Assert.True(isPackInfo.ValueKind == JsonValueKind.True || isPackInfo.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("traits", out var traits));
                Assert.Equal(JsonValueKind.Object, traits.ValueKind);
                Assert.True(entry.TryGetProperty("is_write_capable", out var isWriteCapable));
                Assert.True(isWriteCapable.ValueKind == JsonValueKind.True || isWriteCapable.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("requires_write_governance", out var requiresWriteGovernance));
                Assert.True(requiresWriteGovernance.ValueKind == JsonValueKind.True || requiresWriteGovernance.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("write_governance_contract_id", out var writeGovernanceContractId));
                Assert.True(writeGovernanceContractId.ValueKind == JsonValueKind.String || writeGovernanceContractId.ValueKind == JsonValueKind.Null);
                Assert.True(entry.TryGetProperty("is_authentication_aware", out var isAuthenticationAware));
                Assert.True(isAuthenticationAware.ValueKind == JsonValueKind.True || isAuthenticationAware.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("requires_authentication", out var requiresAuthentication));
                Assert.True(requiresAuthentication.ValueKind == JsonValueKind.True || requiresAuthentication.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("authentication_contract_id", out var authenticationContractId));
                Assert.True(authenticationContractId.ValueKind == JsonValueKind.String || authenticationContractId.ValueKind == JsonValueKind.Null);
                Assert.True(entry.TryGetProperty("authentication_mode", out var authenticationMode));
                Assert.True(authenticationMode.ValueKind == JsonValueKind.String || authenticationMode.ValueKind == JsonValueKind.Null);
                Assert.True(entry.TryGetProperty("authentication_arguments", out var authenticationArguments));
                Assert.Equal(JsonValueKind.Array, authenticationArguments.ValueKind);
                Assert.True(entry.TryGetProperty("supports_connectivity_probe", out var supportsConnectivityProbe));
                Assert.True(supportsConnectivityProbe.ValueKind == JsonValueKind.True || supportsConnectivityProbe.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("probe_tool_name", out var probeToolName));
                Assert.True(probeToolName.ValueKind == JsonValueKind.String || probeToolName.ValueKind == JsonValueKind.Null);

                Assert.True(expectedCatalogByName.TryGetValue(name, out var expectedCatalogEntry), $"Unexpected catalog entry: {name}");
                Assert.Equal(expectedCatalogEntry.Description, description);
                Assert.Equal(expectedCatalogEntry.DisplayName, displayName.GetString());
                Assert.Equal(expectedCatalogEntry.Category, category.GetString());
                Assert.Equal(
                    expectedCatalogEntry.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadStringArray(tags));
                Assert.Contains(
                    category.GetString() ?? string.Empty,
                    ReadStringArray(tags),
                    StringComparer.OrdinalIgnoreCase);
                Assert.Equal(expectedCatalogEntry.Routing.Scope, routing.GetProperty("scope").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Operation, routing.GetProperty("operation").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Entity, routing.GetProperty("entity").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Risk, routing.GetProperty("risk").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Source, routing.GetProperty("source").GetString());
                Assert.Equal(
                    expectedCatalogEntry.RequiredArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadStringArray(requiredArguments));
                Assert.Equal(
                    expectedCatalogEntry.Arguments.Select(static x => x.Name).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadArgumentNames(arguments));
                AssertArgumentDetails(arguments, expectedCatalogEntry.Arguments);
                Assert.Equal(expectedCatalogEntry.SupportsTableViewProjection, supportsProjection.GetBoolean());
                Assert.Equal(expectedCatalogEntry.IsPackInfoTool, isPackInfo.GetBoolean());
                AssertTraitDetails(traits, expectedCatalogEntry.Traits);
                Assert.Equal(expectedCatalogEntry.IsWriteCapable, isWriteCapable.GetBoolean());
                Assert.Equal(expectedCatalogEntry.RequiresWriteGovernance, requiresWriteGovernance.GetBoolean());
                Assert.Equal(expectedCatalogEntry.WriteGovernanceContractId, writeGovernanceContractId.GetString());
                Assert.Equal(expectedCatalogEntry.IsAuthenticationAware, isAuthenticationAware.GetBoolean());
                Assert.Equal(expectedCatalogEntry.RequiresAuthentication, requiresAuthentication.GetBoolean());
                Assert.Equal(expectedCatalogEntry.AuthenticationContractId, authenticationContractId.GetString());
                Assert.Equal(expectedCatalogEntry.AuthenticationMode, authenticationMode.GetString());
                Assert.Equal(
                    expectedCatalogEntry.AuthenticationArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadStringArray(authenticationArguments));
                Assert.Equal(expectedCatalogEntry.SupportsConnectivityProbe, supportsConnectivityProbe.GetBoolean());
                Assert.Equal(expectedCatalogEntry.ProbeToolName, probeToolName.GetString());
            }

            var recommendedFlow = root.GetProperty("recommended_flow");
            Assert.True(recommendedFlow.ValueKind == JsonValueKind.Array);
            Assert.True(recommendedFlow.GetArrayLength() > 0);

            var flowSteps = root.GetProperty("recommended_flow_steps");
            Assert.True(flowSteps.ValueKind == JsonValueKind.Array);
            Assert.True(flowSteps.GetArrayLength() > 0);
            foreach (var step in flowSteps.EnumerateArray()) {
                var goal = step.GetProperty("goal").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(goal));
                var suggestedTools = ReadStringArray(step.GetProperty("suggested_tools"));
                Assert.True(suggestedTools.Length > 0);
            }

            var capabilities = root.GetProperty("capabilities");
            Assert.True(capabilities.ValueKind == JsonValueKind.Array);
            Assert.True(capabilities.GetArrayLength() > 0);
            foreach (var capability in capabilities.EnumerateArray()) {
                var capabilityId = capability.GetProperty("id").GetString() ?? string.Empty;
                var summary = capability.GetProperty("summary").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(capabilityId));
                Assert.False(string.IsNullOrWhiteSpace(summary));

                var primaryTools = ReadStringArray(capability.GetProperty("primary_tools"));
                Assert.True(primaryTools.Length > 0);
            }

            var entityHandoffs = root.GetProperty("entity_handoffs");
            Assert.Equal(JsonValueKind.Array, entityHandoffs.ValueKind);
            AssertHandoffToolReferences(
                @case.Pack,
                entityHandoffs,
                CreateCaseInsensitiveSet(catalogNames),
                knownToolNames);
            foreach (var handoff in entityHandoffs.EnumerateArray()) {
                var handoffId = handoff.GetProperty("id").GetString() ?? string.Empty;
                var summary = handoff.GetProperty("summary").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(handoffId));
                Assert.False(string.IsNullOrWhiteSpace(summary));

                var sourceTools = ReadStringArray(handoff.GetProperty("source_tools"));
                var targetTools = ReadStringArray(handoff.GetProperty("target_tools"));
                Assert.True(sourceTools.Length > 0);
                Assert.True(targetTools.Length > 0);

                var fieldMappings = handoff.GetProperty("field_mappings");
                Assert.Equal(JsonValueKind.Array, fieldMappings.ValueKind);
                foreach (var mapping in fieldMappings.EnumerateArray()) {
                    var sourceField = mapping.GetProperty("source_field").GetString() ?? string.Empty;
                    var targetArgument = mapping.GetProperty("target_argument").GetString() ?? string.Empty;
                    Assert.False(string.IsNullOrWhiteSpace(sourceField));
                    Assert.False(string.IsNullOrWhiteSpace(targetArgument));
                    Assert.True(mapping.TryGetProperty("normalization", out var normalizationNode));
                    Assert.True(normalizationNode.ValueKind == JsonValueKind.String || normalizationNode.ValueKind == JsonValueKind.Null);
                }
            }

            if (string.Equals(@case.Pack, "active_directory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(@case.Pack, "eventlog", StringComparison.OrdinalIgnoreCase)
                || string.Equals(@case.Pack, "domaindetective", StringComparison.OrdinalIgnoreCase)
                || string.Equals(@case.Pack, "system", StringComparison.OrdinalIgnoreCase)
                || string.Equals(@case.Pack, "testimox", StringComparison.OrdinalIgnoreCase)) {
                Assert.True(entityHandoffs.GetArrayLength() > 0);
            }
        }
    }

    [Fact]
    public async Task DomainDetectivePackInfo_ShouldExposeStructuredAdHandoffContract() {
        var tool = new DomainDetectivePackInfoTool(new DomainDetectiveToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("domaindetective", root.GetProperty("pack").GetString());

        var tools = ReadStringArray(root.GetProperty("tools"));
        Assert.Contains("domaindetective_checks_catalog", tools, StringComparer.OrdinalIgnoreCase);

        var entityHandoffs = root.GetProperty("entity_handoffs");
        Assert.Equal(JsonValueKind.Array, entityHandoffs.ValueKind);
        Assert.True(entityHandoffs.GetArrayLength() > 0);

        var handoff = entityHandoffs
            .EnumerateArray()
            .FirstOrDefault(node => string.Equals(node.GetProperty("id").GetString(), "domain_context_to_ad_scope", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, handoff.ValueKind);

        var sourceTools = ReadStringArray(handoff.GetProperty("source_tools"));
        Assert.Contains("domaindetective_domain_summary", sourceTools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domaindetective_network_probe", sourceTools, StringComparer.OrdinalIgnoreCase);

        var targetTools = ReadStringArray(handoff.GetProperty("target_tools"));
        Assert.Contains("ad_scope_discovery", targetTools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_directory_discovery_diagnostics", targetTools, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SystemPackInfo_ShouldExposeStructuredCrossPackHandoffs() {
        var tool = new SystemPackInfoTool(new SystemToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("system", root.GetProperty("pack").GetString());

        var entityHandoffs = root.GetProperty("entity_handoffs");
        Assert.Equal(JsonValueKind.Array, entityHandoffs.ValueKind);
        Assert.True(entityHandoffs.GetArrayLength() > 0);

        var hostScope = entityHandoffs
            .EnumerateArray()
            .FirstOrDefault(node => string.Equals(node.GetProperty("id").GetString(), "ad_or_eventlog_host_to_system_scope", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, hostScope.ValueKind);
        Assert.Contains("ad_scope_discovery", ReadStringArray(hostScope.GetProperty("source_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_info", ReadStringArray(hostScope.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_hardware_summary", ReadStringArray(hostScope.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_metrics_summary", ReadStringArray(hostScope.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_disks_list", ReadStringArray(hostScope.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_logical_disks_list", ReadStringArray(hostScope.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("computer_name", hostScope.GetProperty("field_mappings").ToString(), StringComparison.OrdinalIgnoreCase);

        var patchFollowUp = entityHandoffs
            .EnumerateArray()
            .FirstOrDefault(node => string.Equals(node.GetProperty("id").GetString(), "system_patch_findings_to_ad_eventlog_followup", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, patchFollowUp.ValueKind);
        Assert.Contains("system_patch_compliance", ReadStringArray(patchFollowUp.GetProperty("source_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_object_resolve", ReadStringArray(patchFollowUp.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestimoXPackInfo_ShouldExposeStructuredCrossPackHandoffs() {
        var tool = new TestimoXPackInfoTool(new TestimoXToolOptions { Enabled = true });
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("testimox", root.GetProperty("pack").GetString());

        var capabilities = root.GetProperty("capabilities");
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "run_catalog", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_runs_list", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "run_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_run_summary", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "baseline_catalog", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_baselines_list", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "baseline_compare", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_baseline_compare", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "source_provenance", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_source_query", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "profile_catalog", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "rule_inventory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "baseline_crosswalk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("testimox_analytics_diagnostics_get", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "analytics_diagnostics", StringComparison.OrdinalIgnoreCase));

        var entityHandoffs = root.GetProperty("entity_handoffs");
        Assert.Equal(JsonValueKind.Array, entityHandoffs.ValueKind);
        Assert.True(entityHandoffs.GetArrayLength() > 0);

        var scopeHandoff = entityHandoffs
            .EnumerateArray()
            .FirstOrDefault(node => string.Equals(node.GetProperty("id").GetString(), "ad_scope_to_testimox_execution_scope", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, scopeHandoff.ValueKind);
        Assert.Contains("ad_scope_discovery", ReadStringArray(scopeHandoff.GetProperty("source_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("testimox_rule_inventory", ReadStringArray(scopeHandoff.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("testimox_rules_run", ReadStringArray(scopeHandoff.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);

        var followUpHandoff = entityHandoffs
            .EnumerateArray()
            .FirstOrDefault(node => string.Equals(node.GetProperty("id").GetString(), "testimox_findings_to_ad_system_eventlog_followup", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, followUpHandoff.ValueKind);
        Assert.Contains("testimox_run_summary", ReadStringArray(followUpHandoff.GetProperty("source_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("testimox_rules_run", ReadStringArray(followUpHandoff.GetProperty("source_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_object_resolve", ReadStringArray(followUpHandoff.GetProperty("target_tools")), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestimoXAnalyticsPackInfo_ShouldExposeMonitoringArtifactCapabilities() {
        var tool = new TestimoXAnalyticsPackInfoTool(new TestimoXToolOptions { Enabled = true });
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("testimox_analytics", root.GetProperty("pack").GetString());

        var capabilities = root.GetProperty("capabilities");
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "analytics_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_analytics_diagnostics_get", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "probe_index_status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_probe_index_status", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "maintenance_window_history", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_maintenance_window_history", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "report_data_snapshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_report_data_snapshot_get", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "report_snapshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_report_snapshot_get", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "monitoring_history", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_history_query", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "report_job_history", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_report_job_history", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("testimox_rules_run", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("testimox_runs_list", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("testimox_baselines_list", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackInfoTools_ShouldExposeDeterministicCatalogTaxonomyAcrossPacks() {
        var cases = BuildPackCases();

        foreach (var @case in cases) {
            var json = await @case.Tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var toolCatalog = document.RootElement.GetProperty("tool_catalog");
            Assert.Equal(JsonValueKind.Array, toolCatalog.ValueKind);

            foreach (var entry in toolCatalog.EnumerateArray()) {
                var tags = ReadStringArrayPreserveOrder(entry.GetProperty("tags"));
                var sortedTags = tags
                    .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Assert.True(
                    sortedTags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase),
                    $"{@case.Pack}:{entry.GetProperty("name").GetString()} tags should be deterministic.");

                var routing = entry.GetProperty("routing");
                Assert.Contains(
                    routing.GetProperty("risk").GetString() ?? string.Empty,
                    ToolRoutingTaxonomy.AllowedRisks,
                    StringComparer.Ordinal);
                Assert.Contains(
                    routing.GetProperty("source").GetString() ?? string.Empty,
                    ToolRoutingTaxonomy.AllowedSources,
                    StringComparer.Ordinal);
            }
        }
    }

    [Fact]
    public async Task PackInfoTools_ShouldExposeRoutingContractFieldsAcrossCatalogEntries() {
        var cases = BuildPackCases();

        foreach (var @case in cases) {
            var json = await @case.Tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var toolCatalog = document.RootElement.GetProperty("tool_catalog");
            Assert.Equal(JsonValueKind.Array, toolCatalog.ValueKind);

            foreach (var entry in toolCatalog.EnumerateArray()) {
                Assert.True(entry.TryGetProperty("routing", out var routing));
                Assert.Equal(JsonValueKind.Object, routing.ValueKind);

                Assert.True(routing.TryGetProperty("scope", out var scope));
                Assert.Equal(JsonValueKind.String, scope.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(scope.GetString()));

                Assert.True(routing.TryGetProperty("operation", out var operation));
                Assert.Equal(JsonValueKind.String, operation.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(operation.GetString()));

                Assert.True(routing.TryGetProperty("entity", out var entity));
                Assert.Equal(JsonValueKind.String, entity.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(entity.GetString()));

                Assert.True(routing.TryGetProperty("risk", out var risk));
                Assert.Equal(JsonValueKind.String, risk.ValueKind);
                Assert.Contains(risk.GetString() ?? string.Empty, ToolRoutingTaxonomy.AllowedRisks, StringComparer.Ordinal);

                Assert.True(routing.TryGetProperty("source", out var source));
                Assert.Equal(JsonValueKind.String, source.ValueKind);
                Assert.Contains(source.GetString() ?? string.Empty, ToolRoutingTaxonomy.AllowedSources, StringComparer.Ordinal);
            }
        }
    }

    [Fact]
    public async Task AdPackInfo_ShouldAdvertiseAuthoritativeLastLogonCorrelationFlow() {
        var tool = new AdPackInfoTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var recommendedFlow = root.GetProperty("recommended_flow")
            .EnumerateArray()
            .Select(static x => x.GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(
            recommendedFlow,
            static step => step.Contains("authoritative last-logon", StringComparison.OrdinalIgnoreCase)
                && step.Contains("ad_ldap_query", StringComparison.OrdinalIgnoreCase));

        var flowSteps = root.GetProperty("recommended_flow_steps");
        var lastLogonStep = flowSteps
            .EnumerateArray()
            .FirstOrDefault(static step =>
                (step.GetProperty("goal").GetString() ?? string.Empty)
                    .Contains("authoritative user/computer logon recency", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(JsonValueKind.Object, lastLogonStep.ValueKind);
        var lastLogonTools = ReadStringArray(lastLogonStep.GetProperty("suggested_tools"));
        Assert.Contains("ad_scope_discovery", lastLogonTools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_query", lastLogonTools, StringComparer.OrdinalIgnoreCase);

        var capabilities = root.GetProperty("capabilities");
        var capability = capabilities
            .EnumerateArray()
            .FirstOrDefault(static entry =>
                string.Equals(
                    entry.GetProperty("id").GetString(),
                    "authoritative_logon_correlation",
                    StringComparison.OrdinalIgnoreCase));

        Assert.Equal(JsonValueKind.Object, capability.ValueKind);
        var primaryTools = ReadStringArray(capability.GetProperty("primary_tools"));
        Assert.Contains("ad_ldap_query", primaryTools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_query_paged", primaryTools, StringComparer.OrdinalIgnoreCase);
    }

    private static PackCase[] BuildPackCases() {
        var adOptions = new ActiveDirectoryToolOptions();
        var eventLogOptions = new EventLogToolOptions();
        var fileSystemOptions = new FileSystemToolOptions();
        var systemOptions = new SystemToolOptions();
        var emailOptions = new EmailToolOptions();
        var powerShellOptions = new PowerShellToolOptions { Enabled = true };
        var testimoXOptions = new TestimoXToolOptions { Enabled = true };
        var officeImoOptions = new OfficeImoToolOptions();
        var dnsClientXOptions = new DnsClientXToolOptions();
        var domainDetectiveOptions = new DomainDetectiveToolOptions();

        return new[] {
            new PackCase(
                Pack: "active_directory",
                Engine: "ADPlayground",
                Tool: new AdPackInfoTool(adOptions),
                ExpectedTools: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolNames(adOptions),
                ExpectedCatalog: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolCatalog(adOptions)),
            new PackCase(
                Pack: "eventlog",
                Engine: "EventViewerX",
                Tool: new EventLogPackInfoTool(eventLogOptions),
                ExpectedTools: ToolRegistryEventLogExtensions.GetRegisteredToolNames(eventLogOptions),
                ExpectedCatalog: ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(eventLogOptions)),
            new PackCase(
                Pack: "filesystem",
                Engine: "IntelligenceX.Engines.FileSystem",
                Tool: new FileSystemPackInfoTool(fileSystemOptions),
                ExpectedTools: ToolRegistryFileSystemExtensions.GetRegisteredToolNames(fileSystemOptions),
                ExpectedCatalog: ToolRegistryFileSystemExtensions.GetRegisteredToolCatalog(fileSystemOptions)),
            new PackCase(
                Pack: "system",
                Engine: "ComputerX",
                Tool: new SystemPackInfoTool(systemOptions),
                ExpectedTools: ToolRegistrySystemExtensions.GetRegisteredToolNames(systemOptions),
                ExpectedCatalog: ToolRegistrySystemExtensions.GetRegisteredToolCatalog(systemOptions)),
            new PackCase(
                Pack: "email",
                Engine: "Mailozaurr",
                Tool: new EmailPackInfoTool(emailOptions),
                ExpectedTools: ToolRegistryEmailExtensions.GetRegisteredToolNames(emailOptions),
                ExpectedCatalog: ToolRegistryEmailExtensions.GetRegisteredToolCatalog(emailOptions)),
            new PackCase(
                Pack: "powershell",
                Engine: "IntelligenceX.Engines.PowerShell",
                Tool: new PowerShellPackInfoTool(powerShellOptions),
                ExpectedTools: ToolRegistryPowerShellExtensions.GetRegisteredToolNames(powerShellOptions),
                ExpectedCatalog: ToolRegistryPowerShellExtensions.GetRegisteredToolCatalog(powerShellOptions)),
            new PackCase(
                Pack: "testimox",
                Engine: "TestimoX",
                Tool: new TestimoXPackInfoTool(testimoXOptions),
                ExpectedTools: ToolRegistryTestimoXExtensions.GetRegisteredToolNames(testimoXOptions),
                ExpectedCatalog: ToolRegistryTestimoXExtensions.GetRegisteredToolCatalog(testimoXOptions)),
            new PackCase(
                Pack: "testimox_analytics",
                Engine: "ADPlayground.Monitoring",
                Tool: new TestimoXAnalyticsPackInfoTool(testimoXOptions),
                ExpectedTools: ToolRegistryTestimoXAnalyticsExtensions.GetRegisteredToolNames(testimoXOptions),
                ExpectedCatalog: ToolRegistryTestimoXAnalyticsExtensions.GetRegisteredToolCatalog(testimoXOptions)),
            new PackCase(
                Pack: "officeimo",
                Engine: "OfficeIMO.Reader",
                Tool: new OfficeImoPackInfoTool(officeImoOptions),
                ExpectedTools: ToolRegistryOfficeImoExtensions.GetRegisteredToolNames(officeImoOptions),
                ExpectedCatalog: ToolRegistryOfficeImoExtensions.GetRegisteredToolCatalog(officeImoOptions)),
            new PackCase(
                Pack: "dnsclientx",
                Engine: "DnsClientX",
                Tool: new DnsClientXPackInfoTool(dnsClientXOptions),
                ExpectedTools: ToolRegistryDnsClientXExtensions.GetRegisteredToolNames(dnsClientXOptions),
                ExpectedCatalog: ToolRegistryDnsClientXExtensions.GetRegisteredToolCatalog(dnsClientXOptions)),
            new PackCase(
                Pack: "domaindetective",
                Engine: "DomainDetective",
                Tool: new DomainDetectivePackInfoTool(domainDetectiveOptions),
                ExpectedTools: ToolRegistryDomainDetectiveExtensions.GetRegisteredToolNames(domainDetectiveOptions),
                ExpectedCatalog: ToolRegistryDomainDetectiveExtensions.GetRegisteredToolCatalog(domainDetectiveOptions))
        };
    }

    private static string[] ReadStringArray(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadStringArrayPreserveOrder(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .ToArray();
    }

    private static string[] ReadCatalogNames(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetProperty("name").GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadArgumentNames(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetProperty("name").GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlySet<string> CreateCaseInsensitiveSet(params string[] values) {
        return CreateCaseInsensitiveSet(values.AsEnumerable());
    }

    private static IReadOnlySet<string> CreateCaseInsensitiveSet(IEnumerable<string>? values) {
        return new HashSet<string>(
            (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertHandoffToolReferences(
        string pack,
        JsonElement entityHandoffs,
        IReadOnlySet<string> localCatalogTools,
        IReadOnlySet<string> globalKnownTools) {
        var handoffTools = new[] { "source_tools", "target_tools" };

        foreach (var handoff in entityHandoffs.EnumerateArray()) {
            foreach (var handoffToolKey in handoffTools) {
                foreach (var toolNode in handoff.GetProperty(handoffToolKey).EnumerateArray()) {
                    Assert.Equal(
                        JsonValueKind.String,
                        toolNode.ValueKind);

                    var rawToolName = toolNode.GetString();
                    Assert.False(
                        string.IsNullOrWhiteSpace(rawToolName),
                        $"Pack '{pack}' handoff '{handoff.GetProperty("id").GetString()}' contains an empty {handoffToolKey} entry.");

                    var normalizedToolName = rawToolName!.Trim();
                    Assert.Equal(
                        normalizedToolName,
                        rawToolName);

                    var isLocalTool = localCatalogTools.Contains(normalizedToolName);
                    var isKnownCrossPackTool = globalKnownTools.Contains(normalizedToolName);
                    Assert.True(
                        isLocalTool || isKnownCrossPackTool,
                        $"Pack '{pack}' handoff '{handoff.GetProperty("id").GetString()}' references unknown {handoffToolKey} tool '{normalizedToolName}'. " +
                        "Tool references must resolve to the local pack catalog or a known registered tool across pack catalogs.");
                }
            }
        }
    }

    private static void AssertArgumentDetails(JsonElement actualArguments, IReadOnlyList<ToolPackToolArgumentModel> expectedArguments) {
        var expectedByName = expectedArguments.ToDictionary(
            static x => x.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var arg in actualArguments.EnumerateArray()) {
            var name = arg.GetProperty("name").GetString() ?? string.Empty;
            Assert.True(expectedByName.TryGetValue(name, out var expected), $"Unexpected argument: {name}");

            Assert.Equal(expected.Type, arg.GetProperty("type").GetString());
            Assert.Equal(expected.Required, arg.GetProperty("required").GetBoolean());
            var actualDescription = arg.TryGetProperty("description", out var descriptionNode)
                ? descriptionNode.GetString() ?? string.Empty
                : string.Empty;
            Assert.Equal(expected.Description ?? string.Empty, actualDescription);
            Assert.Equal(
                expected.EnumValues.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                ReadStringArray(arg.GetProperty("enum_values")));
        }
    }

    private static void AssertTraitDetails(JsonElement actualTraits, ToolPackToolTraitsModel expectedTraits) {
        Assert.Equal(expectedTraits.SupportsTableViewProjection, actualTraits.GetProperty("supports_table_view_projection").GetBoolean());
        Assert.Equal(expectedTraits.SupportsPaging, actualTraits.GetProperty("supports_paging").GetBoolean());
        Assert.Equal(expectedTraits.SupportsTimeRange, actualTraits.GetProperty("supports_time_range").GetBoolean());
        Assert.Equal(expectedTraits.SupportsDynamicAttributes, actualTraits.GetProperty("supports_dynamic_attributes").GetBoolean());
        Assert.Equal(expectedTraits.SupportsTargetScoping, actualTraits.GetProperty("supports_target_scoping").GetBoolean());
        Assert.Equal(expectedTraits.SupportsRemoteHostTargeting, actualTraits.GetProperty("supports_remote_host_targeting").GetBoolean());
        Assert.Equal(expectedTraits.SupportsMutatingActions, actualTraits.GetProperty("supports_mutating_actions").GetBoolean());
        Assert.Equal(expectedTraits.SupportsWriteGovernanceMetadata, actualTraits.GetProperty("supports_write_governance_metadata").GetBoolean());
        Assert.Equal(expectedTraits.SupportsAuthentication, actualTraits.GetProperty("supports_authentication").GetBoolean());

        Assert.Equal(
            expectedTraits.TableViewArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("table_view_arguments")));
        Assert.Equal(
            expectedTraits.PagingArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("paging_arguments")));
        Assert.Equal(
            expectedTraits.TimeRangeArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("time_range_arguments")));
        Assert.Equal(
            expectedTraits.DynamicAttributeArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("dynamic_attribute_arguments")));
        Assert.Equal(
            expectedTraits.TargetScopeArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("target_scope_arguments")));
        Assert.Equal(
            expectedTraits.RemoteHostArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("remote_host_arguments")));
        Assert.Equal(
            expectedTraits.MutatingActionArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("mutating_action_arguments")));
        Assert.Equal(
            expectedTraits.WriteGovernanceMetadataArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("write_governance_metadata_arguments")));
        Assert.Equal(
            expectedTraits.AuthenticationArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("authentication_arguments")));
    }

    private sealed record PackCase(
        string Pack,
        string Engine,
        ITool Tool,
        IReadOnlyList<string> ExpectedTools,
        IReadOnlyList<ToolPackToolCatalogEntryModel> ExpectedCatalog);
}
