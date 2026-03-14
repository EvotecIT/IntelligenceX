using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.DomainDetective;

internal static class DomainDetectiveToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_checks_catalog"] = new[] {
                "list supported DomainDetective checks and baseline defaults before choosing a domain analysis"
            },
            ["domaindetective_domain_summary"] = new[] {
                "run DomainDetective posture checks for a public domain across DNS, email, and security controls"
            },
            ["domaindetective_network_probe"] = new[] {
                "run ping or traceroute diagnostics for a host tied to a public-domain investigation"
            }
        };
}
