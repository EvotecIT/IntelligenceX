using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private bool TryBuildDomainIntentHostScopeGuardrailOutput(string threadId, string userRequest, ToolCall call, out ToolOutputDto output) {
        output = null!;
        ToolDefinition? toolDefinition = null;
        if (_registry.TryGetDefinition(call.Name, out var registeredDefinition) && registeredDefinition is not null) {
            toolDefinition = ToolSelectionMetadata.Enrich(registeredDefinition, toolType: null);
        }

        if (!TryGetCurrentDomainIntentFamily(threadId, out var family)
            || !string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
            || !IsDomainIntentHostGuardrailCandidateTool(call.Name, toolDefinition)) {
            return false;
        }

        var hostTargets = ExtractHostScopedTargets(call.Arguments);
        if (hostTargets.Length == 0) {
            return false;
        }

        var knownPublicHosts = CollectThreadHostCandidatesByDomainIntentFamily(threadId, DomainIntentFamilyPublic);
        if (knownPublicHosts.Length == 0) {
            return false;
        }

        var userHostHints = CollectHostHintsFromUserRequest(userRequest);
        var blockedTargets = new List<string>(hostTargets.Length);
        for (var i = 0; i < hostTargets.Length; i++) {
            var target = hostTargets[i];
            if (HostMatchesAnyCandidate(target, userHostHints)) {
                continue;
            }

            if (HostMatchesAnyCandidate(target, knownPublicHosts)) {
                blockedTargets.Add(target);
            }
        }

        if (blockedTargets.Count == 0) {
            return false;
        }

        var blockedPreview = string.Join(", ", blockedTargets.Take(3));
        var guardrail = ToolOutputEnvelope.Error(
            errorCode: DomainScopeHostGuardrailErrorCode,
            error:
            $"Blocked '{call.Name}' host target(s) in ad_domain scope because they match prior public_domain evidence: {blockedPreview}.",
            hints: new[] {
                "Run ad_scope_discovery or ad_domain_controllers first, then retry AD-scope host checks with AD-derived hosts.",
                "If this exact host is intended, include it explicitly in this turn's user request."
            },
            isTransient: false);
        output = BuildToolOutputDto(call.CallId, guardrail);
        return true;
    }

    private static bool IsDomainIntentHostGuardrailCandidateTool(string toolName, ToolDefinition? definition = null) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return false;
        }

        if (definition is not null) {
            if (ToolSelectionMetadata.TryResolveDomainIntentFamily(definition, out var family)
                && string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                return true;
            }
        }

        return ToolSelectionMetadata.TryResolveDomainIntentFamily(
                   normalizedToolName,
                   definition?.Category,
                   definition?.Tags,
                   out var inferredFamily)
               && string.Equals(inferredFamily, DomainIntentFamilyAd, StringComparison.Ordinal);
    }

    private static string[] ExtractHostScopedTargets(JsonObject? arguments) {
        if (arguments is null || arguments.Count == 0) {
            return Array.Empty<string>();
        }

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectHostScopedTargetsFromObject(arguments, targets, depth: 0, maxDepth: 3);
        return targets.Count == 0 ? Array.Empty<string>() : targets.ToArray();
    }

    private static void CollectHostScopedTargetsFromObject(JsonObject node, HashSet<string> targets, int depth, int maxDepth) {
        if (depth > maxDepth || node is null) {
            return;
        }

        foreach (var pair in node) {
            var key = (pair.Key ?? string.Empty).Trim();
            var value = pair.Value;
            if (key.Length == 0 || value is null) {
                continue;
            }

            if (IsHostScopedArgumentKey(key)) {
                CollectHostScopedTargetsFromValue(value, targets, depth, maxDepth);
                continue;
            }

            if (value.Kind == IntelligenceX.Json.JsonValueKind.Object) {
                var nestedObject = value.AsObject();
                if (nestedObject is not null) {
                    CollectHostScopedTargetsFromObject(nestedObject, targets, depth + 1, maxDepth);
                }
                continue;
            }

            if (value.Kind == IntelligenceX.Json.JsonValueKind.Array) {
                var nestedArray = value.AsArray();
                if (nestedArray is not null) {
                    CollectHostScopedTargetsFromArray(nestedArray, targets, depth + 1, maxDepth);
                }
            }
        }
    }

    private static void CollectHostScopedTargetsFromArray(JsonArray node, HashSet<string> targets, int depth, int maxDepth) {
        if (depth > maxDepth || node is null) {
            return;
        }

        foreach (var value in node) {
            CollectHostScopedTargetsFromValue(value, targets, depth, maxDepth);
        }
    }

    private static void CollectHostScopedTargetsFromValue(JsonValue value, HashSet<string> targets, int depth, int maxDepth) {
        if (value is null || depth > maxDepth) {
            return;
        }

        if (value.Kind == IntelligenceX.Json.JsonValueKind.String) {
            var candidate = (value.AsString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(candidate)) {
                targets.Add(candidate);
            }
            return;
        }

        if (value.Kind == IntelligenceX.Json.JsonValueKind.Array) {
            var array = value.AsArray();
            if (array is not null) {
                CollectHostScopedTargetsFromArray(array, targets, depth + 1, maxDepth);
            }
            return;
        }

        if (value.Kind == IntelligenceX.Json.JsonValueKind.Object) {
            var obj = value.AsObject();
            if (obj is not null) {
                CollectHostScopedTargetsFromObject(obj, targets, depth + 1, maxDepth);
            }
        }
    }

    private static bool IsHostScopedArgumentKey(string key) {
        var normalized = (key ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Equals("machine_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("computer_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("host", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("hostname", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("host_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("dns_host_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domain_controller", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domain_controllers", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domainControllers", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("include_domain_controllers", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("exclude_domain_controllers", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("targets", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("target", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("server", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("server_name", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] CollectHostHintsFromUserRequest(string userRequest) {
        var normalizedRequest = (userRequest ?? string.Empty).Trim();
        if (normalizedRequest.Length == 0) {
            return Array.Empty<string>();
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitHint = TryExtractHostHintFromUserRequest(normalizedRequest);
        if (!string.IsNullOrWhiteSpace(explicitHint)) {
            candidates.Add(explicitHint.Trim());
        }

        var tokenStart = -1;
        for (var i = 0; i <= normalizedRequest.Length; i++) {
            var ch = i < normalizedRequest.Length ? normalizedRequest[i] : '\0';
            var tokenChar = i < normalizedRequest.Length
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

            var candidate = normalizedRequest.Substring(tokenStart, i - tokenStart).Trim();
            tokenStart = -1;
            if (ScoreHostHintCandidate(candidate) > 0) {
                candidates.Add(candidate);
            }
        }

        return candidates.Count == 0 ? Array.Empty<string>() : candidates.ToArray();
    }

    private static bool HostMatchesAnyCandidate(string host, IReadOnlyList<string> candidates) {
        var normalizedHost = (host ?? string.Empty).Trim();
        if (normalizedHost.Length == 0 || candidates is null || candidates.Count == 0) {
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidate = (candidates[i] ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                continue;
            }

            if (ScoreHostHintMatch(normalizedHost, candidate) > 0
                || ScoreHostHintMatch(candidate, normalizedHost) > 0) {
                return true;
            }
        }

        return false;
    }

    internal bool TryBuildDomainIntentHostScopeGuardrailOutputForTesting(string threadId, string userRequest, ToolCall call, out ToolOutputDto output) {
        return TryBuildDomainIntentHostScopeGuardrailOutput(threadId, userRequest, call, out output);
    }

    internal static bool IsDomainIntentHostGuardrailCandidateToolForTesting(string toolName, ToolDefinition? definition = null) {
        return IsDomainIntentHostGuardrailCandidateTool(toolName, definition);
    }
}
