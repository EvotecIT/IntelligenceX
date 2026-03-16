using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_environment_discover"] = new[] {
                "discover Active Directory environment scope, search directory objects, and target a specific domain controller or base DN",
                "bootstrap AD work by finding domains, forest context, and viable domain controllers before narrower checks"
            },
            ["ad_scope_discovery"] = new[] {
                "discover Active Directory environment scope, search directory objects, and target a specific domain controller or base DN",
                "prepare the right domain, search base, and domain controller for follow-up identity or replication analysis"
            },
            ["ad_search"] = new[] {
                "search users, groups, computers, or other directory objects inside the current domain or a focused base DN"
            },
            ["ad_object_resolve"] = new[] {
                "resolve a known identity, computer, or distinguished name into focused Active Directory evidence for follow-up checks"
            },
            ["ad_monitoring_probe_run"] = new[] {
                "run live AD monitoring probes such as ldap, dns, kerberos, ntp, replication, ping, or windows_update against domain controllers"
            },
            ["ad_replication_summary"] = new[] {
                "summarize forest or domain replication posture before deciding whether a problem is directory-wide or host-specific"
            },
            ["ad_domain_controller_facts"] = new[] {
                "collect domain controller inventory and posture details that can hand off into remote host diagnostics"
            }
        };
}
