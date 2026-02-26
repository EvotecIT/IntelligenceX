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
        private static bool ShouldRetryScenarioContractRepair(string userRequest, IReadOnlyList<ToolCall> calls) {
            if (!TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) || requirements is null) {
                return false;
            }

            if (requirements.MinToolCalls > 0 && calls.Count < requirements.MinToolCalls) {
                return true;
            }

            if (requirements.RequiredTools.Count > 0) {
                foreach (var requiredPattern in requirements.RequiredTools) {
                    if (!ToolCallSetContainsPattern(calls, requiredPattern)) {
                        return true;
                    }
                }
            }

            if (requirements.RequiredAnyTools.Count > 0) {
                var matchedAnyRequired = false;
                foreach (var requiredPattern in requirements.RequiredAnyTools) {
                    if (!ToolCallSetContainsPattern(calls, requiredPattern)) {
                        continue;
                    }

                    matchedAnyRequired = true;
                    break;
                }

                if (!matchedAnyRequired) {
                    return true;
                }
            }

            if (requirements.MinDistinctToolInputValues.Count == 0) {
                return false;
            }

            foreach (var requirement in requirements.MinDistinctToolInputValues) {
                var minDistinct = Math.Max(0, requirement.Value);
                if (minDistinct == 0) {
                    continue;
                }

                var observedValues = CollectDistinctToolInputValuesByKey(calls, requirement.Key);
                if (observedValues.Count < minDistinct) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseScenarioExecutionContractRequirements(string userRequest, out ScenarioExecutionContractRequirements? requirements) {
            requirements = null;
            var request = userRequest ?? string.Empty;
            if (request.IndexOf(ScenarioExecutionContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
                return false;
            }

            var minToolCalls = 0;
            if (TryParseScenarioExecutionContractIntDirective(request, "min_tool_calls", out var parsedStructuredMinToolCalls)
                && parsedStructuredMinToolCalls > 0) {
                minToolCalls = parsedStructuredMinToolCalls;
            } else {
                var minToolCallsMatch = Regex.Match(
                    request,
                    @"Minimum tool calls in this turn:\s*(?<count>\d+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (minToolCallsMatch.Success
                    && int.TryParse(minToolCallsMatch.Groups["count"].Value, out var parsedMinToolCalls)
                    && parsedMinToolCalls > 0) {
                    minToolCalls = parsedMinToolCalls;
                }
            }

            var minDistinctToolInputValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<string> requiredTools;
            if (!TryParseScenarioExecutionContractToolPatternsDirective(request, "required_tools_all", out requiredTools)) {
                requiredTools = ParseScenarioContractToolPatterns(
                    request,
                    @"Required tool calls \(all\):\s*(?<patterns>[^\r\n]+)");
            }

            IReadOnlyList<string> requiredAnyTools;
            if (!TryParseScenarioExecutionContractToolPatternsDirective(request, "required_tools_any", out requiredAnyTools)) {
                requiredAnyTools = ParseScenarioContractToolPatterns(
                    request,
                    @"Required tool calls \(at least one\):\s*(?<patterns>[^\r\n]+)");
            }

            if (!TryParseScenarioExecutionContractDistinctInputDirective(request, "distinct_tool_inputs", minDistinctToolInputValues)) {
                var distinctMatch = Regex.Match(
                    request,
                    @"Distinct tool input value requirements:\s*(?<requirements>[^\r\n]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (distinctMatch.Success) {
                    ParseScenarioDistinctInputRequirements(distinctMatch.Groups["requirements"].Value, minDistinctToolInputValues, stripTrailingPeriod: true);
                }
            }

            if (minToolCalls <= 0
                && minDistinctToolInputValues.Count == 0
                && requiredTools.Count == 0
                && requiredAnyTools.Count == 0) {
                return false;
            }

            requirements = new ScenarioExecutionContractRequirements(
                minToolCalls,
                minDistinctToolInputValues,
                requiredTools,
                requiredAnyTools);
            return true;
        }

        private static bool TryParseScenarioExecutionContractBoolDirective(string userRequest, string key, out bool value) {
            value = false;
            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            var normalized = (rawValue ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                return false;
            }

            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "1", StringComparison.Ordinal)) {
                value = true;
                return true;
            }

            if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "0", StringComparison.Ordinal)) {
                value = false;
                return true;
            }

            return false;
        }

        private static bool TryParseScenarioExecutionContractIntDirective(string userRequest, string key, out int value) {
            value = 0;
            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            return int.TryParse((rawValue ?? string.Empty).Trim(), out value);
        }

        private static bool TryParseScenarioExecutionContractToolPatternsDirective(string userRequest, string key, out IReadOnlyList<string> patterns) {
            patterns = Array.Empty<string>();
            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            patterns = ParseScenarioContractCsvPatterns(rawValue);
            return true;
        }

        private static bool TryParseScenarioExecutionContractDistinctInputDirective(
            string userRequest,
            string key,
            Dictionary<string, int> destination) {
            if (destination is null) {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            ParseScenarioDistinctInputRequirements(rawValue, destination, stripTrailingPeriod: false);
            return true;
        }

        private static bool TryReadScenarioExecutionContractDirectiveValue(string userRequest, string key, out string value) {
            value = string.Empty;
            var request = userRequest ?? string.Empty;
            if (request.Length == 0 || string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            var markerIndex = request.IndexOf(ScenarioExecutionContractDirectiveMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) {
                return false;
            }

            var tail = request[(markerIndex + ScenarioExecutionContractDirectiveMarker.Length)..];
            var lines = tail.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                var line = (lines[i] ?? string.Empty).Trim();
                if (line.Length == 0) {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {
                    break;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0) {
                    continue;
                }

                var candidateKey = line[..separator].Trim();
                if (!string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                value = line[(separator + 1)..].Trim();
                return true;
            }

            return false;
        }

        private static IReadOnlyList<string> ParseScenarioContractCsvPatterns(string? rawPatterns) {
            var raw = (rawPatterns ?? string.Empty).Trim();
            if (raw.Length == 0 || string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) {
                return Array.Empty<string>();
            }

            var parsed = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parsed.Length == 0 ? Array.Empty<string>() : parsed;
        }

        private static void ParseScenarioDistinctInputRequirements(
            string? rawRequirements,
            Dictionary<string, int> destination,
            bool stripTrailingPeriod) {
            if (destination is null) {
                throw new ArgumentNullException(nameof(destination));
            }

            var raw = (rawRequirements ?? string.Empty).Trim();
            if (stripTrailingPeriod && raw.EndsWith(".", StringComparison.Ordinal)) {
                raw = raw.Substring(0, raw.Length - 1).TrimEnd();
            }

            if (raw.Length == 0 || string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            var segments = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments) {
                var pair = segment.Split(">=", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (pair.Length != 2) {
                    continue;
                }

                var key = (pair[0] ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                if (!int.TryParse(pair[1], out var parsedMinDistinct) || parsedMinDistinct < 0) {
                    continue;
                }

                destination[key] = parsedMinDistinct;
            }
        }

        private static IReadOnlyList<string> ParseScenarioContractToolPatterns(string request, string pattern) {
            var match = Regex.Match(request ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) {
                return Array.Empty<string>();
            }

            var rawPatterns = (match.Groups["patterns"].Value ?? string.Empty).Trim();
            if (rawPatterns.EndsWith(".", StringComparison.Ordinal)) {
                rawPatterns = rawPatterns.Substring(0, rawPatterns.Length - 1).TrimEnd();
            }

            return ParseScenarioContractCsvPatterns(rawPatterns);
        }

        private static bool ToolCallSetContainsPattern(IReadOnlyList<ToolCall> calls, string pattern) {
            var candidatePattern = (pattern ?? string.Empty).Trim();
            if (candidatePattern.Length == 0) {
                return false;
            }

            for (var i = 0; i < calls.Count; i++) {
                var toolName = (calls[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                if (PatternMatchesToolName(candidatePattern, toolName)) {
                    return true;
                }
            }

            return false;
        }

        private static bool PatternMatchesToolName(string pattern, string toolName) {
            var expected = (pattern ?? string.Empty).Trim();
            var actual = (toolName ?? string.Empty).Trim();
            if (expected.Length == 0 || actual.Length == 0) {
                return false;
            }

            if (string.Equals(expected, "*", StringComparison.Ordinal)) {
                return true;
            }

            var hasWildcard = expected.IndexOf('*') >= 0 || expected.IndexOf('?') >= 0;
            if (!hasWildcard) {
                return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
            }

            var regexPattern = "^"
                               + Regex.Escape(expected)
                                   .Replace("\\*", ".*", StringComparison.Ordinal)
                                   .Replace("\\?", ".", StringComparison.Ordinal)
                               + "$";
            return Regex.IsMatch(actual, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string BuildKnownHostTargetHint(IReadOnlyList<string>? knownHostTargets) {
            if (knownHostTargets is null || knownHostTargets.Count == 0) {
                return string.Empty;
            }

            var values = new List<string>(Math.Min(MaxRetryPromptHostTargets, knownHostTargets.Count));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < knownHostTargets.Count && values.Count < MaxRetryPromptHostTargets; i++) {
                var candidate = NormalizeHostTargetCandidate(knownHostTargets[i]);
                if (candidate.Length == 0 || !seen.Add(candidate)) {
                    continue;
                }

                values.Add(candidate);
            }

            if (values.Count == 0) {
                return string.Empty;
            }

            return "Known host/DC targets from prior tool inputs in this thread: "
                   + string.Join(", ", values)
                   + ".";
        }

        private static string BuildForcedToolHint(string? forcedToolName) {
            var toolName = (forcedToolName ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                return string.Empty;
            }

            return "Use tool '" + toolName + "' first in this retry before any narrative text.";
        }

        private static string? ResolveScenarioRepairForcedToolName(
            string userRequest,
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            int retryAttempt) {
            if (retryAttempt < ScenarioForcedToolChoiceRetryThreshold || toolDefinitions.Count == 0) {
                return null;
            }

            if (!TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) || requirements is null) {
                return null;
            }

            var patterns = new List<string>();
            if (requirements.RequiredTools.Count > 0) {
                foreach (var pattern in requirements.RequiredTools) {
                    if (ToolCallSetContainsPattern(calls, pattern)) {
                        continue;
                    }

                    patterns.Add(pattern);
                }
            }

            if (requirements.RequiredAnyTools.Count > 0) {
                var anyMatched = requirements.RequiredAnyTools.Any(pattern => ToolCallSetContainsPattern(calls, pattern));
                if (!anyMatched) {
                    patterns.AddRange(requirements.RequiredAnyTools);
                }
            }

            if (patterns.Count == 0) {
                return null;
            }

            var requiresHostTargetInputs = RequirementsNeedHostTargetCoverage(requirements);
            foreach (var pattern in patterns.Distinct(StringComparer.OrdinalIgnoreCase)) {
                var preferred = FindMatchingForcedToolName(
                    pattern: pattern,
                    toolDefinitions: toolDefinitions,
                    requireHostTargetInputs: requiresHostTargetInputs);
                if (!string.IsNullOrWhiteSpace(preferred)) {
                    return preferred;
                }
            }

            return null;
        }

        private static bool RequirementsNeedHostTargetCoverage(ScenarioExecutionContractRequirements requirements) {
            if (requirements.MinDistinctToolInputValues.Count == 0) {
                return false;
            }

            foreach (var key in requirements.MinDistinctToolInputValues.Keys) {
                var aliases = GetScenarioInputKeyAliases(key);
                if (aliases.Any(alias => string.Equals(alias, "machine_name", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "domain_controller", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "host", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "server", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "target", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "computer_name", StringComparison.OrdinalIgnoreCase))) {
                    return true;
                }
            }

            return false;
        }

        private static string? FindMatchingForcedToolName(
            string pattern,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            bool requireHostTargetInputs) {
            for (var i = 0; i < toolDefinitions.Count; i++) {
                var definition = toolDefinitions[i];
                var toolName = (definition.Name ?? string.Empty).Trim();
                if (toolName.Length == 0 || !PatternMatchesToolName(pattern, toolName)) {
                    continue;
                }

                if (requireHostTargetInputs && !ToolDefinitionSupportsHostTargetInputs(definition)) {
                    continue;
                }

                return toolName;
            }

            if (!requireHostTargetInputs) {
                return null;
            }

            // Host-target requirement is a preference for contract recovery; fallback to any matching
            // tool to avoid deadlocking when schema metadata is incomplete.
            for (var i = 0; i < toolDefinitions.Count; i++) {
                var toolName = (toolDefinitions[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                if (PatternMatchesToolName(pattern, toolName)) {
                    return toolName;
                }
            }

            return null;
        }

        private static bool ToolDefinitionSupportsHostTargetInputs(ToolDefinition definition) {
            var properties = definition.Parameters?.GetObject("properties");
            if (properties is null) {
                return false;
            }

            var candidateKeys = GetScenarioInputKeyAliases("machine_name");
            if (candidateKeys.Count == 0) {
                return false;
            }

            for (var keyIndex = 0; keyIndex < candidateKeys.Count; keyIndex++) {
                var key = candidateKeys[keyIndex];
                if (properties.GetObject(key) is not null) {
                    return true;
                }

                foreach (var pair in properties) {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildScenarioContractRepairRetryPrompt(
            string userRequest,
            string assistantDraft,
            IReadOnlyList<ToolCall> calls,
            int retryAttempt,
            IReadOnlyList<string>? knownHostTargets = null,
            string? forcedToolName = null) {
            var request = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
            var draft = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
            _ = retryAttempt;
            var safeCalls = calls ?? Array.Empty<ToolCall>();
            var observedToolCalls = Math.Max(0, safeCalls.Count);
            var knownHostHint = BuildKnownHostTargetHint(knownHostTargets);
            var forcedToolHint = BuildForcedToolHint(forcedToolName);

            var contractSummary = "unable to parse scenario requirements.";
            var qualifyingPatternHint = string.Empty;
            if (TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) && requirements is not null) {
                var minCalls = Math.Max(0, requirements.MinToolCalls);
                var distinctParts = new List<string>();
                foreach (var requirement in requirements.MinDistinctToolInputValues) {
                    var observedValues = CollectDistinctToolInputValuesByKey(safeCalls, requirement.Key);
                    distinctParts.Add(requirement.Key + "=" + observedValues.Count + "/" + Math.Max(0, requirement.Value));
                }

                var requiredAllCount = requirements.RequiredTools.Count;
                var requiredAllMatched = requirements.RequiredTools.Count(pattern => ToolCallSetContainsPattern(safeCalls, pattern));
                var requiredAnyCount = requirements.RequiredAnyTools.Count;
                var requiredAnyMatched = requirements.RequiredAnyTools.Any(pattern => ToolCallSetContainsPattern(safeCalls, pattern)) ? 1 : 0;

                var distinctSummary = distinctParts.Count == 0
                    ? "none"
                    : string.Join(", ", distinctParts);
                contractSummary = "min_tool_calls=" + minCalls + ", observed_tool_calls=" + observedToolCalls
                                  + ", required_all=" + requiredAllMatched + "/" + requiredAllCount
                                  + ", required_any=" + requiredAnyMatched + "/" + requiredAnyCount
                                  + ", distinct_inputs={" + distinctSummary + "}";

                var qualifyingPatterns = new List<string>();
                qualifyingPatterns.AddRange(requirements.RequiredTools);
                qualifyingPatterns.AddRange(requirements.RequiredAnyTools);
                if (qualifyingPatterns.Count > 0) {
                    qualifyingPatternHint = string.Join(", ", qualifyingPatterns.Distinct(StringComparer.OrdinalIgnoreCase));
                }
            }

            return $$"""
                [Execution correction]
                The previous assistant draft ended with partial tool execution that does not satisfy the scenario execution contract.
                Observed tool-call count so far in this turn: {{observedToolCalls}}.
                Contract progress: {{contractSummary}}.
                {{(qualifyingPatternHint.Length == 0 ? string.Empty : "Qualifying tool patterns for this turn: " + qualifyingPatternHint + ".")}}

                User request:
                {{request}}

                Previous assistant draft:
                {{draft}}

                Execute additional qualifying tool calls now in this same turn to satisfy the missing contract requirements.
                Do not ask follow-up questions before issuing additional tool calls.
                Emit at least one qualifying tool call before any narrative prose in this retry.
                {{forcedToolHint}}
                If required coverage is >1 (for example min_tool_calls>=2 or machine_name>=2), issue multiple tool calls in this retry.
                Do not repeat identical tool-call signatures unless there is no alternative; prioritize missing distinct input coverage.
                Infer missing read-only inputs from prior tool outputs where possible.
                For continuation requests over remaining discovered DCs/hosts, execute calls across at least two distinct host/DC inputs.
                If current discovery returns zero hosts, use previously seen DC/host targets from this thread as fallback and proceed with best-effort execution.
                {{knownHostHint}}
                Do not claim internal retry/exhaustion limits; this is an internal execution correction path.
                If tools still cannot satisfy the missing contract requirements after best effort, state the exact blocker once.
                """;
        }

    }
}
