using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Native.Rendering;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Resolves concise native table artifact titles from projected table schemas.
/// </summary>
internal static class NativeTableArtifactTitleResolver {
    public static string Resolve(NativeTranscriptTable table) {
        if (table == null) throw new ArgumentNullException(nameof(table));

        var headers = table.Headers
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Select(header => header.Trim())
            .ToArray();
        if (ContainsAll(headers, "Account", "Risk", "Last Sign-in")) {
            return "Privileged Accounts";
        }

        if (ContainsAll(headers, "Workload", "Finding", "Severity")) {
            return "Tenant Findings";
        }

        if (ContainsAll(headers, "Domain", "SPF", "DKIM", "DMARC")) {
            return "Mail Authentication";
        }

        if (ContainsAll(headers, "Time", "Actor", "Action")) {
            return "Incident Timeline";
        }

        if (ContainsAll(headers, "Object", "Kind", "Finding")) {
            return "Directory Objects";
        }

        if (ContainsAll(headers, "Group", "Removed Members")) {
            return "Group Cleanup";
        }

        if (ContainsAll(headers, "Account", "Exception", "Expires")) {
            return "MFA Exceptions";
        }

        return "Table Evidence";
    }

    private static bool ContainsAll(IReadOnlyCollection<string> headers, params string[] expected) =>
        expected.All(value => headers.Contains(value, StringComparer.OrdinalIgnoreCase));
}
