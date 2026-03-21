using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static string[] ResolveCapabilitySnapshotSkills(
        IEnumerable<PluginInfoDto>? plugins,
        ToolRoutingCatalogDiagnostics? routingCatalog,
        IEnumerable<string>? connectedRuntimeSkills = null,
        IEnumerable<string>? fallbackSkills = null) {
        var explicitSkillIds = NormalizeCapabilitySnapshotSkills(
            (plugins ?? Array.Empty<PluginInfoDto>())
            .Where(static plugin => plugin.Enabled)
            .SelectMany(static plugin => plugin.SkillIds ?? Array.Empty<string>())
            .Concat(connectedRuntimeSkills ?? Array.Empty<string>()));
        if (explicitSkillIds.Length > 0) {
            return explicitSkillIds;
        }

        var routingSkillIds = NormalizeCapabilitySnapshotSkills(
            MapCapabilityFamilyActions(routingCatalog)
            .Select(static summary => BuildSkillSnapshotValue(summary.Family, summary.ActionId)));
        if (routingSkillIds.Length > 0) {
            return routingSkillIds;
        }

        return NormalizeCapabilitySnapshotSkills(fallbackSkills ?? Array.Empty<string>());
    }
}
