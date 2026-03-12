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
                if (requirements.ForbiddenToolInputValues.Count == 0) {
                    return false;
                }

                foreach (var requirement in requirements.ForbiddenToolInputValues) {
                    var forbiddenMatches = CollectForbiddenScenarioInputMatches(calls, requirement.Key, requirement.Value);
                    if (forbiddenMatches.Count > 0) {
                        return true;
                    }
                }

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

            if (requirements.ForbiddenToolInputValues.Count == 0) {
                return false;
            }

            foreach (var requirement in requirements.ForbiddenToolInputValues) {
                var forbiddenMatches = CollectForbiddenScenarioInputMatches(calls, requirement.Key, requirement.Value);
                if (forbiddenMatches.Count > 0) {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> CollectForbiddenScenarioInputMatches(
            IReadOnlyList<ToolCall> calls,
            string inputKey,
            IReadOnlyCollection<string> forbiddenValues) {
            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (calls.Count == 0 || string.IsNullOrWhiteSpace(inputKey) || forbiddenValues is null || forbiddenValues.Count == 0) {
                return matches;
            }

            var normalizedForbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var forbiddenValue in forbiddenValues) {
                var comparableValues = GetScenarioContractComparableInputValues(inputKey, forbiddenValue);
                for (var i = 0; i < comparableValues.Count; i++) {
                    if (comparableValues[i].Length > 0) {
                        normalizedForbidden.Add(comparableValues[i]);
                    }
                }
            }

            if (normalizedForbidden.Count == 0) {
                return matches;
            }

            var observedValues = CollectDistinctToolInputValuesByKey(calls, inputKey);
            foreach (var observedValue in observedValues) {
                var comparableObservedValues = GetScenarioContractComparableInputValues(inputKey, observedValue);
                var matched = false;
                for (var i = 0; i < comparableObservedValues.Count; i++) {
                    if (!normalizedForbidden.Contains(comparableObservedValues[i])) {
                        continue;
                    }

                    matches.Add(comparableObservedValues[i]);
                    matched = true;
                    break;
                }

                if (!matched) {
                    continue;
                }
            }

            return matches;
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

            var forbiddenToolInputValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (!TryParseScenarioExecutionContractForbiddenInputDirective(request, "forbidden_tool_inputs", forbiddenToolInputValues)) {
                _ = TryParseScenarioExecutionContractForbiddenInputDirective(request, "forbidden_tool_input_values", forbiddenToolInputValues);
            }

            if (forbiddenToolInputValues.Count == 0) {
                var forbiddenInputsMatch = Regex.Match(
                    request,
                    @"Forbidden tool input values:\s*(?<requirements>[^\r\n]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (forbiddenInputsMatch.Success) {
                    ParseScenarioForbiddenInputRequirements(
                        forbiddenInputsMatch.Groups["requirements"].Value,
                        forbiddenToolInputValues,
                        stripTrailingPeriod: true);
                }
            }

            if (minToolCalls <= 0
                && minDistinctToolInputValues.Count == 0
                && requiredTools.Count == 0
                && requiredAnyTools.Count == 0
                && forbiddenToolInputValues.Count == 0) {
                return false;
            }

            var finalizedForbiddenToolInputValues = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in forbiddenToolInputValues) {
                if (pair.Value is null || pair.Value.Count == 0) {
                    continue;
                }

                finalizedForbiddenToolInputValues[pair.Key] = pair.Value
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            requirements = new ScenarioExecutionContractRequirements(
                minToolCalls,
                minDistinctToolInputValues,
                requiredTools,
                requiredAnyTools,
                finalizedForbiddenToolInputValues);
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

        private static bool TryParseScenarioExecutionContractForbiddenInputDirective(
            string userRequest,
            string key,
            Dictionary<string, HashSet<string>> destination) {
            if (destination is null) {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            ParseScenarioForbiddenInputRequirements(rawValue, destination, stripTrailingPeriod: false);
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

        private static void ParseScenarioForbiddenInputRequirements(
            string? rawRequirements,
            Dictionary<string, HashSet<string>> destination,
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
                if (!TrySplitScenarioForbiddenInputRequirement(segment, out var normalizedKey, out var valuesExpression)) {
                    continue;
                }

                var rawValues = valuesExpression
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (rawValues.Length == 0) {
                    continue;
                }

                if (!destination.TryGetValue(normalizedKey, out var forbiddenValues) || forbiddenValues is null) {
                    forbiddenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    destination[normalizedKey] = forbiddenValues;
                }

                foreach (var rawValue in rawValues) {
                    var comparableValues = GetScenarioContractComparableInputValues(normalizedKey, rawValue);
                    for (var valueIndex = 0; valueIndex < comparableValues.Count; valueIndex++) {
                        if (comparableValues[valueIndex].Length > 0) {
                            forbiddenValues.Add(comparableValues[valueIndex]);
                        }
                    }
                }
            }
        }

        private static bool TrySplitScenarioForbiddenInputRequirement(
            string? segment,
            out string key,
            out string valuesExpression) {
            key = string.Empty;
            valuesExpression = string.Empty;

            var rawSegment = (segment ?? string.Empty).Trim();
            if (rawSegment.Length == 0) {
                return false;
            }

            var notEqualsIndex = rawSegment.IndexOf("!=", StringComparison.Ordinal);
            if (notEqualsIndex > 0) {
                key = rawSegment[..notEqualsIndex].Trim();
                valuesExpression = rawSegment[(notEqualsIndex + 2)..].Trim();
            } else {
                var match = Regex.Match(
                    rawSegment,
                    "^(?<key>[^\\s]+)\\s+not(?:\\s*-\\s*|\\s+)in\\s*(?<values>.+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) {
                    return false;
                }

                key = (match.Groups["key"].Value ?? string.Empty).Trim();
                valuesExpression = (match.Groups["values"].Value ?? string.Empty).Trim();
            }

            if (key.Length == 0 || valuesExpression.Length == 0) {
                return false;
            }

            if ((valuesExpression.StartsWith("[", StringComparison.Ordinal) && valuesExpression.EndsWith("]", StringComparison.Ordinal))
                || (valuesExpression.StartsWith("{", StringComparison.Ordinal) && valuesExpression.EndsWith("}", StringComparison.Ordinal))) {
                valuesExpression = valuesExpression.Substring(1, valuesExpression.Length - 2).Trim();
            }

            return key.Length > 0 && valuesExpression.Length > 0;
        }

        private static string NormalizeScenarioContractInputValue(string inputKey, string value) {
            var normalizedKey = (inputKey ?? string.Empty).Trim();
            var normalizedValue = (value ?? string.Empty).Trim();
            if (normalizedKey.Length == 0 || normalizedValue.Length == 0) {
                return string.Empty;
            }

            var aliases = GetScenarioInputKeyAliases(normalizedKey);
            if (aliases.Any(IsHostTargetAlias)) {
                return NormalizeHostTargetCandidate(normalizedValue);
            }

            return normalizedValue;
        }

        private static IReadOnlyList<string> GetScenarioContractComparableInputValues(string inputKey, string value) {
            var normalizedKey = (inputKey ?? string.Empty).Trim();
            var normalizedValue = NormalizeScenarioContractInputValue(normalizedKey, value);
            if (normalizedValue.Length == 0) {
                return Array.Empty<string>();
            }

            var aliases = GetScenarioInputKeyAliases(normalizedKey);
            if (!aliases.Any(IsHostTargetAlias)) {
                return new[] { normalizedValue };
            }

            if (!TryGetHostTargetShortLabel(normalizedValue, out var shortLabel)) {
                return new[] { normalizedValue };
            }

            if (string.Equals(normalizedValue, shortLabel, StringComparison.OrdinalIgnoreCase)) {
                return new[] { normalizedValue };
            }

            return new[] { normalizedValue, shortLabel };
        }

        private static bool TryGetHostTargetShortLabel(string value, out string shortLabel) {
            shortLabel = string.Empty;
            var normalized = NormalizeHostTargetCandidate(value);
            if (normalized.Length == 0) {
                return false;
            }

            var dotIndex = normalized.IndexOf('.');
            if (dotIndex <= 0) {
                shortLabel = normalized;
                return true;
            }

            var candidate = normalized[..dotIndex].Trim();
            if (candidate.Length < 2 || candidate.Length > 128) {
                return false;
            }

            shortLabel = candidate;
            return true;
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
            if (requirements.MinDistinctToolInputValues.Count > 0) {
                foreach (var key in requirements.MinDistinctToolInputValues.Keys) {
                    var aliases = GetScenarioInputKeyAliases(key);
                    if (aliases.Any(IsHostTargetAlias)) {
                        return true;
                    }
                }
            }

            if (requirements.ForbiddenToolInputValues.Count == 0) {
                return false;
            }

            foreach (var key in requirements.ForbiddenToolInputValues.Keys) {
                var aliases = GetScenarioInputKeyAliases(key);
                if (aliases.Any(IsHostTargetAlias)) {
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
            return ToolHostTargeting.ToolSupportsHostTargetInputs(definition);
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
            var forbiddenInputHint = string.Empty;
            if (TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) && requirements is not null) {
                var minCalls = Math.Max(0, requirements.MinToolCalls);
                var distinctParts = new List<string>();
                foreach (var requirement in requirements.MinDistinctToolInputValues) {
                    var observedValues = CollectDistinctToolInputValuesByKey(safeCalls, requirement.Key);
                    distinctParts.Add(requirement.Key + "=" + observedValues.Count + "/" + Math.Max(0, requirement.Value));
                }
                var forbiddenParts = new List<string>();
                foreach (var requirement in requirements.ForbiddenToolInputValues) {
                    var forbiddenValues = requirement.Value ?? Array.Empty<string>();
                    var forbiddenMatches = CollectForbiddenScenarioInputMatches(safeCalls, requirement.Key, forbiddenValues);
                    var normalizedForbiddenValues = forbiddenValues
                        .Select(value => NormalizeScenarioContractInputValue(requirement.Key, value))
                        .Where(static value => value.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (normalizedForbiddenValues.Length == 0) {
                        continue;
                    }

                    forbiddenParts.Add(requirement.Key + "="
                                       + forbiddenMatches.Count
                                       + "/0 blocked (forbidden: "
                                       + string.Join("|", normalizedForbiddenValues)
                                       + ")");
                }

                var requiredAllCount = requirements.RequiredTools.Count;
                var requiredAllMatched = requirements.RequiredTools.Count(pattern => ToolCallSetContainsPattern(safeCalls, pattern));
                var requiredAnyCount = requirements.RequiredAnyTools.Count;
                var requiredAnyMatched = requirements.RequiredAnyTools.Any(pattern => ToolCallSetContainsPattern(safeCalls, pattern)) ? 1 : 0;

                var distinctSummary = distinctParts.Count == 0
                    ? "none"
                    : string.Join(", ", distinctParts);
                var forbiddenSummary = forbiddenParts.Count == 0
                    ? "none"
                    : string.Join(", ", forbiddenParts);
                contractSummary = "min_tool_calls=" + minCalls + ", observed_tool_calls=" + observedToolCalls
                                  + ", required_all=" + requiredAllMatched + "/" + requiredAllCount
                                  + ", required_any=" + requiredAnyMatched + "/" + requiredAnyCount
                                  + ", distinct_inputs={" + distinctSummary + "}"
                                  + ", forbidden_inputs={" + forbiddenSummary + "}";

                var qualifyingPatterns = new List<string>();
                qualifyingPatterns.AddRange(requirements.RequiredTools);
                qualifyingPatterns.AddRange(requirements.RequiredAnyTools);
                if (qualifyingPatterns.Count > 0) {
                    qualifyingPatternHint = string.Join(", ", qualifyingPatterns.Distinct(StringComparer.OrdinalIgnoreCase));
                }

                if (requirements.ForbiddenToolInputValues.Count > 0) {
                    var forbiddenDirectives = new List<string>();
                    foreach (var requirement in requirements.ForbiddenToolInputValues) {
                        var forbiddenValues = (requirement.Value ?? Array.Empty<string>())
                            .Select(value => NormalizeScenarioContractInputValue(requirement.Key, value))
                            .Where(static value => value.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        if (forbiddenValues.Length == 0) {
                            continue;
                        }

                        forbiddenDirectives.Add(requirement.Key + " not-in [" + string.Join("|", forbiddenValues) + "]");
                    }

                    if (forbiddenDirectives.Count > 0) {
                        forbiddenInputHint = string.Join(", ", forbiddenDirectives);
                    }
                }
            }

            return $$"""
                [Execution correction]
                The previous assistant draft ended with partial tool execution that does not satisfy the scenario execution contract.
                Observed tool-call count so far in this turn: {{observedToolCalls}}.
                Contract progress: {{contractSummary}}.
                {{(qualifyingPatternHint.Length == 0 ? string.Empty : "Qualifying tool patterns for this turn: " + qualifyingPatternHint + ".")}}
                {{(forbiddenInputHint.Length == 0 ? string.Empty : "Forbidden tool input values for this turn: " + forbiddenInputHint + ".")}}

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
                Do not use forbidden tool input values specified by the contract; if a drafted call contains one, replace it with an allowed value before execution.
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
