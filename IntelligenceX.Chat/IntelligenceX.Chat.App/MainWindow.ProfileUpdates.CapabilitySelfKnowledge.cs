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

        if (runtimeIntrospectionMode) {
            if (enabledPackNames.Count == 0) {
                lines.Add("If tooling details are still sparse, answer with only confirmed runtime or model facts and say the rest is still loading.");
            }

            lines.Add("For runtime self-report, mention only the live tooling or capability areas that are relevant to the user's scope.");
            lines.Add("Keep this section practical and concise; exact runtime/model/tool limits belong in the runtime capability handshake.");
        } else {
            AddGenericCapabilityGuidance(lines, enabledPackNames);
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

    private static void AddGenericCapabilityGuidance(List<string> lines, IReadOnlyList<string> enabledPackNames) {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(enabledPackNames);

        if (enabledPackNames.Count == 0) {
            lines.Add("Keep capability claims narrow until the session policy finishes loading and you can name the enabled areas confidently.");
            lines.Add("Concrete examples you can mention: only tasks that are clearly confirmed by the current session policy or recent runtime evidence.");
            return;
        }

        lines.Add("Use the enabled capability areas above as the source of truth for what is live in this session.");
        lines.Add("Concrete examples you can mention: a few practical tasks grounded in the enabled areas above, phrased in the user's language and scope.");

        if (enabledPackNames.Count == 1) {
            lines.Add("If you need a concrete anchor, start from the single enabled area above instead of inventing broader capability claims.");
            return;
        }

        lines.Add("Prefer the enabled areas that best match the user's request instead of listing every area in the session.");
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
