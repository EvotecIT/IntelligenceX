using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolPackCatalogParityTests {
    private static readonly string[] RepresentativeToolNames = {
        "ad_pack_info",
        "ad_environment_discover",
        "ad_scope_discovery",
        "ad_monitoring_probe_run",
        "system_info",
        "system_metrics_summary",
        "system_logical_disks_list",
        "system_time_sync",
        "eventlog_channels_list",
        "eventlog_live_query",
        "eventlog_timeline_query"
    };

    private static readonly string[] KnownTargetScopeArgumentNames = {
        "search_base_dn",
        "path",
        "folder",
        "channel",
        "provider_name"
    };

    private static readonly string[] KnownRemoteHostArgumentNames = {
        "computer_name",
        "computer_names",
        "machine_name",
        "machine_names",
        "domain_controller",
        "targets",
        "target"
    };

    [Fact]
    public void RepresentativePackCatalogEntries_ShouldStayAlignedWithRuntimeDefinitions() {
        var adOptions = new ActiveDirectoryToolOptions();
        var systemOptions = new SystemToolOptions();
        var eventLogOptions = new EventLogToolOptions();

        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(adOptions);
        registry.RegisterSystemPack(systemOptions);
        registry.RegisterEventLogPack(eventLogOptions);

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);
        var catalogByName = ToolRegistryActiveDirectoryExtensions.GetRegisteredToolCatalog(adOptions)
            .Concat(ToolRegistrySystemExtensions.GetRegisteredToolCatalog(systemOptions))
            .Concat(ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(eventLogOptions))
            .ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in RepresentativeToolNames) {
            var definition = Assert.IsType<ToolDefinition>(definitionsByName[toolName]);
            var entry = Assert.IsType<ToolPackToolCatalogEntryModel>(catalogByName[toolName]);

            AssertCatalogEntryMatchesDefinition(entry, definition);
        }
    }

    private static void AssertCatalogEntryMatchesDefinition(ToolPackToolCatalogEntryModel entry, ToolDefinition definition) {
        var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
        Assert.Equal(routing.PackId, entry.Routing.PackId);
        Assert.Equal(routing.Role, entry.Routing.Role);
        Assert.Equal(routing.DomainIntentFamily ?? string.Empty, entry.Routing.DomainIntentFamily);
        Assert.Equal(routing.DomainIntentActionId ?? string.Empty, entry.Routing.DomainIntentActionId);
        Assert.Equal(
            string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase),
            entry.IsPackInfoTool);
        Assert.Equal(
            string.Equals(routing.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase),
            entry.IsEnvironmentDiscoverTool);

        var remoteHostArguments = DeriveKnownSchemaArguments(definition.Parameters, KnownRemoteHostArgumentNames);
        var targetScopeArguments = MergeKnownArguments(
            DeriveKnownSchemaArguments(definition.Parameters, KnownTargetScopeArgumentNames),
            remoteHostArguments);

        Assert.Equal(remoteHostArguments.Length > 0, entry.Traits.SupportsRemoteHostTargeting);
        Assert.Equal(targetScopeArguments.Length > 0, entry.Traits.SupportsTargetScoping);
        Assert.Equal(remoteHostArguments, entry.Traits.RemoteHostArguments);
        Assert.Equal(targetScopeArguments, entry.Traits.TargetScopeArguments);
        Assert.Equal(
            remoteHostArguments.Length > 0 ? "local_or_remote" : "local_only",
            entry.Traits.ExecutionScope);

        AssertSetupParity(entry, definition);
        AssertHandoffParity(entry, definition);
        AssertRecoveryParity(entry, definition);
    }

    private static void AssertSetupParity(ToolPackToolCatalogEntryModel entry, ToolDefinition definition) {
        if (definition.Setup is not ToolSetupContract { IsSetupAware: true } setup) {
            Assert.False(entry.Setup.IsSetupAware);
            Assert.True(string.IsNullOrWhiteSpace(entry.Setup.SetupToolName));
            Assert.Empty(entry.Setup.RequirementIds);
            Assert.Empty(entry.Setup.HintKeys);
            return;
        }

        Assert.True(entry.Setup.IsSetupAware);
        Assert.Equal(setup.SetupToolName, entry.Setup.SetupToolName);
        Assert.Equal(
            setup.Requirements
                .Select(static requirement => requirement.RequirementId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase),
            entry.Setup.RequirementIds.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            setup.SetupHintKeys
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase),
            entry.Setup.HintKeys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static void AssertHandoffParity(ToolPackToolCatalogEntryModel entry, ToolDefinition definition) {
        if (definition.Handoff is not ToolHandoffContract { IsHandoffAware: true } handoff) {
            Assert.False(entry.Handoff.IsHandoffAware);
            Assert.Empty(entry.Handoff.Routes);
            return;
        }

        Assert.True(entry.Handoff.IsHandoffAware);
        Assert.Equal(handoff.OutboundRoutes.Count, entry.Handoff.Routes.Count);

        var expectedRoutes = handoff.OutboundRoutes.ToDictionary(
            static route => CreateRouteKey(route.TargetPackId, route.TargetToolName, route.TargetRole),
            StringComparer.OrdinalIgnoreCase);

        foreach (var route in entry.Handoff.Routes) {
            Assert.True(
                expectedRoutes.TryGetValue(CreateRouteKey(route.TargetPackId, route.TargetToolName, route.TargetRole), out var expectedRoute),
                $"Unexpected catalog handoff route for '{definition.Name}'.");
            Assert.Equal(
                expectedRoute.Bindings.Select(static binding => binding.SourceField + "->" + binding.TargetArgument),
                route.BindingPairs);
        }
    }

    private static void AssertRecoveryParity(ToolPackToolCatalogEntryModel entry, ToolDefinition definition) {
        if (definition.Recovery is not ToolRecoveryContract { IsRecoveryAware: true } recovery) {
            Assert.False(entry.Recovery.IsRecoveryAware);
            Assert.False(entry.Recovery.SupportsTransientRetry);
            Assert.Equal(0, entry.Recovery.MaxRetryAttempts);
            Assert.Empty(entry.Recovery.RetryableErrorCodes);
            Assert.Empty(entry.Recovery.RecoveryToolNames);
            return;
        }

        Assert.True(entry.Recovery.IsRecoveryAware);
        Assert.Equal(recovery.SupportsTransientRetry, entry.Recovery.SupportsTransientRetry);
        Assert.Equal(recovery.MaxRetryAttempts, entry.Recovery.MaxRetryAttempts);
        Assert.Equal(
            recovery.RetryableErrorCodes
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase),
            entry.Recovery.RetryableErrorCodes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            recovery.RecoveryToolNames
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase),
            entry.Recovery.RecoveryToolNames.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static string[] DeriveKnownSchemaArguments(JsonObject? parameters, IReadOnlyList<string> knownNames) {
        var properties = parameters?.GetObject("properties");
        if (properties is null || properties.Count == 0 || knownNames.Count == 0) {
            return Array.Empty<string>();
        }

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties) {
            var normalized = NormalizeSchemaToken(property.Key);
            if (normalized.Length == 0) {
                continue;
            }

            available.Add(normalized);
        }

        if (available.Count == 0) {
            return Array.Empty<string>();
        }

        var resolved = new List<string>();
        for (var i = 0; i < knownNames.Count; i++) {
            var candidate = knownNames[i];
            if (available.Contains(candidate)) {
                resolved.Add(candidate);
            }
        }

        return resolved.Count == 0 ? Array.Empty<string>() : resolved.ToArray();
    }

    private static string[] MergeKnownArguments(IReadOnlyList<string> first, IReadOnlyList<string> second) {
        if ((first is null || first.Count == 0) && (second is null || second.Count == 0)) {
            return Array.Empty<string>();
        }

        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendDistinct(first, merged, seen);
        AppendDistinct(second, merged, seen);
        return merged.ToArray();
    }

    private static void AppendDistinct(IReadOnlyList<string>? source, ICollection<string> destination, ISet<string> seen) {
        if (source is null || source.Count == 0) {
            return;
        }

        for (var i = 0; i < source.Count; i++) {
            var candidate = NormalizeSchemaToken(source[i]);
            if (candidate.Length == 0 || !seen.Add(candidate)) {
                continue;
            }

            destination.Add(candidate);
        }
    }

    private static string CreateRouteKey(string? packId, string? toolName, string? role) {
        return string.Join(
            "|",
            NormalizeSchemaToken(packId),
            NormalizeSchemaToken(toolName),
            NormalizeSchemaToken(role));
    }

    private static string NormalizeSchemaToken(string? value) {
        return (value ?? string.Empty).Trim();
    }
}
