using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    internal IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(bool runtimeIntrospectionMode = false) {
        return BuildCapabilitySelfKnowledgeLines(_sessionPolicy, runtimeIntrospectionMode);
    }

    internal static IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        bool runtimeIntrospectionMode = false) {
        var lines = new List<string>();
        var snapshot = sessionPolicy?.CapabilitySnapshot;
        var enabledPackIds = BuildEnabledPackIds(sessionPolicy);
        var enabledPackNames = BuildEnabledPackDisplayNames(sessionPolicy);
        if (enabledPackNames.Count > 0) {
            lines.Add("Areas you can help with here include " + string.Join(", ", enabledPackNames) + ".");
        }

        if (snapshot is not null) {
            if (snapshot.ToolingAvailable) {
                lines.Add("You can actively use live session tools when the user wants checks, investigation, or data gathering.");
            } else {
                lines.Add("Tooling is not currently available in this session, so answers should stay conversational and reasoning-based.");
            }

            if (!string.IsNullOrWhiteSpace(snapshot.RemoteReachabilityMode)) {
                lines.Add("Remote reachability right now is " + DescribeReachabilityMode(snapshot.RemoteReachabilityMode) + ".");
            }
        } else if (enabledPackNames.Count == 0) {
            lines.Add("Session capabilities are still loading, so avoid pretending to have tools you cannot verify.");
        }

        var capabilityCategories = BuildCapabilityCategorySummaries(enabledPackIds);
        for (var i = 0; i < capabilityCategories.Count; i++) {
            lines.Add(capabilityCategories[i]);
        }

        var exampleLines = BuildCapabilityExampleLines(enabledPackIds);
        for (var i = 0; i < exampleLines.Count; i++) {
            lines.Add(exampleLines[i]);
        }

        if (runtimeIntrospectionMode) {
            lines.Add("Keep this section practical and concise; exact runtime/model/tool limits belong in the runtime capability handshake.");
        } else {
            lines.Add("For explicit capability questions, lead with a few practical examples that are genuinely live in this session, then invite the user's task.");
            lines.Add("When asked what you can do, answer with useful examples and invite the task instead of listing internal identifiers or protocol details.");
        }

        return lines;
    }

    private static List<string> BuildEnabledPackDisplayNames(SessionPolicyDto? sessionPolicy) {
        var names = new List<string>();
        var packs = sessionPolicy?.Packs;
        if (packs is not { Length: > 0 }) {
            return names;
        }

        for (var i = 0; i < packs.Length; i++) {
            var pack = packs[i];
            if (!pack.Enabled) {
                continue;
            }

            var displayName = (pack.Name ?? string.Empty).Trim();
            if (displayName.Length == 0) {
                displayName = NormalizePackId(pack.Id);
            }

            if (displayName.Length > 0 && !ContainsIgnoreCase(names, displayName)) {
                names.Add(displayName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static HashSet<string> BuildEnabledPackIds(SessionPolicyDto? sessionPolicy) {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packs = sessionPolicy?.Packs;
        if (packs is not { Length: > 0 }) {
            return ids;
        }

        for (var i = 0; i < packs.Length; i++) {
            var pack = packs[i];
            if (!pack.Enabled) {
                continue;
            }

            var normalizedId = NormalizePackId(pack.Id);
            if (normalizedId.Length == 0) {
                normalizedId = NormalizePackId(pack.Name);
            }

            if (normalizedId.Length > 0) {
                ids.Add(normalizedId);
            }
        }

        return ids;
    }

    private static List<string> BuildCapabilityCategorySummaries(ISet<string> enabledPackIds) {
        ArgumentNullException.ThrowIfNull(enabledPackIds);

        var lines = new List<string>();
        if (HasPackCapability(enabledPackIds, "ad", "adplayground", "active_directory")) {
            lines.Add("You can help with Active Directory checks such as users, groups, LDAP lookups, and domain-controller or replication-related investigation when those tools are enabled.");
        }

        if (HasPackCapability(enabledPackIds, "eventlog", "event_viewer")) {
            lines.Add("You can inspect Windows event logs and correlate system evidence when the session has Event Log tooling available.");
        }

        if (HasPackCapability(enabledPackIds, "dnsclientx", "domaindetective", "public_domain")) {
            lines.Add("You can investigate public-domain signals such as DNS and mail configuration when the relevant tooling is enabled.");
        }

        if (HasPackCapability(enabledPackIds, "system")) {
            lines.Add("You can inspect local system posture such as services, scheduled tasks, installed updates, and host-level inventory when those tools are enabled.");
        }

        if (HasPackCapability(enabledPackIds, "fs", "filesystem", "officeimo")) {
            lines.Add("You can inspect allowed filesystem content and extract evidence from common document formats when those tools are enabled.");
        }

        return lines;
    }

    private static List<string> BuildCapabilityExampleLines(ISet<string> enabledPackIds) {
        ArgumentNullException.ThrowIfNull(enabledPackIds);

        var lines = new List<string>();
        if (HasPackCapability(enabledPackIds, "ad", "adplayground", "active_directory")) {
            lines.Add("Concrete examples you can mention: check AD replication health, find users/groups/computers, or review group membership and LDAP data.");
        }

        if (HasPackCapability(enabledPackIds, "eventlog", "event_viewer")) {
            lines.Add("Concrete examples you can mention: inspect Windows event logs, summarize recurring errors, or correlate recent failures on this machine or a reachable target.");
        }

        if (HasPackCapability(enabledPackIds, "dnsclientx", "domaindetective", "public_domain")) {
            lines.Add("Concrete examples you can mention: inspect public DNS, check MX/SPF/DMARC, or review mail-related public-domain signals.");
        }

        if (HasPackCapability(enabledPackIds, "system")) {
            lines.Add("Concrete examples you can mention: inspect local host inventory, services, scheduled tasks, or security posture on the current machine.");
        }

        if (HasPackCapability(enabledPackIds, "fs", "filesystem", "officeimo")) {
            lines.Add("Concrete examples you can mention: search allowed files and folders, inspect configuration artifacts, or extract useful content from documents.");
        }

        return lines;
    }

    private static bool HasPackCapability(ISet<string> enabledPackIds, params string[] expectedIds) {
        ArgumentNullException.ThrowIfNull(enabledPackIds);
        ArgumentNullException.ThrowIfNull(expectedIds);

        for (var i = 0; i < expectedIds.Length; i++) {
            var expected = NormalizePackId(expectedIds[i]);
            if (expected.Length > 0 && enabledPackIds.Contains(expected)) {
                return true;
            }
        }

        return false;
    }

    private static string DescribeReachabilityMode(string? mode) {
        var normalized = (mode ?? string.Empty).Trim();
        if (normalized.Equals("remote_capable", StringComparison.OrdinalIgnoreCase)) {
            return "remote-capable";
        }

        if (normalized.Equals("local_only", StringComparison.OrdinalIgnoreCase)) {
            return "local-only";
        }

        return normalized.Length == 0 ? "unknown" : normalized;
    }

    private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string candidate) {
        ArgumentNullException.ThrowIfNull(values);
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0) {
            return false;
        }

        for (var i = 0; i < values.Count; i++) {
            if (string.Equals((values[i] ?? string.Empty).Trim(), normalizedCandidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
