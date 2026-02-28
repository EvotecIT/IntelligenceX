using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if DOMAINDETECTIVE_ENABLED
using DomainDetective;
#endif

namespace IntelligenceX.Tools.DomainDetective;

internal static class DomainDetectiveCheckNameCatalog {
    private static readonly IReadOnlyList<string> DefaultChecksValue = Array.AsReadOnly(new[] {
        "DNSHEALTH",
        "SOA",
        "NS",
        "MX",
        "SPF",
        "DMARC",
        "DNSSEC",
        "TTL"
    });

    private static readonly IReadOnlyDictionary<string, string> CheckAliasByToken = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal) {
            ["NAMESERVER"] = "NS",
            ["NAMESERVERS"] = "NS",
            ["NAMESERVERRECORD"] = "NS",
            ["NAMESERVERRECORDS"] = "NS",
            ["NSRECORD"] = "NS",
            ["NSRECORDS"] = "NS",
            ["MXRECORD"] = "MX",
            ["MXRECORDS"] = "MX",
            ["MAILSERVER"] = "MX",
            ["MAILSERVERS"] = "MX",
            ["SPFRECORD"] = "SPF",
            ["SPFRECORDS"] = "SPF",
            ["DMARCRECORD"] = "DMARC",
            ["DMARCRECORDS"] = "DMARC",
            ["DKIMRECORD"] = "DKIM",
            ["DKIMRECORDS"] = "DKIM",
            ["CAARECORD"] = "CAA",
            ["CAARECORDS"] = "CAA"
        });

    internal static IReadOnlyList<string> DefaultChecks => DefaultChecksValue;
    internal static IReadOnlyDictionary<string, string> AliasByToken => CheckAliasByToken;

    internal static string NormalizeDomainDetectiveCheckName(string check) {
        var token = NormalizeCheckLookupToken(check);
        if (token.Length == 0) {
            return string.Empty;
        }

        if (CheckAliasByToken.TryGetValue(token, out var alias)) {
            return alias;
        }

        return token;
    }

    internal static string NormalizeCheckLookupToken(string value) {
        var input = (value ?? string.Empty).Trim();
        if (input.Length == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];
            if (!char.IsLetterOrDigit(ch)) {
                continue;
            }

            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    internal static IReadOnlyList<string> GetSupportedCheckNames() {
#if DOMAINDETECTIVE_ENABLED
        return Enum.GetNames<HealthCheckType>()
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
#else
        return DefaultChecksValue
            .Concat(CheckAliasByToken.Values)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
#endif
    }

#if DOMAINDETECTIVE_ENABLED
    internal static bool TryResolveHealthCheckType(string check, out HealthCheckType parsed) {
        parsed = default;
        if (string.IsNullOrWhiteSpace(check)) {
            return false;
        }

        var trimmed = check.Trim();
        if (Enum.TryParse<HealthCheckType>(trimmed, ignoreCase: true, out parsed)) {
            return true;
        }

        var normalized = NormalizeDomainDetectiveCheckName(trimmed);
        if (normalized.Length == 0) {
            return false;
        }

        return Enum.TryParse<HealthCheckType>(normalized, ignoreCase: true, out parsed);
    }
#endif
}
