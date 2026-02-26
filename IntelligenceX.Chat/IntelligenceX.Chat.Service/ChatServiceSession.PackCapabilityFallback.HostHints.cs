using System;
using System.Collections.Generic;
using System.Text.Json;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private static string? TryExtractHostHintFromUserRequest(string? userRequest) {
        var text = NormalizeRoutingUserText((userRequest ?? string.Empty).Trim());
        if (text.Length == 0) {
            return null;
        }

        var bestCandidate = string.Empty;
        var bestScore = 0;
        var tokenStart = -1;
        for (var i = 0; i <= text.Length; i++) {
            var ch = i < text.Length ? text[i] : '\0';
            var tokenChar = i < text.Length
                            && (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_');
            if (tokenChar) {
                if (tokenStart < 0) {
                    tokenStart = i;
                }
                continue;
            }

            if (tokenStart < 0) {
                continue;
            }

            var candidate = text.Substring(tokenStart, i - tokenStart);
            tokenStart = -1;
            var score = ScoreHostHintCandidate(candidate);
            if (score <= bestScore) {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
        }

        return bestScore > 0 ? bestCandidate : null;
    }

    private static string? TryResolveHostHintFromPriorDiscoveryOutputs(string hostHint, IReadOnlyList<ToolOutputDto> toolOutputs) {
        var normalizedHint = (hostHint ?? string.Empty).Trim();
        if (normalizedHint.Length == 0 || toolOutputs.Count == 0) {
            return null;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = toolOutputs.Count - 1; i >= 0; i--) {
            var payload = (toolOutputs[i].Output ?? string.Empty).Trim();
            if (payload.Length == 0 || payload[0] != '{') {
                continue;
            }

            try {
                using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                CollectHostCandidates(doc.RootElement, candidates, depth: 0, maxDepth: 4, budget: 256);
            } catch (JsonException) {
                // Best-effort host discovery only.
            }
        }

        if (candidates.Count == 0) {
            return null;
        }

        var bestCandidate = string.Empty;
        var bestScore = 0;
        foreach (var candidate in candidates) {
            var score = ScoreHostHintMatch(normalizedHint, candidate);
            if (score <= bestScore) {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
        }

        return bestScore > 0 ? bestCandidate : null;
    }

    private static int ScoreHostHintMatch(string hint, string candidate) {
        var normalizedHint = (hint ?? string.Empty).Trim();
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedHint.Length == 0 || normalizedCandidate.Length == 0) {
            return 0;
        }

        if (!IsHostLikeCandidate(normalizedCandidate)) {
            return 0;
        }

        if (string.Equals(normalizedHint, normalizedCandidate, StringComparison.OrdinalIgnoreCase)) {
            return normalizedCandidate.Contains('.', StringComparison.Ordinal) ? 8 : 6;
        }

        if (normalizedCandidate.StartsWith(normalizedHint + ".", StringComparison.OrdinalIgnoreCase)) {
            return 7;
        }

        var hintLabel = ExtractPrimaryHostLabel(normalizedHint);
        var candidateLabel = ExtractPrimaryHostLabel(normalizedCandidate);
        if (hintLabel.Length == 0 || candidateLabel.Length == 0) {
            return 0;
        }

        if (string.Equals(hintLabel, candidateLabel, StringComparison.OrdinalIgnoreCase)) {
            return normalizedCandidate.Contains('.', StringComparison.Ordinal) ? 6 : 4;
        }

        return 0;
    }

    private static string ExtractPrimaryHostLabel(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var dot = normalized.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? normalized[..dot] : normalized;
    }

    private static void CollectHostCandidates(JsonElement node, HashSet<string> candidates, int depth, int maxDepth, int budget) {
        if (depth > maxDepth || budget <= 0) {
            return;
        }

        switch (node.ValueKind) {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject()) {
                    if (budget-- <= 0) {
                        return;
                    }

                    var name = property.Name;
                    if (LooksLikeHostFieldName(name)) {
                        AddHostCandidateFromNode(property.Value, candidates);
                    }

                    CollectHostCandidates(property.Value, candidates, depth + 1, maxDepth, budget);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray()) {
                    if (budget-- <= 0) {
                        return;
                    }

                    AddHostCandidateFromNode(item, candidates);
                    CollectHostCandidates(item, candidates, depth + 1, maxDepth, budget);
                }
                break;
        }
    }

    private static bool LooksLikeHostFieldName(string name) {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Equals("machine_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("computer_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("hostname", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("host_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("dns_host_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("dnshostname", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("server", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("server_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domain_controller", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domain_controllers", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domainControllers", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddHostCandidateFromNode(JsonElement node, HashSet<string> candidates) {
        if (node.ValueKind == JsonValueKind.String) {
            var value = (node.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) {
            return;
        }

        if (node.TryGetProperty("machine_name", out var machineNameNode) && machineNameNode.ValueKind == JsonValueKind.String) {
            var value = (machineNameNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
        if (node.TryGetProperty("computer_name", out var computerNameNode) && computerNameNode.ValueKind == JsonValueKind.String) {
            var value = (computerNameNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
        if (node.TryGetProperty("dns_host_name", out var dnsHostNode) && dnsHostNode.ValueKind == JsonValueKind.String) {
            var value = (dnsHostNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
        if (node.TryGetProperty("dNSHostName", out var dnsHostCaseNode) && dnsHostCaseNode.ValueKind == JsonValueKind.String) {
            var value = (dnsHostCaseNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
    }

    private static bool IsHostLikeCandidate(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 2 or > 255) {
            return false;
        }

        if (normalized.StartsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains(' ', StringComparison.Ordinal)
            || normalized.Contains('\\', StringComparison.Ordinal)
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains('@', StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)) {
            return false;
        }

        var hasLetter = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetter(ch)) {
                hasLetter = true;
                continue;
            }

            if (char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '_') {
                continue;
            }

            return false;
        }

        return hasLetter;
    }

    private static int ScoreHostHintCandidate(string candidate) {
        var value = (candidate ?? string.Empty).Trim();
        if (value.Length is < 3 or > 255) {
            return 0;
        }

        if (value.StartsWith(".", StringComparison.Ordinal)
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)) {
            return 0;
        }

        var hasLetter = false;
        var hasDigit = false;
        var hasDot = false;
        var hasDash = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsLetter(ch)) {
                hasLetter = true;
                continue;
            }

            if (char.IsDigit(ch)) {
                hasDigit = true;
                continue;
            }

            if (ch == '.') {
                hasDot = true;
                continue;
            }

            if (ch == '-') {
                hasDash = true;
                continue;
            }

            if (ch == '_') {
                continue;
            }

            return 0;
        }

        if (!hasLetter) {
            return 0;
        }

        // Keep the heuristic shape-based and language-agnostic:
        // host-like candidates should look like inventory labels (digit/dot) or longer dashed ids.
        if (!hasDigit && !hasDot && !(hasDash && value.Length >= 6)) {
            return 0;
        }

        var score = 1;
        if (hasDot) {
            score += 3;
        }
        if (hasDigit) {
            score += 2;
        }
        if (hasDash) {
            score += 1;
        }
        if (value.Length >= 8) {
            score += 1;
        }

        return score;
    }

}
