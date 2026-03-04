using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text;
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
        private static IReadOnlyList<ToolCall> ApplyScenarioDistinctHostCoverageFallbacks(
            string userRequest,
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            IReadOnlyList<string>? knownHostTargets) {
            if (calls.Count == 0 || toolDefinitions.Count == 0 || knownHostTargets is null || knownHostTargets.Count == 0) {
                return calls;
            }

            if (!TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) || requirements is null) {
                return calls;
            }

            var requiredDistinctHostCoverage = GetRequiredDistinctHostCoverage(requirements);
            var forbiddenHostTargets = GetForbiddenHostTargets(requirements);
            var observedDistinctTargets = CollectDistinctToolInputValuesByKey(calls, "machine_name");
            var observedDistinctAllowedTargets = observedDistinctTargets
                .Where(target => !IsForbiddenHostTarget(target, forbiddenHostTargets))
                .ToArray();
            var hasForbiddenObservedTargets = observedDistinctTargets.Any(target => IsForbiddenHostTarget(target, forbiddenHostTargets));
            var hasMissingDistinctCoverage = requiredDistinctHostCoverage > 1
                                             && observedDistinctAllowedTargets.Length < requiredDistinctHostCoverage;
            if (!hasForbiddenObservedTargets && !hasMissingDistinctCoverage) {
                return calls;
            }

            var orderedAllowedKnownTargets = CollectAllowedKnownHostTargets(knownHostTargets, forbiddenHostTargets);
            if (orderedAllowedKnownTargets.Count == 0) {
                return calls;
            }

            var patchedCalls = calls.ToList();
            var patchedAny = false;
            var callIds = new HashSet<string>(StringComparer.Ordinal);
            for (var callIndex = 0; callIndex < patchedCalls.Count; callIndex++) {
                callIds.Add(patchedCalls[callIndex].CallId ?? string.Empty);
            }

            var hostUsageByTarget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var primaryHostByCallIndex = new Dictionary<int, string>();
            for (var i = 0; i < patchedCalls.Count; i++) {
                if (!TryGetPrimaryHostTargetValue(patchedCalls[i], out var primaryHostTarget)) {
                    continue;
                }

                primaryHostByCallIndex[i] = primaryHostTarget;
                if (!hostUsageByTarget.TryGetValue(primaryHostTarget, out var count)) {
                    hostUsageByTarget[primaryHostTarget] = 1;
                    continue;
                }

                hostUsageByTarget[primaryHostTarget] = count + 1;
            }

            var hasRemainingForbiddenTargets = HasForbiddenHostUsage(hostUsageByTarget, forbiddenHostTargets);
            while ((hasRemainingForbiddenTargets
                    || GetDistinctAllowedHostCoverage(hostUsageByTarget, forbiddenHostTargets) < requiredDistinctHostCoverage)
                   && orderedAllowedKnownTargets.Count > 0) {
                var patchedThisPass = false;
                for (var i = 0; i < patchedCalls.Count; i++) {
                    if (!primaryHostByCallIndex.TryGetValue(i, out var currentHostTarget)
                        || !hostUsageByTarget.TryGetValue(currentHostTarget, out var currentHostUsage)) {
                        continue;
                    }

                    var needsDistinctCoverage = requiredDistinctHostCoverage > 1
                                                && GetDistinctAllowedHostCoverage(hostUsageByTarget, forbiddenHostTargets) < requiredDistinctHostCoverage;
                    var currentIsForbidden = IsForbiddenHostTarget(currentHostTarget, forbiddenHostTargets);
                    if (!currentIsForbidden && !needsDistinctCoverage) {
                        continue;
                    }

                    if (!currentIsForbidden && currentHostUsage <= 1) {
                        continue;
                    }

                    var fallbackTarget = SelectPreferredFallbackTarget(
                        currentHostTarget,
                        orderedAllowedKnownTargets,
                        hostUsageByTarget,
                        requireNewDistinct: needsDistinctCoverage);
                    if (fallbackTarget.Length == 0) {
                        continue;
                    }

                    var originalCall = patchedCalls[i];
                    var definition = FindToolDefinitionByName(toolDefinitions, originalCall.Name);
                    var patchedCall = ApplyHostTargetOverride(originalCall, definition, fallbackTarget);
                    if (ReferenceEquals(patchedCall, originalCall)) {
                        continue;
                    }

                    patchedCalls[i] = patchedCall;
                    primaryHostByCallIndex[i] = fallbackTarget;
                    patchedAny = true;
                    patchedThisPass = true;

                    var updatedCurrentHostUsage = currentHostUsage - 1;
                    if (updatedCurrentHostUsage <= 0) {
                        hostUsageByTarget.Remove(currentHostTarget);
                    } else {
                        hostUsageByTarget[currentHostTarget] = updatedCurrentHostUsage;
                    }

                    if (!hostUsageByTarget.TryGetValue(fallbackTarget, out var fallbackUsage)) {
                        hostUsageByTarget[fallbackTarget] = 1;
                    } else {
                        hostUsageByTarget[fallbackTarget] = fallbackUsage + 1;
                    }

                    hasRemainingForbiddenTargets = HasForbiddenHostUsage(hostUsageByTarget, forbiddenHostTargets);
                }

                if (!patchedThisPass) {
                    break;
                }
            }

            var derivedCallIndex = 0;
            while (requiredDistinctHostCoverage > 1
                   && GetDistinctAllowedHostCoverage(hostUsageByTarget, forbiddenHostTargets) < requiredDistinctHostCoverage) {
                var fallbackTarget = SelectPreferredFallbackTarget(
                    currentHostTarget: string.Empty,
                    orderedAllowedKnownTargets,
                    hostUsageByTarget,
                    requireNewDistinct: true);
                if (fallbackTarget.Length == 0) {
                    break;
                }

                var appended = false;
                for (var i = 0; i < patchedCalls.Count; i++) {
                    var templateCall = patchedCalls[i];
                    var definition = FindToolDefinitionByName(toolDefinitions, templateCall.Name);
                    var derivedCall = TryCreateDerivedHostOverrideCall(
                        templateCall,
                        definition,
                        fallbackTarget,
                        callIds,
                        ref derivedCallIndex);
                    if (derivedCall is null) {
                        continue;
                    }

                    patchedCalls.Add(derivedCall);
                    hostUsageByTarget[fallbackTarget] = hostUsageByTarget.TryGetValue(fallbackTarget, out var current)
                        ? current + 1
                        : 1;
                    patchedAny = true;
                    appended = true;
                    break;
                }

                if (!appended) {
                    break;
                }
            }

            return patchedAny ? patchedCalls.ToArray() : calls;
        }

        private static int GetDistinctAllowedHostCoverage(
            IReadOnlyDictionary<string, int> hostUsageByTarget,
            ISet<string> forbiddenHostTargets) {
            if (hostUsageByTarget.Count == 0) {
                return 0;
            }

            var count = 0;
            foreach (var pair in hostUsageByTarget) {
                if (pair.Value <= 0 || IsForbiddenHostTarget(pair.Key, forbiddenHostTargets)) {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool HasForbiddenHostUsage(
            IReadOnlyDictionary<string, int> hostUsageByTarget,
            ISet<string> forbiddenHostTargets) {
            if (forbiddenHostTargets.Count == 0 || hostUsageByTarget.Count == 0) {
                return false;
            }

            foreach (var pair in hostUsageByTarget) {
                if (pair.Value <= 0 || !IsForbiddenHostTarget(pair.Key, forbiddenHostTargets)) {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static string SelectPreferredFallbackTarget(
            string currentHostTarget,
            IReadOnlyList<string> orderedAllowedKnownTargets,
            IReadOnlyDictionary<string, int> hostUsageByTarget,
            bool requireNewDistinct) {
            var normalizedCurrentHostTarget = NormalizeHostTargetCandidate(currentHostTarget);
            if (orderedAllowedKnownTargets.Count == 0) {
                return string.Empty;
            }

            for (var i = 0; i < orderedAllowedKnownTargets.Count; i++) {
                var candidate = orderedAllowedKnownTargets[i];
                if (candidate.Length == 0) {
                    continue;
                }

                if (normalizedCurrentHostTarget.Length > 0
                    && string.Equals(candidate, normalizedCurrentHostTarget, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var inUse = hostUsageByTarget.TryGetValue(candidate, out var usage) && usage > 0;
                if (requireNewDistinct && inUse) {
                    continue;
                }

                return candidate;
            }

            if (requireNewDistinct) {
                return string.Empty;
            }

            for (var i = 0; i < orderedAllowedKnownTargets.Count; i++) {
                var candidate = orderedAllowedKnownTargets[i];
                if (candidate.Length == 0) {
                    continue;
                }

                if (normalizedCurrentHostTarget.Length > 0
                    && string.Equals(candidate, normalizedCurrentHostTarget, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                return candidate;
            }

            return string.Empty;
        }

        private static List<string> CollectAllowedKnownHostTargets(
            IReadOnlyList<string> knownHostTargets,
            ISet<string> forbiddenHostTargets) {
            var allowedTargets = new List<string>();
            var seenAllowedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < knownHostTargets.Count; i++) {
                var normalized = NormalizeHostTargetCandidate(knownHostTargets[i]);
                if (normalized.Length == 0
                    || IsForbiddenHostTarget(normalized, forbiddenHostTargets)
                    || !seenAllowedTargets.Add(normalized)) {
                    continue;
                }

                allowedTargets.Add(normalized);
            }

            return OrderHostTargetCandidatesBySpecificity(allowedTargets);
        }

        private static HashSet<string> GetForbiddenHostTargets(ScenarioExecutionContractRequirements requirements) {
            var forbiddenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (requirements is null || requirements.ForbiddenToolInputValues.Count == 0) {
                return forbiddenTargets;
            }

            foreach (var requirement in requirements.ForbiddenToolInputValues) {
                var aliases = GetScenarioInputKeyAliases(requirement.Key);
                if (!aliases.Any(IsHostTargetAlias)) {
                    continue;
                }

                var forbiddenValues = requirement.Value ?? Array.Empty<string>();
                foreach (var forbiddenValue in forbiddenValues) {
                    var comparableValues = GetScenarioContractComparableInputValues(requirement.Key, forbiddenValue);
                    for (var valueIndex = 0; valueIndex < comparableValues.Count; valueIndex++) {
                        if (comparableValues[valueIndex].Length > 0) {
                            forbiddenTargets.Add(comparableValues[valueIndex]);
                        }
                    }
                }
            }

            return forbiddenTargets;
        }

        private static bool IsForbiddenHostTarget(string hostTarget, ISet<string> forbiddenHostTargets) {
            if (forbiddenHostTargets is null || forbiddenHostTargets.Count == 0) {
                return false;
            }

            var comparableValues = GetScenarioContractComparableInputValues("machine_name", hostTarget);
            for (var i = 0; i < comparableValues.Count; i++) {
                if (forbiddenHostTargets.Contains(comparableValues[i])) {
                    return true;
                }
            }

            return false;
        }

        private static ToolDefinition? FindToolDefinitionByName(
            IReadOnlyList<ToolDefinition> toolDefinitions,
            string? toolName) {
            var normalizedToolName = (toolName ?? string.Empty).Trim();
            if (normalizedToolName.Length == 0) {
                return null;
            }

            for (var definitionIndex = 0; definitionIndex < toolDefinitions.Count; definitionIndex++) {
                var candidateName = (toolDefinitions[definitionIndex].Name ?? string.Empty).Trim();
                if (string.Equals(candidateName, normalizedToolName, StringComparison.OrdinalIgnoreCase)) {
                    return toolDefinitions[definitionIndex];
                }
            }

            return null;
        }

        private static bool TryGetPrimaryHostTargetValue(ToolCall call, out string value) {
            value = string.Empty;
            if (call.Arguments is null) {
                return false;
            }

            var candidateInputKeys = GetScenarioInputKeyAliases("machine_name");
            for (var keyIndex = 0; keyIndex < candidateInputKeys.Count; keyIndex++) {
                if (!TryReadToolInputValuesByKey(call.Arguments, candidateInputKeys[keyIndex], out var values) || values.Count == 0) {
                    continue;
                }

                for (var valueIndex = 0; valueIndex < values.Count; valueIndex++) {
                    var normalized = NormalizeHostTargetCandidate(values[valueIndex]);
                    if (normalized.Length == 0) {
                        continue;
                    }

                    value = normalized;
                    return true;
                }
            }

            return false;
        }

        private static ToolCall? TryCreateDerivedHostOverrideCall(
            ToolCall templateCall,
            ToolDefinition? definition,
            string hostTarget,
            ISet<string> existingCallIds,
            ref int derivedCallIndex) {
            var patchedCall = ApplyHostTargetOverride(templateCall, definition, hostTarget);
            if (ReferenceEquals(patchedCall, templateCall)) {
                return null;
            }

            var baseCallId = string.IsNullOrWhiteSpace(templateCall.CallId)
                ? "call"
                : templateCall.CallId.Trim();
            while (true) {
                derivedCallIndex++;
                var candidateId = baseCallId + "_hostcov_" + derivedCallIndex.ToString(CultureInfo.InvariantCulture);
                if (!existingCallIds.Add(candidateId)) {
                    continue;
                }

                return new ToolCall(
                    candidateId,
                    patchedCall.Name,
                    patchedCall.Input,
                    patchedCall.Arguments,
                    patchedCall.Raw);
            }
        }

        private static int GetRequiredDistinctHostCoverage(ScenarioExecutionContractRequirements requirements) {
            if (requirements is null || requirements.MinDistinctToolInputValues.Count == 0) {
                return 0;
            }

            var requiredDistinctHostCoverage = 0;
            foreach (var requirement in requirements.MinDistinctToolInputValues) {
                var requiredDistinct = Math.Max(0, requirement.Value);
                if (requiredDistinct <= 1) {
                    continue;
                }

                var aliases = GetScenarioInputKeyAliases(requirement.Key);
                if (!aliases.Any(IsHostTargetAlias)) {
                    continue;
                }

                requiredDistinctHostCoverage = Math.Max(requiredDistinctHostCoverage, requiredDistinct);
            }

            return requiredDistinctHostCoverage;
        }

        private static bool IsHostTargetAlias(string key) {
            return string.Equals(key, "machine_name", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "domain_controller", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "host", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "server", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "target", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "targets", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "servers", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "computer_name", StringComparison.OrdinalIgnoreCase);
        }

        private static ToolCall ApplyHostTargetOverride(ToolCall call, ToolDefinition? definition, string hostTarget) {
            if (call.Arguments is null) {
                return call;
            }

            var normalizedTarget = NormalizeHostTargetCandidate(hostTarget);
            if (normalizedTarget.Length == 0) {
                return call;
            }

            if (!TryPickHostTargetInputKey(call, definition, out var targetKey, out var keyIsArray)) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            var replaced = false;
            foreach (var pair in call.Arguments) {
                if (!IsHostTargetAlias(pair.Key)) {
                    rewrittenArguments.Add(pair.Key, pair.Value ?? JsonValue.Null);
                    continue;
                }

                // Keep host-target aliases internally consistent within the same call. If any fallback
                // host is applied, every present host/DC alias should resolve to that same host target.
                AddHostTargetValue(
                    rewrittenArguments,
                    pair.Key,
                    normalizedTarget,
                    asArray: pair.Value?.AsArray() is not null);
                replaced = true;
            }

            if (!replaced) {
                AddHostTargetValue(rewrittenArguments, targetKey, normalizedTarget, keyIsArray);
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static bool TryPickHostTargetInputKey(
            ToolCall call,
            ToolDefinition? definition,
            out string key,
            out bool keyIsArray) {
            key = string.Empty;
            keyIsArray = false;
            if (call.Arguments is null) {
                return false;
            }

            var preferredKeys = new[] {
                "machine_name",
                "domain_controller",
                "host",
                "server",
                "computer_name",
                "target",
                "targets",
                "servers"
            };

            for (var preferredKeyIndex = 0; preferredKeyIndex < preferredKeys.Length; preferredKeyIndex++) {
                var preferredKey = preferredKeys[preferredKeyIndex];
                foreach (var pair in call.Arguments) {
                    if (!string.Equals(pair.Key, preferredKey, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    key = pair.Key;
                    keyIsArray = pair.Value?.AsArray() is not null;
                    return true;
                }
            }

            if (definition is null) {
                return false;
            }

            for (var preferredKeyIndex = 0; preferredKeyIndex < preferredKeys.Length; preferredKeyIndex++) {
                var preferredKey = preferredKeys[preferredKeyIndex];
                if (!ToolDefinitionHasInputProperty(definition, preferredKey)) {
                    continue;
                }

                key = preferredKey;
                keyIsArray = string.Equals(preferredKey, "targets", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(preferredKey, "servers", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            return false;
        }

        private static void AddHostTargetValue(JsonObject arguments, string key, string value, bool asArray) {
            if (!asArray) {
                arguments.Add(key, value);
                return;
            }

            var array = new JsonArray();
            array.Add(value);
            arguments.Add(key, array);
        }

        private static ToolCall ApplyKnownHostTargetFallbacks(
            ToolCall call,
            ToolDefinition? definition,
            IReadOnlyList<string>? knownHostTargets) {
            if (definition is null || knownHostTargets is null || knownHostTargets.Count == 0 || call.Arguments is null) {
                return call;
            }

            if (!ToolDefinitionSupportsHostTargetInputs(definition)) {
                return call;
            }

            var candidateInputKeys = GetScenarioInputKeyAliases("machine_name");
            for (var keyIndex = 0; keyIndex < candidateInputKeys.Count; keyIndex++) {
                if (!TryReadToolInputValuesByKey(call.Arguments, candidateInputKeys[keyIndex], out var values) || values.Count == 0) {
                    continue;
                }

                return call;
            }

            var supportsTarget = ToolDefinitionHasInputProperty(definition, "target");
            var supportsTargets = ToolDefinitionHasInputProperty(definition, "targets");
            if (!supportsTarget && !supportsTargets) {
                return call;
            }

            var normalizedTargets = new List<string>(Math.Min(MaxAutoFilledToolTargets, knownHostTargets.Count));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < knownHostTargets.Count && normalizedTargets.Count < MaxAutoFilledToolTargets; i++) {
                var normalized = NormalizeHostTargetCandidate(knownHostTargets[i]);
                if (normalized.Length == 0 || !seen.Add(normalized)) {
                    continue;
                }

                normalizedTargets.Add(normalized);
            }

            if (normalizedTargets.Count == 0) {
                return call;
            }

            normalizedTargets = OrderHostTargetCandidatesBySpecificity(normalizedTargets);
            var patchedArguments = new JsonObject(StringComparer.Ordinal);
            foreach (var pair in call.Arguments) {
                patchedArguments.Add(pair.Key, pair.Value);
            }

            if (supportsTarget) {
                patchedArguments.Add("target", normalizedTargets[0]);
            }

            if (supportsTargets) {
                var targetsArray = new JsonArray();
                for (var i = 0; i < normalizedTargets.Count; i++) {
                    targetsArray.Add(normalizedTargets[i]);
                }

                patchedArguments.Add("targets", targetsArray);
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(patchedArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, patchedArguments, call.Raw);
        }

        private static bool ToolDefinitionHasInputProperty(ToolDefinition definition, string key) {
            if (definition is null || string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            var properties = definition.Parameters?.GetObject("properties");
            if (properties is null) {
                return false;
            }

            foreach (var pair in properties) {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

    }
}
