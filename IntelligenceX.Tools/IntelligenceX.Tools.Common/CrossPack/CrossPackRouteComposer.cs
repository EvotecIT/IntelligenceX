using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

internal static class CrossPackRouteComposer {
    public static ToolHandoffRoute[] Combine(params IReadOnlyList<ToolHandoffRoute>[] routeGroups) {
        if (routeGroups is null || routeGroups.Length == 0) {
            return Array.Empty<ToolHandoffRoute>();
        }

        var totalCount = 0;
        for (var i = 0; i < routeGroups.Length; i++) {
            var group = routeGroups[i];
            if (group is null || group.Count == 0) {
                continue;
            }

            totalCount += group.Count;
        }

        if (totalCount == 0) {
            return Array.Empty<ToolHandoffRoute>();
        }

        var combined = new ToolHandoffRoute[totalCount];
        var offset = 0;
        for (var i = 0; i < routeGroups.Length; i++) {
            var group = routeGroups[i];
            if (group is null || group.Count == 0) {
                continue;
            }

            for (var j = 0; j < group.Count; j++) {
                combined[offset++] = group[j];
            }
        }

        return combined;
    }
}
