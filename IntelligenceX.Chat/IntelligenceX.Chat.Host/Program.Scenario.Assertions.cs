using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IxJsonArray = IntelligenceX.Json.JsonArray;
using IxJsonObject = IntelligenceX.Json.JsonObject;
using IxJsonValue = IntelligenceX.Json.JsonValue;
using IxJsonValueKind = IntelligenceX.Json.JsonValueKind;
using JsonLite = IntelligenceX.Json.JsonLite;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static List<string> EvaluateScenarioAssertions(ChatScenarioTurn turn, ReplTurnMetricsResult? turnResult) {
        var failures = new List<string>();
        var assistantText = turnResult?.Result.Text ?? string.Empty;
        var toolCalls = turnResult?.Result.ToolCalls ?? Array.Empty<ToolCall>();
        var toolOutputs = turnResult?.Result.ToolOutputs ?? Array.Empty<ToolOutput>();
        var noToolExecutionRetries = turnResult?.Result.NoToolExecutionRetries ?? 0;
        var hasToolContract = TurnHasToolContract(
            turn.MinToolCalls,
            turn.MinToolRounds,
            turn.RequireTools,
            turn.RequireAnyTools,
            turn.MinDistinctToolInputValues,
            turn.ForbidToolInputValues,
            turn.AssertToolOutputContains,
            turn.AssertToolOutputNotContains,
            turn.AssertNoToolErrors,
            turn.ForbidToolErrorCodes);
        var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var toolName = (toolCalls[i].Name ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                toolNames.Add(toolName);
            }
        }
        var (toolErrorCount, toolErrorCodes) = SummarizeToolOutputErrors(toolOutputs);
        var toolCallIdCounts = BuildToolCallIdCounts(toolCalls);
        var toolOutputCallIdCounts = BuildToolOutputCallIdCounts(toolOutputs);
        var toolCallSignatureCounts = BuildToolCallSignatureCounts(toolCalls);

        if (turn.AssertCleanCompletion && ContainsPartialCompletionMarker(assistantText)) {
            failures.Add("Expected clean completion, but assistant output contains partial/transport failure markers.");
        }

        foreach (var expected in turn.AssertContains) {
            if (assistantText.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0) {
                continue;
            }
            failures.Add($"Expected assistant output to contain '{expected}'.");
        }

        if (turn.AssertContainsAny.Count > 0) {
            var matchedAny = false;
            foreach (var expectedAny in turn.AssertContainsAny) {
                if (assistantText.IndexOf(expectedAny, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                matchedAny = true;
                break;
            }

            if (!matchedAny) {
                failures.Add("Expected assistant output to contain at least one of: " + string.Join(", ", turn.AssertContainsAny) + ".");
            }
        }

        foreach (var disallowed in turn.AssertNotContains) {
            if (assistantText.IndexOf(disallowed, StringComparison.OrdinalIgnoreCase) < 0) {
                continue;
            }

            failures.Add($"Expected assistant output to not contain '{disallowed}'.");
        }

        foreach (var pattern in turn.AssertMatchesRegex) {
            try {
                if (Regex.IsMatch(assistantText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                    continue;
                }

                failures.Add($"Expected assistant output to match regex '{pattern}'.");
            } catch (ArgumentException ex) {
                failures.Add($"Invalid regex in assert_matches_regex '{pattern}': {ex.Message}");
            }
        }

        if (turn.AssertNoQuestions && ContainsQuestionSignal(assistantText)) {
            failures.Add("Expected assistant output to not contain question markers.");
        }

        var toolCallsCount = turnResult?.Metrics.ToolCallsCount ?? 0;
        if (turn.MinToolCalls.HasValue && toolCallsCount < turn.MinToolCalls.Value) {
            failures.Add($"Expected at least {turn.MinToolCalls.Value} tool call(s); observed {toolCallsCount}.");
        }

        var toolRounds = turnResult?.Metrics.ToolRounds ?? 0;
        if (turn.MinToolRounds.HasValue && toolRounds < turn.MinToolRounds.Value) {
            failures.Add($"Expected at least {turn.MinToolRounds.Value} tool round(s); observed {toolRounds}.");
        }

        if (turn.MaxPhaseDurationMs.Count > 0) {
            var observedPhaseDurations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var observedPhaseEventCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var phaseTimings = turnResult?.Metrics.PhaseTimings ?? Array.Empty<TurnPhaseTimingDto>();
            for (var i = 0; i < phaseTimings.Count; i++) {
                var phaseTiming = phaseTimings[i];
                if (!TryNormalizeScenarioPhaseName(phaseTiming.Phase, out var normalizedPhase)) {
                    continue;
                }

                observedPhaseDurations[normalizedPhase] = observedPhaseDurations.TryGetValue(normalizedPhase, out var currentDuration)
                    ? currentDuration + Math.Max(0, phaseTiming.DurationMs)
                    : Math.Max(0, phaseTiming.DurationMs);
                observedPhaseEventCounts[normalizedPhase] = observedPhaseEventCounts.TryGetValue(normalizedPhase, out var currentEventCount)
                    ? currentEventCount + Math.Max(0, phaseTiming.EventCount)
                    : Math.Max(0, phaseTiming.EventCount);
            }

            foreach (var phaseLimit in turn.MaxPhaseDurationMs) {
                if (!TryNormalizeScenarioPhaseName(phaseLimit.Key, out var normalizedPhase)) {
                    continue;
                }

                if (!observedPhaseDurations.TryGetValue(normalizedPhase, out var observedDurationMs)) {
                    failures.Add($"Expected phase timing '{normalizedPhase}' to be present for duration guardrail checks.");
                    continue;
                }

                var maxDurationMs = Math.Max(0, phaseLimit.Value);
                if (observedDurationMs <= maxDurationMs) {
                    continue;
                }

                var observedEventCount = observedPhaseEventCounts.TryGetValue(normalizedPhase, out var value)
                    ? value
                    : 0;
                failures.Add(
                    $"Expected phase '{normalizedPhase}' duration <= {maxDurationMs}ms;"
                    + $" observed {observedDurationMs}ms across {observedEventCount} event(s).");
            }
        }

        if (turn.MinDistinctToolInputValues.Count > 0) {
            foreach (var requirement in turn.MinDistinctToolInputValues) {
                var inputKey = requirement.Key;
                var minDistinct = Math.Max(0, requirement.Value);
                if (minDistinct == 0) {
                    continue;
                }

                var observedValues = CollectDistinctToolInputValuesByKey(toolCalls, inputKey);
                if (observedValues.Count >= minDistinct) {
                    continue;
                }

                failures.Add(
                    $"Expected at least {minDistinct} distinct '{inputKey}' tool input value(s); observed {observedValues.Count}."
                    + " Values: " + FormatValuesForAssertion(observedValues.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray()) + ".");
            }
        }

        if (turn.ForbidToolInputValues.Count > 0) {
            foreach (var requirement in turn.ForbidToolInputValues) {
                var inputKey = requirement.Key;
                var forbiddenValues = requirement.Value ?? Array.Empty<string>();
                if (forbiddenValues.Count == 0) {
                    continue;
                }

                var observedValues = CollectDistinctToolInputValuesByKey(toolCalls, inputKey);
                if (observedValues.Count == 0) {
                    continue;
                }

                var normalizedForbiddenValues = forbiddenValues
                    .SelectMany(value => GetScenarioAssertionComparableInputValues(inputKey, value))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (normalizedForbiddenValues.Count == 0) {
                    continue;
                }

                var matchedForbiddenValues = observedValues
                    .SelectMany(value => GetScenarioAssertionComparableInputValues(inputKey, value))
                    .Where(value => value.Length > 0 && normalizedForbiddenValues.Contains(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (matchedForbiddenValues.Length == 0) {
                    continue;
                }

                failures.Add(
                    $"Expected forbidden '{inputKey}' tool input values to be absent, but observed "
                    + FormatValuesForAssertion(matchedForbiddenValues)
                    + ".");
            }
        }

        var effectiveNoToolExecutionRetries = noToolExecutionRetries;
        if (hasToolContract
            && toolCallsCount > 0
            && noToolExecutionRetries > 0) {
            effectiveNoToolExecutionRetries = Math.Max(
                0,
                noToolExecutionRetries - NoToolExecutionRetryToleranceOnSuccessfulToolTurn);
        }

        if (hasToolContract
            && turn.MaxNoToolExecutionRetries.HasValue
            && effectiveNoToolExecutionRetries > turn.MaxNoToolExecutionRetries.Value) {
            failures.Add(
                $"Expected at most {turn.MaxNoToolExecutionRetries.Value} no-tool execution retry attempt(s);"
                + $" observed {noToolExecutionRetries} (effective {effectiveNoToolExecutionRetries} after tolerance {NoToolExecutionRetryToleranceOnSuccessfulToolTurn}).");
        }

        foreach (var requiredTool in turn.RequireTools) {
            if (ToolNameSetContains(toolNames, requiredTool)) {
                continue;
            }

            failures.Add($"Expected tool call '{requiredTool}' (exact or wildcard), but it was not executed.");
        }

        if (turn.RequireAnyTools.Count > 0) {
            var matchedAnyRequiredTool = false;
            foreach (var requiredTool in turn.RequireAnyTools) {
                if (!ToolNameSetContains(toolNames, requiredTool)) {
                    continue;
                }

                matchedAnyRequiredTool = true;
                break;
            }

            if (!matchedAnyRequiredTool) {
                failures.Add("Expected at least one of these tool calls: " + string.Join(", ", turn.RequireAnyTools) + ".");
            }
        }

        foreach (var forbiddenTool in turn.ForbidTools) {
            if (!ToolNameSetContains(toolNames, forbiddenTool)) {
                continue;
            }

            failures.Add($"Expected tool call '{forbiddenTool}' (exact or wildcard) to be absent, but it executed.");
        }

        foreach (var expected in turn.AssertToolOutputContains) {
            if (AnyToolOutputContains(toolOutputs, expected)) {
                continue;
            }

            failures.Add($"Expected tool output to contain '{expected}'.");
        }

        foreach (var disallowed in turn.AssertToolOutputNotContains) {
            if (!AnyToolOutputContains(toolOutputs, disallowed)) {
                continue;
            }

            failures.Add($"Expected tool output to not contain '{disallowed}'.");
        }

        if (turn.AssertNoToolErrors && toolErrorCount > 0) {
            var codeList = toolErrorCodes.Count == 0
                ? "-"
                : string.Join(", ", toolErrorCodes.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase));
            failures.Add($"Expected no tool output errors, but observed {toolErrorCount} error output(s). Codes: {codeList}.");
        }

        foreach (var forbiddenErrorCode in turn.ForbidToolErrorCodes) {
            if (!ToolNameSetContains(toolErrorCodes, forbiddenErrorCode)) {
                continue;
            }

            failures.Add($"Expected tool error code '{forbiddenErrorCode}' (exact or wildcard) to be absent, but it was observed.");
        }

        if (turn.AssertNoDuplicateToolCallIds) {
            var duplicateCallIds = toolCallIdCounts
                .Where(static pair => pair.Value > 1)
                .Select(static pair => pair.Key)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (duplicateCallIds.Length > 0) {
                failures.Add("Expected unique tool call IDs, but duplicates were observed: " + FormatValuesForAssertion(duplicateCallIds) + ".");
            }
        }

        if (turn.AssertNoDuplicateToolOutputCallIds) {
            var duplicateOutputCallIds = toolOutputCallIdCounts
                .Where(static pair => pair.Value > 1)
                .Select(static pair => pair.Key)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (duplicateOutputCallIds.Length > 0) {
                failures.Add("Expected unique tool output call IDs, but duplicates were observed: " + FormatValuesForAssertion(duplicateOutputCallIds) + ".");
            }
        }

        if (turn.AssertToolCallOutputPairing) {
            var callIds = new HashSet<string>(toolCallIdCounts.Keys, StringComparer.OrdinalIgnoreCase);
            var outputCallIds = new HashSet<string>(toolOutputCallIdCounts.Keys, StringComparer.OrdinalIgnoreCase);
            callIds.Remove(string.Empty);
            outputCallIds.Remove(string.Empty);

            var missingOutputs = callIds
                .Where(id => !outputCallIds.Contains(id))
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingOutputs.Length > 0) {
                failures.Add("Expected a tool output for each tool call ID; missing output(s) for: " + FormatValuesForAssertion(missingOutputs) + ".");
            }

            var orphanOutputs = outputCallIds
                .Where(id => !callIds.Contains(id))
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (orphanOutputs.Length > 0) {
                failures.Add("Expected tool outputs to map to in-turn tool calls; orphan output ID(s): " + FormatValuesForAssertion(orphanOutputs) + ".");
            }

            var emptyOutputCallIds = toolOutputs.Count(static output => string.IsNullOrWhiteSpace(output.CallId));
            if (emptyOutputCallIds > 0) {
                failures.Add($"Expected tool output call_id to be present; observed {emptyOutputCallIds} output(s) without call_id.");
            }
        }

        if (turn.MaxDuplicateToolCallSignatures.HasValue) {
            var maxAllowed = Math.Max(0, turn.MaxDuplicateToolCallSignatures.Value);
            var duplicates = toolCallSignatureCounts
                .Where(pair => pair.Value > maxAllowed)
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key} x{pair.Value}")
                .ToArray();
            if (duplicates.Length > 0) {
                failures.Add(
                    $"Expected each tool call signature to appear at most {maxAllowed} time(s), but observed duplicates: "
                    + FormatValuesForAssertion(duplicates) + ".");
            }
        }

        return failures;
    }

    private static bool ContainsPartialCompletionMarker(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var value = text.Trim();
        for (var i = 0; i < PartialCompletionMarkers.Length; i++) {
            if (value.IndexOf(PartialCompletionMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, int> BuildToolCallIdCounts(IReadOnlyList<ToolCall> toolCalls) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var callId = (toolCalls[i].CallId ?? string.Empty).Trim();
            if (!counts.TryGetValue(callId, out var count)) {
                counts[callId] = 1;
                continue;
            }

            counts[callId] = count + 1;
        }

        return counts;
    }

    private static Dictionary<string, int> BuildToolOutputCallIdCounts(IReadOnlyList<ToolOutput> toolOutputs) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolOutputs.Count; i++) {
            var callId = (toolOutputs[i].CallId ?? string.Empty).Trim();
            if (!counts.TryGetValue(callId, out var count)) {
                counts[callId] = 1;
                continue;
            }

            counts[callId] = count + 1;
        }

        return counts;
    }

    private static Dictionary<string, int> BuildToolCallSignatureCounts(IReadOnlyList<ToolCall> toolCalls) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var signature = BuildToolCallSignature(toolCalls[i]);
            if (signature.Length == 0) {
                continue;
            }

            if (!counts.TryGetValue(signature, out var count)) {
                counts[signature] = 1;
                continue;
            }

            counts[signature] = count + 1;
        }

        return counts;
    }

    private static HashSet<string> CollectDistinctToolInputValuesByKey(IReadOnlyList<ToolCall> toolCalls, string inputKey) {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (toolCalls.Count == 0 || string.IsNullOrWhiteSpace(inputKey)) {
            return values;
        }

        var normalizedKey = inputKey.Trim();
        var candidateInputKeys = GetScenarioInputKeyAliases(normalizedKey);
        for (var i = 0; i < toolCalls.Count; i++) {
            var args = toolCalls[i].Arguments;
            if (args is null) {
                continue;
            }

            foreach (var candidateKey in candidateInputKeys) {
                if (!TryReadToolInputValuesByKey(args, candidateKey, out var candidateValues) || candidateValues.Count == 0) {
                    continue;
                }

                foreach (var candidateValue in candidateValues) {
                    values.Add(candidateValue);
                }
            }
        }

        return values;
    }

    private static IReadOnlyList<string> GetScenarioInputKeyAliases(string inputKey) {
        var key = (inputKey ?? string.Empty).Trim();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (key.Length == 0) {
            return Array.Empty<string>();
        }

        aliases.Add(key);
        if (string.Equals(key, "machine_name", StringComparison.OrdinalIgnoreCase)) {
            aliases.Add("domain_controller");
            aliases.Add("servers");
            aliases.Add("targets");
            aliases.Add("target");
            aliases.Add("host");
            aliases.Add("server");
            aliases.Add("computer_name");
        } else if (string.Equals(key, "domain_controller", StringComparison.OrdinalIgnoreCase)) {
            aliases.Add("machine_name");
            aliases.Add("servers");
            aliases.Add("targets");
            aliases.Add("target");
            aliases.Add("host");
            aliases.Add("server");
            aliases.Add("computer_name");
        }

        return aliases.ToArray();
    }

    private static string NormalizeScenarioAssertionInputValue(string inputKey, string value) {
        var normalizedKey = (inputKey ?? string.Empty).Trim();
        var normalizedValue = (value ?? string.Empty).Trim();
        if (normalizedKey.Length == 0 || normalizedValue.Length == 0) {
            return string.Empty;
        }

        var aliases = GetScenarioInputKeyAliases(normalizedKey);
        if (aliases.Any(IsHostTargetAliasForAssertion)) {
            return NormalizeHostTargetCandidateForAssertion(normalizedValue);
        }

        return normalizedValue;
    }

    private static IReadOnlyList<string> GetScenarioAssertionComparableInputValues(string inputKey, string value) {
        var normalized = NormalizeScenarioAssertionInputValue(inputKey, value);
        if (normalized.Length == 0) {
            return Array.Empty<string>();
        }

        var aliases = GetScenarioInputKeyAliases(inputKey);
        if (!aliases.Any(IsHostTargetAliasForAssertion)) {
            return new[] { normalized };
        }

        var dotIndex = normalized.IndexOf('.');
        if (dotIndex <= 0) {
            return new[] { normalized };
        }

        var shortLabel = normalized[..dotIndex].Trim();
        if (shortLabel.Length < 2 || shortLabel.Length > 128) {
            return new[] { normalized };
        }

        return new[] { normalized, shortLabel };
    }

    private static bool IsHostTargetAliasForAssertion(string key) {
        return string.Equals(key, "machine_name", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "domain_controller", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "host", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "server", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "target", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "targets", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "servers", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "computer_name", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHostTargetCandidateForAssertion(string value) {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length < 2 || candidate.Length > 128) {
            return string.Empty;
        }

        if (candidate.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
            return string.Empty;
        }

        return candidate;
    }

    private static bool TryReadToolInputValuesByKey(IxJsonObject arguments, string inputKey, out IReadOnlyList<string> values) {
        values = Array.Empty<string>();
        if (arguments is null || string.IsNullOrWhiteSpace(inputKey)) {
            return false;
        }

        var normalizedKey = inputKey.Trim();
        if (arguments.TryGetValue(normalizedKey, out var exactValue)
            && TryCollectNormalizedToolInputValues(exactValue, out var normalizedValues)) {
            values = normalizedValues;
            return true;
        }

        foreach (var pair in arguments) {
            if (!string.Equals(pair.Key, normalizedKey, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (TryCollectNormalizedToolInputValues(pair.Value, out var aliasNormalizedValues)) {
                values = aliasNormalizedValues;
                return true;
            }
        }

        return false;
    }

    private static bool TryCollectNormalizedToolInputValues(IxJsonValue? value, out IReadOnlyList<string> normalizedValues) {
        normalizedValues = Array.Empty<string>();
        if (value is null) {
            return false;
        }

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectNormalizedToolInputValues(value, values);
        if (values.Count == 0) {
            return false;
        }

        normalizedValues = values.ToArray();
        return true;
    }

    private static void CollectNormalizedToolInputValues(IxJsonValue? value, HashSet<string> target) {
        if (value is null || target is null) {
            return;
        }

        switch (value.Kind) {
            case IxJsonValueKind.String:
                var stringValue = (value.AsString() ?? string.Empty).Trim();
                if (stringValue.Length > 0) {
                    target.Add(stringValue);
                }
                break;
            case IxJsonValueKind.Number:
            case IxJsonValueKind.Boolean:
                var scalarValue = value.ToString().Trim();
                if (scalarValue.Length > 0) {
                    target.Add(scalarValue);
                }
                break;
            case IxJsonValueKind.Array:
                var array = value.AsArray();
                if (array is null) {
                    return;
                }

                foreach (var item in array) {
                    CollectNormalizedToolInputValues(item, target);
                }
                break;
        }
    }

    private static string BuildToolCallSignature(ToolCall call) {
        var toolName = (call.Name ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            return string.Empty;
        }

        var args = SerializeCanonicalToolArguments(call.Arguments);
        return toolName + " " + args;
    }

    private static string SerializeCanonicalToolArguments(IxJsonObject? arguments) {
        if (arguments is null) {
            return "{}";
        }

        var canonical = CanonicalizeJsonObject(arguments);
        return JsonLite.Serialize(canonical);
    }

    private static IxJsonObject CanonicalizeJsonObject(IxJsonObject source) {
        var result = new IxJsonObject(StringComparer.Ordinal);
        var keys = source
            .Select(static pair => pair.Key)
            .OrderBy(static key => key, StringComparer.Ordinal);
        foreach (var key in keys) {
            if (!source.TryGetValue(key, out var value) || value is null) {
                result.Add(key, IxJsonValue.Null);
                continue;
            }

            result.Add(key, CanonicalizeJsonValue(value));
        }

        return result;
    }

    private static IxJsonArray CanonicalizeJsonArray(IxJsonArray source) {
        var result = new IxJsonArray();
        foreach (var value in source) {
            result.Add(CanonicalizeJsonValue(value));
        }
        return result;
    }

    private static IxJsonValue CanonicalizeJsonValue(IxJsonValue value) {
        if (value is null) {
            return IxJsonValue.Null;
        }

        return value.Kind switch {
            IxJsonValueKind.Object => IxJsonValue.From(CanonicalizeJsonObject(value.AsObject() ?? new IxJsonObject(StringComparer.Ordinal))),
            IxJsonValueKind.Array => IxJsonValue.From(CanonicalizeJsonArray(value.AsArray() ?? new IxJsonArray())),
            _ => value
        };
    }

    private static string FormatValuesForAssertion(IReadOnlyList<string> values, int maxItems = 6) {
        if (values.Count == 0) {
            return "-";
        }

        var capped = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(Math.Max(1, maxItems))
            .ToArray();
        if (capped.Length == 0) {
            return "-";
        }

        var suffix = values.Count > capped.Length ? ", ..." : string.Empty;
        return string.Join(", ", capped) + suffix;
    }

    private static bool ToolNameSetContains(IReadOnlyCollection<string> toolNames, string expectedNameOrPattern) {
        if (toolNames.Count == 0) {
            return false;
        }

        var expected = (expectedNameOrPattern ?? string.Empty).Trim();
        if (expected.Length == 0) {
            return false;
        }

        foreach (var toolName in toolNames) {
            if (ToolNameMatches(toolName, expected)) {
                return true;
            }
        }

        return false;
    }

    private static bool ToolNameMatches(string actualToolName, string expectedNameOrPattern) {
        var actual = (actualToolName ?? string.Empty).Trim();
        var expected = (expectedNameOrPattern ?? string.Empty).Trim();
        if (actual.Length == 0 || expected.Length == 0) {
            return false;
        }

        if (!ContainsWildcard(expected)) {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        return WildcardMatches(actual, expected);
    }

    private static bool ContainsWildcard(string value) {
        return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;
    }

    private static bool WildcardMatches(string value, string wildcardPattern) {
        var regexPattern = "^"
            + Regex.Escape(wildcardPattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal)
            + "$";
        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool AnyToolOutputContains(IReadOnlyList<ToolOutput> toolOutputs, string expected) {
        if (toolOutputs.Count == 0 || string.IsNullOrWhiteSpace(expected)) {
            return false;
        }

        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i].Output ?? string.Empty;
            if (output.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsQuestionSignal(string text) {
        var value = text ?? string.Empty;
        return value.IndexOf('?', StringComparison.Ordinal) >= 0
               || value.IndexOf('？', StringComparison.Ordinal) >= 0
               || value.IndexOf('¿', StringComparison.Ordinal) >= 0
               || value.IndexOf('؟', StringComparison.Ordinal) >= 0;
    }

    private static (int ErrorCount, HashSet<string> ErrorCodes) SummarizeToolOutputErrors(IReadOnlyList<ToolOutput> toolOutputs) {
        var errorCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errorCount = 0;
        if (toolOutputs.Count == 0) {
            return (errorCount, errorCodes);
        }

        for (var i = 0; i < toolOutputs.Count; i++) {
            if (!TryReadToolOutputErrorCode(toolOutputs[i].Output, out var errorCode)) {
                continue;
            }

            errorCount++;
            if (errorCode.Length > 0) {
                errorCodes.Add(errorCode);
            }
        }

        return (errorCount, errorCodes);
    }

    private static bool TryReadToolOutputErrorCode(string output, out string errorCode) {
        errorCode = string.Empty;
        if (string.IsNullOrWhiteSpace(output)) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.False) {
                return false;
            }

            if (root.TryGetProperty("error_code", out var errorCodeElement) && errorCodeElement.ValueKind == JsonValueKind.String) {
                errorCode = (errorCodeElement.GetString() ?? string.Empty).Trim();
            }

            return true;
        } catch (JsonException) {
            return false;
        }
    }

}
