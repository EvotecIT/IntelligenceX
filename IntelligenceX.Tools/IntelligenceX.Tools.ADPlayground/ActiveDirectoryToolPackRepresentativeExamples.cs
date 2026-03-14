using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_environment_discover"] = new[] {
                "discover Active Directory environment scope, search directory objects, and target a specific domain controller or base DN"
            },
            ["ad_scope_discovery"] = new[] {
                "discover Active Directory environment scope, search directory objects, and target a specific domain controller or base DN"
            },
            ["ad_search"] = new[] {
                "search users, groups, computers, or other directory objects inside the current domain or a focused base DN"
            },
            ["ad_object_resolve"] = new[] {
                "resolve a known identity, computer, or distinguished name into focused Active Directory evidence for follow-up checks"
            }
        };
}
