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

            var autonomySummary = root.GetProperty("autonomy_summary");
            Assert.Equal(JsonValueKind.Object, autonomySummary.ValueKind);
            Assert.Equal(@case.ExpectedCatalog.Count, autonomySummary.GetProperty("total_tools").GetInt32());
            Assert.Equal(
                CountExpectedLocalCapableTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("local_capable_tools").GetInt32());
            Assert.Equal(
                CountExpectedRemoteCapableTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("remote_capable_tools").GetInt32());
            Assert.Equal(
                CountExpectedTargetScopedTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("target_scoped_tools").GetInt32());
            Assert.Equal(
                CountExpectedRemoteHostTargetingTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("remote_host_targeting_tools").GetInt32());
            Assert.Equal(
                CountExpectedSetupAwareTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("setup_aware_tools").GetInt32());
            Assert.Equal(
                CountExpectedEnvironmentDiscoverTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("environment_discover_tools").GetInt32());
            Assert.Equal(
                CountExpectedHandoffAwareTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("handoff_aware_tools").GetInt32());
            Assert.Equal(
                CountExpectedRecoveryAwareTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("recovery_aware_tools").GetInt32());
            Assert.Equal(
                CountExpectedWriteCapableTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("write_capable_tools").GetInt32());
            Assert.Equal(
                CountExpectedGovernedWriteTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("governed_write_tools").GetInt32());
            Assert.Equal(
                CountExpectedAuthenticationRequiredTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("authentication_required_tools").GetInt32());
            Assert.Equal(
                CountExpectedProbeCapableTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("probe_capable_tools").GetInt32());
            Assert.Equal(
                CountExpectedCrossPackHandoffTools(@case.ExpectedCatalog),
                autonomySummary.GetProperty("cross_pack_handoff_tools").GetInt32());
            Assert.Equal(
                ReadExpectedLocalCapableToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("local_capable_tool_names")));
            Assert.Equal(
                ReadExpectedRemoteCapableToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("remote_capable_tool_names")));
            Assert.Equal(
                ReadExpectedGovernedWriteToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("governed_write_tool_names")));
            Assert.Equal(
                ReadExpectedTargetScopedToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("target_scoped_tool_names")));
            Assert.Equal(
                ReadExpectedRemoteHostTargetingToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("remote_host_targeting_tool_names")));
            Assert.Equal(
                ReadExpectedSetupAwareToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("setup_aware_tool_names")));
            Assert.Equal(
                ReadExpectedEnvironmentDiscoverToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("environment_discover_tool_names")));
            Assert.Equal(
                ReadExpectedHandoffAwareToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("handoff_aware_tool_names")));
            Assert.Equal(
                ReadExpectedRecoveryAwareToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("recovery_aware_tool_names")));
            Assert.Equal(
                ReadExpectedWriteCapableToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("write_capable_tool_names")));
            Assert.Equal(
                ReadExpectedAuthenticationRequiredToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("authentication_required_tool_names")));
            Assert.Equal(
                ReadExpectedProbeCapableToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("probe_capable_tool_names")));
            Assert.Equal(
                ReadExpectedCrossPackHandoffToolNames(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("cross_pack_handoff_tool_names")));
            Assert.Equal(
                ReadExpectedCrossPackTargetPacks(@case.ExpectedCatalog),
                ReadStringArray(autonomySummary.GetProperty("cross_pack_target_packs")));

            var runtimeCapabilities = root.GetProperty("runtime_capabilities");
            Assert.Equal(JsonValueKind.Object, runtimeCapabilities.ValueKind);
            Assert.Equal(
                CountExpectedLocalCapableTools(@case.ExpectedCatalog) > 0,
                runtimeCapabilities.GetProperty("supports_local_execution").GetBoolean());
            Assert.Equal(
                CountExpectedRemoteCapableTools(@case.ExpectedCatalog) > 0,
                runtimeCapabilities.GetProperty("supports_remote_execution").GetBoolean());
            Assert.Equal(
                CountExpectedTargetScopedTools(@case.ExpectedCatalog) > 0,
                runtimeCapabilities.GetProperty("supports_target_scoping").GetBoolean());
            Assert.Equal(
                CountExpectedRemoteHostTargetingTools(@case.ExpectedCatalog) > 0,
                runtimeCapabilities.GetProperty("supports_remote_host_targeting").GetBoolean());
            Assert.Equal(
                CountExpectedEnvironmentDiscoverTools(@case.ExpectedCatalog) > 0,
                runtimeCapabilities.GetProperty("supports_environment_discovery").GetBoolean());
            Assert.Equal(
                CountExpectedGovernedWriteTools(@case.ExpectedCatalog) > 0,
                runtimeCapabilities.GetProperty("supports_governed_writes").GetBoolean());
            Assert.Equal(
                CountExpectedAuthenticationRequiredTools(@case.ExpectedCatalog) > 0
                || @case.ExpectedCatalog.Any(static entry =>
                    entry.IsAuthenticationAware
                    || entry.Traits.SupportsAuthentication
                    || entry.AuthenticationArguments.Count > 0),
                runtimeCapabilities.GetProperty("supports_authentication").GetBoolean());
            Assert.Equal(
                CountExpectedProbeCapableTools(@case.ExpectedCatalog) > 0
                || @case.ExpectedCatalog.Any(static entry => !string.IsNullOrWhiteSpace(entry.ProbeToolName)),
                runtimeCapabilities.GetProperty("supports_connectivity_probe").GetBoolean());
            Assert.Equal(
                ReadExpectedTargetScopeArguments(@case.ExpectedCatalog),
                ReadStringArray(runtimeCapabilities.GetProperty("target_scope_arguments")));
            Assert.Equal(
                ReadExpectedRemoteHostArguments(@case.ExpectedCatalog),
                ReadStringArray(runtimeCapabilities.GetProperty("remote_host_arguments")));

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
                Assert.True(entry.TryGetProperty("is_environment_discover_tool", out var isEnvironmentDiscover));
                Assert.True(isEnvironmentDiscover.ValueKind == JsonValueKind.True || isEnvironmentDiscover.ValueKind == JsonValueKind.False);
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
                Assert.Equal(expectedCatalogEntry.Routing.PackId, routing.GetProperty("pack_id").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Role, routing.GetProperty("role").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Scope, routing.GetProperty("scope").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Operation, routing.GetProperty("operation").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Entity, routing.GetProperty("entity").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Risk, routing.GetProperty("risk").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.Source, routing.GetProperty("source").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.DomainIntentFamily, routing.GetProperty("domain_intent_family").GetString());
                Assert.Equal(expectedCatalogEntry.Routing.DomainIntentActionId, routing.GetProperty("domain_intent_action_id").GetString());
                Assert.Equal(
                    expectedCatalogEntry.RequiredArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadStringArray(requiredArguments));
                Assert.Equal(
                    expectedCatalogEntry.Arguments.Select(static x => x.Name).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadArgumentNames(arguments));
                AssertArgumentDetails(arguments, expectedCatalogEntry.Arguments);
                Assert.Equal(expectedCatalogEntry.SupportsTableViewProjection, supportsProjection.GetBoolean());
                Assert.Equal(expectedCatalogEntry.IsPackInfoTool, isPackInfo.GetBoolean());
                Assert.Equal(expectedCatalogEntry.IsEnvironmentDiscoverTool, isEnvironmentDiscover.GetBoolean());
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

            var recommendedRecipes = root.GetProperty("recommended_recipes");
            Assert.Equal(JsonValueKind.Array, recommendedRecipes.ValueKind);
            foreach (var recipe in recommendedRecipes.EnumerateArray()) {
                var recipeId = recipe.GetProperty("id").GetString() ?? string.Empty;
                var summary = recipe.GetProperty("summary").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(recipeId));
                Assert.False(string.IsNullOrWhiteSpace(summary));

                if (recipe.TryGetProperty("when_to_use", out var whenToUse) && whenToUse.ValueKind != JsonValueKind.Null) {
                    Assert.False(string.IsNullOrWhiteSpace(whenToUse.GetString()));
                }

                var recipeSteps = recipe.GetProperty("steps");
                Assert.Equal(JsonValueKind.Array, recipeSteps.ValueKind);
                Assert.True(recipeSteps.GetArrayLength() > 0);
                foreach (var step in recipeSteps.EnumerateArray()) {
                    var goal = step.GetProperty("goal").GetString() ?? string.Empty;
                    Assert.False(string.IsNullOrWhiteSpace(goal));
                    var suggestedTools = ReadStringArray(step.GetProperty("suggested_tools"));
                    Assert.True(suggestedTools.Length > 0);
                }

                var verificationTools = ReadStringArray(recipe.GetProperty("verification_tools"));
                Assert.True(verificationTools.Length > 0);
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
    public async Task RuntimeGuidancePacks_ShouldExposeRuntimeCapabilityGuidance() {
        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new AdPackInfoTool(new ActiveDirectoryToolOptions()),
            expectedEntryTools: new[] { "ad_environment_discover", "ad_scope_discovery", "ad_forest_discover" },
            expectedProbeTools: new[] { "ad_connectivity_probe", "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" },
            expectedPrerequisiteSnippet: "AllowedMonitoringRoots",
            expectedProbeFreshnessWindowSeconds: 600,
            expectedSetupFreshnessWindowSeconds: 1800,
            expectedRecipeFreshnessWindowSeconds: 900);

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new AdLifecyclePackInfoTool(new ActiveDirectoryToolOptions()),
            expectedEntryTools: new[] { "ad_environment_discover", "ad_user_lifecycle", "ad_group_lifecycle", "ad_ou_lifecycle" },
            expectedProbeTools: Array.Empty<string>(),
            expectedPrerequisiteSnippet: "apply=false");

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new EventLogPackInfoTool(new EventLogToolOptions()),
            expectedEntryTools: new[] { "eventlog_channels_list", "eventlog_top_events", "eventlog_live_query" },
            expectedProbeTools: new[] { "eventlog_connectivity_probe" },
            expectedPrerequisiteSnippet: "AllowedRoots",
            expectedProbeFreshnessWindowSeconds: 300,
            expectedSetupFreshnessWindowSeconds: 900,
            expectedRecipeFreshnessWindowSeconds: 300);

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new SystemPackInfoTool(new SystemToolOptions()),
            expectedEntryTools: new[] { "system_info", "system_hardware_summary", "system_metrics_summary" },
            expectedProbeTools: new[] { "system_connectivity_probe" },
            expectedPrerequisiteSnippet: "computer_name",
            expectedProbeFreshnessWindowSeconds: 600,
            expectedSetupFreshnessWindowSeconds: 1800,
            expectedRecipeFreshnessWindowSeconds: 900);

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new DnsClientXPackInfoTool(new DnsClientXToolOptions()),
            expectedEntryTools: new[] { "dnsclientx_query" },
            expectedProbeTools: new[] { "dnsclientx_ping" },
            expectedPrerequisiteSnippet: "endpoint",
            expectedProbeFreshnessWindowSeconds: 300,
            expectedSetupFreshnessWindowSeconds: 900,
            expectedRecipeFreshnessWindowSeconds: 300);

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new DomainDetectivePackInfoTool(new DomainDetectiveToolOptions()),
            expectedEntryTools: new[] { "domaindetective_checks_catalog", "domaindetective_domain_summary" },
            expectedProbeTools: new[] { "domaindetective_network_probe" },
            expectedPrerequisiteSnippet: "checks[]",
            expectedProbeFreshnessWindowSeconds: 300,
            expectedSetupFreshnessWindowSeconds: 900,
            expectedRecipeFreshnessWindowSeconds: 600);

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new TestimoXPackInfoTool(new TestimoXToolOptions()),
            expectedEntryTools: new[] { "testimox_profiles_list", "testimox_rule_inventory", "testimox_rules_list" },
            expectedProbeTools: Array.Empty<string>(),
            expectedPrerequisiteSnippet: "include_domains",
            expectedProbeFreshnessWindowSeconds: 900,
            expectedSetupFreshnessWindowSeconds: 1800,
            expectedRecipeFreshnessWindowSeconds: 900);

        await AssertRuntimeCapabilityGuidanceAsync(
            tool: new TestimoXAnalyticsPackInfoTool(new TestimoXToolOptions()),
            expectedEntryTools: new[] { "testimox_analytics_diagnostics_get", "testimox_dashboard_autogenerate_status_get", "testimox_history_query" },
            expectedProbeTools: new[] { "testimox_probe_index_status", "testimox_availability_rollup_status_get" },
            expectedPrerequisiteSnippet: "AllowedHistoryRoots",
            expectedProbeFreshnessWindowSeconds: 300,
            expectedSetupFreshnessWindowSeconds: 900,
            expectedRecipeFreshnessWindowSeconds: 600);
    }

    [Fact]
    public async Task ADLifecyclePackInfo_ShouldExposeGovernedLifecycleRecipes() {
        var json = await new AdLifecyclePackInfoTool(new ActiveDirectoryToolOptions())
            .InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var recipes = document.RootElement.GetProperty("recommended_recipes");

        Assert.Equal(
            new[] {
                "joiner_onboarding",
                "leaver_offboarding",
                "mover_access_transition",
                "quarantine_ou_preparation"
            },
            recipes
                .EnumerateArray()
                .Select(static node => node.GetProperty("id").GetString())
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase));

        var joiner = recipes
            .EnumerateArray()
            .First(node => string.Equals(node.GetProperty("id").GetString(), "joiner_onboarding", StringComparison.OrdinalIgnoreCase));
        var joinerSteps = joiner.GetProperty("steps").EnumerateArray().ToArray();
        Assert.True(joinerSteps.Length >= 3);
        Assert.Contains("ad_user_lifecycle", ReadStringArray(joinerSteps[1].GetProperty("suggested_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_object_get", ReadStringArray(joiner.GetProperty("verification_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_object_resolve", ReadStringArray(joiner.GetProperty("verification_tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_user_groups_resolved", ReadStringArray(joiner.GetProperty("verification_tools")), StringComparer.OrdinalIgnoreCase);

        var quarantine = recipes
            .EnumerateArray()
            .First(node => string.Equals(node.GetProperty("id").GetString(), "quarantine_ou_preparation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ad_ou_lifecycle", quarantine.ToString(), StringComparison.OrdinalIgnoreCase);

        var handoffs = document.RootElement.GetProperty("entity_handoffs");
        Assert.Contains(
            handoffs.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "computer_lifecycle_to_host_followup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handoffs.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "computer_lifecycle_to_eventlog_followup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handoffs.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "group_lifecycle_to_membership_verification", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handoffs.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "lifecycle_to_readonly_verification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ADSystemAndEventLogPackInfo_ShouldExposeOperationalRecipes() {
        await AssertRecipeIdsAsync(
            tool: new AdPackInfoTool(new ActiveDirectoryToolOptions()),
            expectedRecipeIds: new[] {
                "authoritative_last_logon_investigation",
                "dc_runtime_health_followup",
                "forest_scope_bootstrap"
            });

        await AssertRecipeIdsAsync(
            tool: new EventLogPackInfoTool(new EventLogToolOptions()),
            expectedRecipeIds: new[] {
                "event_host_followup",
                "live_authentication_triage",
                "offline_evtx_timeline"
            });

        await AssertRecipeIdsAsync(
            tool: new SystemPackInfoTool(new SystemToolOptions()),
            expectedRecipeIds: new[] {
                "host_security_posture_review",
                "patch_exposure_review",
                "remote_host_runtime_triage"
            });
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
            static id => string.Equals(id, "dashboard_autogenerate_status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_dashboard_autogenerate_status_get", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            capabilities.EnumerateArray().Select(static node => node.GetProperty("id").GetString()),
            static id => string.Equals(id, "availability_rollup_status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("testimox_availability_rollup_status_get", ReadStringArray(root.GetProperty("tools")), StringComparer.OrdinalIgnoreCase);
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

    [Fact]
    public async Task EventLogPackInfo_ShouldExposeStructuredSetupHandoffAndRecoveryCatalogContracts() {
        var tool = new EventLogPackInfoTool(new EventLogToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var toolCatalog = root.GetProperty("tool_catalog");
        var timelineEntry = toolCatalog
            .EnumerateArray()
            .First(static node => string.Equals(node.GetProperty("name").GetString(), "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase));

        var traits = timelineEntry.GetProperty("traits");
        Assert.True(traits.GetProperty("is_execution_aware").GetBoolean());
        Assert.Equal(ToolExecutionContract.DefaultContractId, traits.GetProperty("execution_contract_id").GetString());
        Assert.Equal("local_or_remote", traits.GetProperty("execution_scope").GetString());
        Assert.True(traits.GetProperty("supports_local_execution").GetBoolean());
        Assert.True(traits.GetProperty("supports_remote_execution").GetBoolean());
        Assert.Contains("machine_name", ReadStringArray(traits.GetProperty("remote_host_arguments")), StringComparer.OrdinalIgnoreCase);

        var setup = timelineEntry.GetProperty("setup");
        Assert.True(setup.GetProperty("is_setup_aware").GetBoolean());
        Assert.Equal("eventlog_connectivity_probe", setup.GetProperty("setup_tool_name").GetString());

        var handoff = timelineEntry.GetProperty("handoff");
        Assert.True(handoff.GetProperty("is_handoff_aware").GetBoolean());
        var routes = handoff.GetProperty("routes").EnumerateArray().ToArray();
        Assert.Contains(routes, static route =>
            string.Equals(route.GetProperty("target_pack_id").GetString(), "system", StringComparison.OrdinalIgnoreCase)
            && string.Equals(route.GetProperty("target_tool_name").GetString(), "system_info", StringComparison.OrdinalIgnoreCase));

        var recovery = timelineEntry.GetProperty("recovery");
        Assert.True(recovery.GetProperty("is_recovery_aware").GetBoolean());
        Assert.Contains("eventlog_connectivity_probe", ReadStringArray(recovery.GetProperty("recovery_tool_names")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog_channels_list", ReadStringArray(recovery.GetProperty("recovery_tool_names")), StringComparer.OrdinalIgnoreCase);

        var autonomySummary = root.GetProperty("autonomy_summary");
        Assert.Contains("eventlog_timeline_query", ReadStringArray(autonomySummary.GetProperty("remote_capable_tool_names")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog_timeline_query", ReadStringArray(autonomySummary.GetProperty("setup_aware_tool_names")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog_timeline_query", ReadStringArray(autonomySummary.GetProperty("cross_pack_handoff_tool_names")), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", ReadStringArray(autonomySummary.GetProperty("cross_pack_target_packs")), StringComparer.OrdinalIgnoreCase);
    }

    private static PackCase[] BuildPackCases() {
        var adOptions = new ActiveDirectoryToolOptions();
        var adLifecycleOptions = new ActiveDirectoryToolOptions();
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
                Pack: "active_directory_lifecycle",
                Engine: "ADPlayground",
                Tool: new AdLifecyclePackInfoTool(adLifecycleOptions),
                ExpectedTools: ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolNames(adLifecycleOptions),
                ExpectedCatalog: ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolCatalog(adLifecycleOptions)),
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
        Assert.Equal(expectedTraits.IsExecutionAware, TryGetBoolean(actualTraits, "is_execution_aware"));
        Assert.Equal(expectedTraits.ExecutionContractId, TryGetString(actualTraits, "execution_contract_id"));
        Assert.Equal(expectedTraits.ExecutionScope, actualTraits.GetProperty("execution_scope").GetString());
        Assert.Equal(expectedTraits.SupportsLocalExecution, TryGetBoolean(actualTraits, "supports_local_execution", fallback: true));
        Assert.Equal(expectedTraits.SupportsRemoteExecution, TryGetBoolean(actualTraits, "supports_remote_execution"));
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

    private static int CountExpectedRemoteCapableTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry =>
            entry.Traits.SupportsRemoteHostTargeting
            || entry.Traits.RemoteHostArguments.Count > 0
            || ToolExecutionScopes.IsRemoteCapable(entry.Traits.ExecutionScope));
    }

    private static int CountExpectedLocalCapableTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry =>
            entry.Traits.SupportsLocalExecution
            || !string.Equals(entry.Traits.ExecutionScope, "remote_only", StringComparison.OrdinalIgnoreCase));
    }

    private static int CountExpectedTargetScopedTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry =>
            entry.Traits.SupportsTargetScoping
            || entry.Traits.TargetScopeArguments.Count > 0);
    }

    private static int CountExpectedRemoteHostTargetingTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry =>
            entry.Traits.SupportsRemoteHostTargeting
            || entry.Traits.RemoteHostArguments.Count > 0);
    }

    private static int CountExpectedSetupAwareTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.Setup.IsSetupAware);
    }

    private static int CountExpectedEnvironmentDiscoverTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.IsEnvironmentDiscoverTool);
    }

    private static int CountExpectedHandoffAwareTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.Handoff.IsHandoffAware);
    }

    private static int CountExpectedRecoveryAwareTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.Recovery.IsRecoveryAware);
    }

    private static int CountExpectedWriteCapableTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.IsWriteCapable);
    }

    private static int CountExpectedGovernedWriteTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry =>
            entry.RequiresWriteGovernance
            || !string.IsNullOrWhiteSpace(entry.WriteGovernanceContractId));
    }

    private static int CountExpectedAuthenticationRequiredTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.RequiresAuthentication);
    }

    private static int CountExpectedProbeCapableTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry => entry.SupportsConnectivityProbe);
    }

    private static int CountExpectedCrossPackHandoffTools(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog.Count(static entry =>
            entry.Handoff.Routes.Any(static route => !string.IsNullOrWhiteSpace(route.TargetPackId)));
    }

    private static string[] ReadExpectedTargetScopeArguments(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .SelectMany(static entry => entry.Traits.TargetScopeArguments)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedRemoteHostArguments(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .SelectMany(static entry => entry.Traits.RemoteHostArguments)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedRemoteCapableToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry =>
                entry.Traits.SupportsRemoteHostTargeting
                || entry.Traits.RemoteHostArguments.Count > 0
                || ToolExecutionScopes.IsRemoteCapable(entry.Traits.ExecutionScope))
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedLocalCapableToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry =>
                entry.Traits.SupportsLocalExecution
                || !string.Equals(entry.Traits.ExecutionScope, "remote_only", StringComparison.OrdinalIgnoreCase))
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedGovernedWriteToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry =>
                entry.RequiresWriteGovernance
                || !string.IsNullOrWhiteSpace(entry.WriteGovernanceContractId))
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedTargetScopedToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.Traits.SupportsTargetScoping || entry.Traits.TargetScopeArguments.Count > 0)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedRemoteHostTargetingToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.Traits.SupportsRemoteHostTargeting || entry.Traits.RemoteHostArguments.Count > 0)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, bool fallback = false) {
        return element.TryGetProperty(propertyName, out var property) ? property.GetBoolean() : fallback;
    }

    private static string? TryGetString(JsonElement element, string propertyName) {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }

    private static string[] ReadExpectedSetupAwareToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.Setup.IsSetupAware)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedEnvironmentDiscoverToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.IsEnvironmentDiscoverTool)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedHandoffAwareToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.Handoff.IsHandoffAware)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedRecoveryAwareToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.Recovery.IsRecoveryAware)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedWriteCapableToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.IsWriteCapable)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedAuthenticationRequiredToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.RequiresAuthentication)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedProbeCapableToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.SupportsConnectivityProbe)
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedCrossPackHandoffToolNames(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .Where(static entry => entry.Handoff.Routes.Any(static route => !string.IsNullOrWhiteSpace(route.TargetPackId)))
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadExpectedCrossPackTargetPacks(IReadOnlyList<ToolPackToolCatalogEntryModel> catalog) {
        return catalog
            .SelectMany(static entry => entry.Handoff.Routes)
            .Select(static route => route.TargetPackId)
            .Where(static packId => !string.IsNullOrWhiteSpace(packId))
            .Select(static packId => packId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task AssertRuntimeCapabilityGuidanceAsync(
        ITool tool,
        string[] expectedEntryTools,
        string[] expectedProbeTools,
        string expectedPrerequisiteSnippet,
        int? expectedProbeFreshnessWindowSeconds = null,
        int? expectedSetupFreshnessWindowSeconds = null,
        int? expectedRecipeFreshnessWindowSeconds = null) {
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var runtimeCapabilities = document.RootElement.GetProperty("runtime_capabilities");

        Assert.Equal(expectedEntryTools, ReadStringArrayPreserveOrder(runtimeCapabilities.GetProperty("preferred_entry_tools")));
        Assert.Equal(expectedProbeTools, ReadStringArrayPreserveOrder(runtimeCapabilities.GetProperty("preferred_probe_tools")));

        var prerequisites = ReadStringArrayPreserveOrder(runtimeCapabilities.GetProperty("runtime_prerequisites"));
        Assert.NotEmpty(prerequisites);
        Assert.Contains(
            prerequisites,
            item => item.Contains(expectedPrerequisiteSnippet, StringComparison.OrdinalIgnoreCase));

        AssertOptionalIntProperty(
            runtimeCapabilities,
            "probe_helper_freshness_window_seconds",
            expectedProbeFreshnessWindowSeconds);
        AssertOptionalIntProperty(
            runtimeCapabilities,
            "setup_helper_freshness_window_seconds",
            expectedSetupFreshnessWindowSeconds);
        AssertOptionalIntProperty(
            runtimeCapabilities,
            "recipe_helper_freshness_window_seconds",
            expectedRecipeFreshnessWindowSeconds);

        var notes = runtimeCapabilities.GetProperty("notes").GetString();
        Assert.False(string.IsNullOrWhiteSpace(notes));
    }

    private static void AssertOptionalIntProperty(JsonElement parent, string propertyName, int? expectedValue) {
        if (expectedValue.HasValue) {
            Assert.True(parent.TryGetProperty(propertyName, out var property));
            Assert.Equal(JsonValueKind.Number, property.ValueKind);
            Assert.Equal(expectedValue.Value, property.GetInt32());
            return;
        }

        if (!parent.TryGetProperty(propertyName, out var optionalProperty)) {
            return;
        }

        Assert.Equal(JsonValueKind.Null, optionalProperty.ValueKind);
    }

    private static async Task AssertRecipeIdsAsync(ITool tool, string[] expectedRecipeIds) {
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var recipes = document.RootElement.GetProperty("recommended_recipes");

        Assert.Equal(
            expectedRecipeIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase),
            recipes
                .EnumerateArray()
                .Select(static node => node.GetProperty("id").GetString())
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase));
    }

    private sealed record PackCase(
        string Pack,
        string Engine,
        ITool Tool,
        IReadOnlyList<string> ExpectedTools,
        IReadOnlyList<ToolPackToolCatalogEntryModel> ExpectedCatalog);
}
