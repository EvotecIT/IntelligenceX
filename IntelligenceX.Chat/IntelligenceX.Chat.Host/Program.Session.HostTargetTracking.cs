using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private sealed partial class ReplSession {
        private void RememberRecentHostTargets(IReadOnlyList<ToolCall> calls) {
            if (calls.Count == 0) {
                return;
            }

            var candidateKeys = GetScenarioInputKeyAliases("machine_name");
            if (candidateKeys.Count == 0) {
                return;
            }

            for (var i = 0; i < calls.Count; i++) {
                var args = calls[i].Arguments;
                if (args is null) {
                    continue;
                }

                for (var keyIndex = 0; keyIndex < candidateKeys.Count; keyIndex++) {
                    var candidateKey = candidateKeys[keyIndex];
                    if (!TryReadToolInputValuesByKey(args, candidateKey, out var values) || values.Count == 0) {
                        continue;
                    }

                    for (var valueIndex = 0; valueIndex < values.Count; valueIndex++) {
                        var normalized = NormalizeHostTargetCandidate(values[valueIndex]);
                        if (normalized.Length == 0) {
                            continue;
                        }

                        RememberRecentHostTarget(normalized, ResolveToolCallDomainIntentFamily(calls[i]));
                    }
                }
            }
        }

        private void RememberRecentHostTargetsFromOutputs(
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<ToolOutput> outputs) {
            var count = Math.Min(calls.Count, outputs.Count);
            if (count <= 0) {
                return;
            }

            for (var index = 0; index < count; index++) {
                var family = ResolveToolCallDomainIntentFamily(calls[index]);
                if (string.Equals(family, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
                    var discoveredHosts = ExtractAdHostTargetsFromToolOutput(outputs[index].Output);
                    for (var hostIndex = 0; hostIndex < discoveredHosts.Length; hostIndex++) {
                        RememberRecentHostTarget(discoveredHosts[hostIndex], family);
                    }

                    continue;
                }

                if (!string.Equals(family, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                    continue;
                }

                var discoveredPublicHosts = ExtractPublicHostTargetsFromToolOutput(outputs[index].Output);
                for (var hostIndex = 0; hostIndex < discoveredPublicHosts.Length; hostIndex++) {
                    RememberRecentHostTarget(discoveredPublicHosts[hostIndex], family);
                }
            }
        }

        private string[] GetRecentHostTargetsSnapshot() {
            if (TryGetPreferredDomainIntentFamily(out var preferredFamily)) {
                if (string.Equals(preferredFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
                    return GetRecentHostTargetsSnapshot(_recentAdHostTargets);
                }

                if (string.Equals(preferredFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                    return GetRecentHostTargetsSnapshot(_recentPublicHostTargets);
                }
            }

            return GetRecentHostTargetsSnapshot(_recentHostTargets);
        }

        private static string[] GetRecentHostTargetsSnapshot(IReadOnlyList<string> source) {
            if (source.Count == 0) {
                return Array.Empty<string>();
            }

            var start = Math.Max(0, source.Count - MaxRetryPromptHostTargets);
            return source
                .Skip(start)
                .Take(MaxRetryPromptHostTargets)
                .ToArray();
        }

        private void RememberRecentHostTarget(string value, string family) {
            var normalized = NormalizeHostTargetCandidate(value);
            if (normalized.Length == 0) {
                return;
            }

            AppendRecentHostTarget(_recentHostTargets, normalized);
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                return;
            }

            if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
                AppendRecentHostTarget(_recentAdHostTargets, normalized);
                return;
            }

            if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                AppendRecentHostTarget(_recentPublicHostTargets, normalized);
            }
        }

        private static void AppendRecentHostTarget(List<string> targets, string normalized) {
            targets.RemoveAll(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
            targets.Add(normalized);
            if (targets.Count <= MaxRecentHostTargets) {
                return;
            }

            targets.RemoveAt(0);
        }

        private string ResolveToolCallDomainIntentFamily(ToolCall call) {
            var toolName = (call?.Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || !_registry.TryGetDefinition(toolName, out var definition)) {
                return string.Empty;
            }

            return ToolSelectionMetadata.TryResolveDomainIntentFamily(definition, out var family)
                ? family
                : string.Empty;
        }

        private static string[] ExtractAdHostTargetsFromToolOutput(string output) {
            var normalizedOutput = (output ?? string.Empty).Trim();
            if (normalizedOutput.Length == 0 || normalizedOutput[0] != '{') {
                return Array.Empty<string>();
            }

            try {
                using var document = JsonDocument.Parse(normalizedOutput);
                var discovered = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectAdHostTargets(document.RootElement, discovered, seen);
                return discovered.ToArray();
            } catch (JsonException) {
                return Array.Empty<string>();
            }
        }

        private static string[] ExtractPublicHostTargetsFromToolOutput(string output) {
            var normalizedOutput = (output ?? string.Empty).Trim();
            if (normalizedOutput.Length == 0 || normalizedOutput[0] != '{') {
                return Array.Empty<string>();
            }

            try {
                using var document = JsonDocument.Parse(normalizedOutput);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) {
                    return Array.Empty<string>();
                }

                var discovered = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectPublicHostTargets(document.RootElement, discovered, seen);
                return discovered.ToArray();
            } catch (JsonException) {
                return Array.Empty<string>();
            }
        }

        private static void CollectAdHostTargets(
            JsonElement node,
            ICollection<string> discovered,
            ISet<string> seen) {
            switch (node.ValueKind) {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject()) {
                        if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            && IsAdHostTargetOutputProperty(property.Name)) {
                            AddDiscoveredAdHost(discovered, seen, property.Value.GetString());
                        } else if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Array
                                   && IsAdHostTargetOutputProperty(property.Name)) {
                            foreach (var item in property.Value.EnumerateArray()) {
                                if (item.ValueKind == System.Text.Json.JsonValueKind.String) {
                                    AddDiscoveredAdHost(discovered, seen, item.GetString());
                                } else {
                                    CollectAdHostTargets(item, discovered, seen);
                                }
                            }
                        } else {
                            CollectAdHostTargets(property.Value, discovered, seen);
                        }
                    }

                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray()) {
                        CollectAdHostTargets(item, discovered, seen);
                    }

                    break;
            }
        }

        private static bool IsAdHostTargetOutputProperty(string propertyName) {
            return string.Equals(propertyName, "domain_controllers", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(propertyName, "domain_controller", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(propertyName, "machine_name", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(propertyName, "server", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddDiscoveredAdHost(
            ICollection<string> discovered,
            ISet<string> seen,
            string? value) {
            var normalized = NormalizeHostTargetCandidate(value ?? string.Empty);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                return;
            }

            discovered.Add(normalized);
        }

        private static void CollectPublicHostTargets(
            JsonElement node,
            ICollection<string> discovered,
            ISet<string> seen) {
            if (node.ValueKind != System.Text.Json.JsonValueKind.Object) {
                return;
            }

            if (TryGetPublicDnsAnswerRecordType(node, out var recordType)
                && (string.Equals(recordType, "MX", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(recordType, "NS", StringComparison.OrdinalIgnoreCase))
                && node.TryGetProperty("answers", out var answersNode)
                && answersNode.ValueKind == System.Text.Json.JsonValueKind.Array) {
                foreach (var answerNode in answersNode.EnumerateArray()) {
                    if (answerNode.ValueKind != System.Text.Json.JsonValueKind.Object) {
                        continue;
                    }

                    if (answerNode.TryGetProperty("data", out var dataNode)
                        && dataNode.ValueKind == System.Text.Json.JsonValueKind.String) {
                        AddDiscoveredPublicHost(discovered, seen, dataNode.GetString());
                    }
                }
            }

            if (node.TryGetProperty("host", out var hostNode) && hostNode.ValueKind == System.Text.Json.JsonValueKind.String) {
                AddDiscoveredPublicHost(discovered, seen, hostNode.GetString());
            }

            if (node.TryGetProperty("target", out var targetNode) && targetNode.ValueKind == System.Text.Json.JsonValueKind.String) {
                AddDiscoveredPublicHost(discovered, seen, targetNode.GetString());
            }

            foreach (var property in node.EnumerateObject()) {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    CollectPublicHostTargets(property.Value, discovered, seen);
                    continue;
                }

                if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Array) {
                    continue;
                }

                foreach (var item in property.Value.EnumerateArray()) {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
                        CollectPublicHostTargets(item, discovered, seen);
                    }
                }
            }
        }

        private static bool TryGetPublicDnsAnswerRecordType(JsonElement node, out string recordType) {
            recordType = string.Empty;
            if (!node.TryGetProperty("query", out var queryNode) || queryNode.ValueKind != System.Text.Json.JsonValueKind.Object) {
                return false;
            }

            if (!queryNode.TryGetProperty("record_type", out var recordTypeNode) || recordTypeNode.ValueKind != System.Text.Json.JsonValueKind.String) {
                return false;
            }

            recordType = (recordTypeNode.GetString() ?? string.Empty).Trim();
            return recordType.Length > 0;
        }

        private static void AddDiscoveredPublicHost(
            ICollection<string> discovered,
            ISet<string> seen,
            string? value) {
            var normalized = NormalizePublicHostTargetCandidate(value);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                return;
            }

            discovered.Add(normalized);
        }

        private static string NormalizePublicHostTargetCandidate(string? value) {
            var candidate = (value ?? string.Empty).Trim().Trim('"');
            if (candidate.Length == 0) {
                return string.Empty;
            }

            var separatorIndex = candidate.IndexOf(' ');
            if (separatorIndex > 0
                && int.TryParse(candidate[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
                candidate = candidate[(separatorIndex + 1)..].Trim();
            }

            candidate = candidate.TrimEnd('.');
            if (candidate.Length == 0 || candidate[0] == '_') {
                return string.Empty;
            }

            var normalized = NormalizeHostTargetCandidate(candidate);
            if (normalized.Length == 0 || Uri.CheckHostName(normalized) == UriHostNameType.Unknown) {
                return string.Empty;
            }

            return normalized;
        }

        private static string NormalizeHostTargetCandidate(string value) {
            var candidate = (value ?? string.Empty).Trim();
            if (candidate.Length < 2 || candidate.Length > 128) {
                return string.Empty;
            }

            if (candidate.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
                return string.Empty;
            }

            return candidate;
        }

        private static List<string> OrderHostTargetCandidatesBySpecificity(IReadOnlyList<string> candidates) {
            if (candidates.Count <= 1) {
                return candidates as List<string> ?? candidates.ToList();
            }

            return candidates
                .Select(static (value, index) => new {
                    Value = value,
                    Index = index,
                    Score = ComputeHostTargetSpecificity(value)
                })
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Index)
                .Select(static candidate => candidate.Value)
                .ToList();
        }

        private static int ComputeHostTargetSpecificity(string value) {
            var candidate = NormalizeHostTargetCandidate(value);
            if (candidate.Length == 0) {
                return int.MinValue;
            }

            var score = candidate.Contains('.')
                ? HostTargetSpecificityFqdnBonus
                : HostTargetSpecificityShortNameBonus;
            if (Uri.CheckHostName(candidate) is UriHostNameType.IPv4 or UriHostNameType.IPv6) {
                score -= HostTargetSpecificityIpLiteralPenalty;
            }

            if (candidate.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) {
                score -= HostTargetSpecificityLocalhostPenalty;
            }

            return score;
        }

    }
}
