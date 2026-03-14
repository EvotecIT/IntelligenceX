using System;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static object? BuildRoutingCatalogState(SessionRoutingCatalogDiagnosticsDto? routingCatalog) {
        if (routingCatalog is null) {
            return null;
        }

        return new {
            totalTools = routingCatalog.TotalTools,
            routingAwareTools = routingCatalog.RoutingAwareTools,
            explicitRoutingTools = routingCatalog.ExplicitRoutingTools,
            inferredRoutingTools = routingCatalog.InferredRoutingTools,
            missingRoutingContractTools = routingCatalog.MissingRoutingContractTools,
            missingPackIdTools = routingCatalog.MissingPackIdTools,
            missingRoleTools = routingCatalog.MissingRoleTools,
            setupAwareTools = routingCatalog.SetupAwareTools,
            handoffAwareTools = routingCatalog.HandoffAwareTools,
            recoveryAwareTools = routingCatalog.RecoveryAwareTools,
            remoteCapableTools = routingCatalog.RemoteCapableTools,
            crossPackHandoffTools = routingCatalog.CrossPackHandoffTools,
            domainFamilyTools = routingCatalog.DomainFamilyTools,
            expectedDomainFamilyMissingTools = routingCatalog.ExpectedDomainFamilyMissingTools,
            domainFamilyMissingActionTools = routingCatalog.DomainFamilyMissingActionTools,
            actionWithoutFamilyTools = routingCatalog.ActionWithoutFamilyTools,
            familyActionConflictFamilies = routingCatalog.FamilyActionConflictFamilies,
            isHealthy = routingCatalog.IsHealthy,
            isExplicitRoutingReady = routingCatalog.IsExplicitRoutingReady,
            familyActions = Array.ConvertAll(
                routingCatalog.FamilyActions,
                static item => new {
                    family = item.Family,
                    actionId = item.ActionId,
                    toolCount = item.ToolCount
                }),
            autonomyReadinessHighlights = routingCatalog.AutonomyReadinessHighlights ?? Array.Empty<string>()
        };
    }

    private static object? BuildCapabilitySnapshotState(SessionCapabilitySnapshotDto? capabilitySnapshot) {
        if (capabilitySnapshot is null) {
            return null;
        }

        return new {
            registeredTools = capabilitySnapshot.RegisteredTools,
            enabledPackCount = capabilitySnapshot.EnabledPackCount,
            pluginCount = capabilitySnapshot.PluginCount,
            enabledPluginCount = capabilitySnapshot.EnabledPluginCount,
            toolingAvailable = capabilitySnapshot.ToolingAvailable,
            allowedRootCount = capabilitySnapshot.AllowedRootCount,
            enabledPackIds = capabilitySnapshot.EnabledPackIds ?? Array.Empty<string>(),
            enabledPluginIds = capabilitySnapshot.EnabledPluginIds ?? Array.Empty<string>(),
            routingFamilies = capabilitySnapshot.RoutingFamilies ?? Array.Empty<string>(),
            familyActions = Array.ConvertAll(
                capabilitySnapshot.FamilyActions ?? Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                static item => new {
                    family = item.Family,
                    actionId = item.ActionId,
                    toolCount = item.ToolCount
                }),
            skills = capabilitySnapshot.Skills ?? Array.Empty<string>(),
            healthyTools = capabilitySnapshot.HealthyTools ?? Array.Empty<string>(),
            remoteReachabilityMode = capabilitySnapshot.RemoteReachabilityMode,
            autonomy = capabilitySnapshot.Autonomy is null ? null : new {
                remoteCapableToolCount = capabilitySnapshot.Autonomy.RemoteCapableToolCount,
                setupAwareToolCount = capabilitySnapshot.Autonomy.SetupAwareToolCount,
                handoffAwareToolCount = capabilitySnapshot.Autonomy.HandoffAwareToolCount,
                recoveryAwareToolCount = capabilitySnapshot.Autonomy.RecoveryAwareToolCount,
                crossPackHandoffToolCount = capabilitySnapshot.Autonomy.CrossPackHandoffToolCount,
                remoteCapablePackIds = capabilitySnapshot.Autonomy.RemoteCapablePackIds ?? Array.Empty<string>(),
                crossPackReadyPackIds = capabilitySnapshot.Autonomy.CrossPackReadyPackIds ?? Array.Empty<string>(),
                crossPackTargetPackIds = capabilitySnapshot.Autonomy.CrossPackTargetPackIds ?? Array.Empty<string>()
            },
            parityAttentionCount = capabilitySnapshot.ParityAttentionCount,
            parityMissingCapabilityCount = capabilitySnapshot.ParityMissingCapabilityCount
        };
    }
}
