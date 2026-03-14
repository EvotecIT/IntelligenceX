using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.DnsClientX;

internal static class DnsClientXToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_query"] = new[] {
                "query DNS records such as A, MX, TXT, CNAME, or PTR against a chosen resolver"
            },
            ["dnsclientx_ping"] = new[] {
                "check quick ICMP reachability for one or more hosts before deeper DNS or network triage"
            }
        };
}
