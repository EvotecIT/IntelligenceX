using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string AutomaticAlternateEngineSelectorValue = "auto";

    private bool TryBuildPreferredHealthyAlternateEngineCall(
        string threadId,
        ToolCall call,
        ToolDefinition? definition,
        ToolRetryProfile profile,
        out ToolCall preferredCall,
        out string selectedEngineId) {
        preferredCall = call;
        selectedEngineId = string.Empty;

        if (definition?.Parameters is null || profile.AlternateEngineIds.Count == 0) {
            return false;
        }

        if (!TryResolveAlternateEngineSelectorArgumentName(definition, out var selectorArgumentName, out var selectorProperty)) {
            return false;
        }

        if (!TryParseToolCallArgumentsFromInput(call.Input, out var parsedInputArguments)) {
            parsedInputArguments = null!;
        }

        var sourceArguments = call.Arguments ?? parsedInputArguments;
        var currentEngineId = ReadAlternateEngineSelectorValue(sourceArguments, selectorArgumentName);
        if (!IsImplicitAlternateEngineSelection(currentEngineId)) {
            return false;
        }

        var allowedSelectorValues = ReadAllowedAlternateEngineSelectorValues(selectorProperty);
        if (!TrySelectPreferredHealthyAlternateEngineId(
                threadId,
                call.Name,
                profile.AlternateEngineIds,
                allowedSelectorValues,
                currentEngineId,
                out selectedEngineId)) {
            return false;
        }

        var preferredArguments = CloneToolArguments(sourceArguments);
        preferredArguments[selectorArgumentName] = JsonValue.From(selectedEngineId);
        var preferredInput = JsonLite.Serialize(preferredArguments);
        preferredCall = new ToolCall(call.CallId, call.Name, preferredInput, preferredArguments, call.Raw);
        return true;
    }

    private bool TryBuildAlternateEngineFallbackCall(
        string threadId,
        ToolCall call,
        ToolDefinition? definition,
        ToolRetryProfile profile,
        IReadOnlySet<string>? attemptedAlternateEngineIds,
        out ToolCall fallbackCall,
        out string selectedEngineId) {
        fallbackCall = call;
        selectedEngineId = string.Empty;

        if (definition?.Parameters is null || profile.AlternateEngineIds.Count == 0) {
            return false;
        }

        if (!TryResolveAlternateEngineSelectorArgumentName(definition, out var selectorArgumentName, out var selectorProperty)) {
            return false;
        }

        if (!TryParseToolCallArgumentsFromInput(call.Input, out var parsedInputArguments)) {
            parsedInputArguments = null!;
        }

        var sourceArguments = call.Arguments ?? parsedInputArguments;
        var currentEngineId = ReadAlternateEngineSelectorValue(sourceArguments, selectorArgumentName);
        var allowedSelectorValues = ReadAllowedAlternateEngineSelectorValues(selectorProperty);
        var orderedAlternateEngineIds = OrderAlternateEngineIdsByHealth(
            threadId,
            call.Name,
            profile.AlternateEngineIds,
            allowedSelectorValues,
            attemptedAlternateEngineIds);
        if (orderedAlternateEngineIds.Length == 0) {
            return false;
        }

        return TryBuildAlternateEngineFallbackCallCore(
            call,
            sourceArguments,
            selectorArgumentName,
            orderedAlternateEngineIds,
            currentEngineId,
            out fallbackCall,
            out selectedEngineId);
    }

    private static bool TryBuildAlternateEngineFallbackCall(
        ToolCall call,
        ToolDefinition? definition,
        ToolRetryProfile profile,
        out ToolCall fallbackCall,
        out string selectedEngineId) {
        fallbackCall = call;
        selectedEngineId = string.Empty;

        if (definition?.Parameters is null || profile.AlternateEngineIds.Count == 0) {
            return false;
        }

        if (!TryParseToolCallArgumentsFromInput(call.Input, out var parsedInputArguments)) {
            parsedInputArguments = null!;
        }

        var sourceArguments = call.Arguments ?? parsedInputArguments;
        if (!TryResolveAlternateEngineSelectorArgumentName(definition, out var selectorArgumentName, out var selectorProperty)) {
            return false;
        }

        var currentEngineId = ReadAlternateEngineSelectorValue(sourceArguments, selectorArgumentName);
        var allowedSelectorValues = ReadAllowedAlternateEngineSelectorValues(selectorProperty);
        var orderedAlternateEngineIds = profile.AlternateEngineIds
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(static candidate => candidate.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedAlternateEngineIds.Length == 0) {
            return false;
        }

        if (allowedSelectorValues is not null && allowedSelectorValues.Count > 0) {
            orderedAlternateEngineIds = orderedAlternateEngineIds
                .Where(allowedSelectorValues.Contains)
                .ToArray();
            if (orderedAlternateEngineIds.Length == 0) {
                return false;
            }
        }

        return TryBuildAlternateEngineFallbackCallCore(
            call,
            sourceArguments,
            selectorArgumentName,
            orderedAlternateEngineIds,
            currentEngineId,
            out fallbackCall,
            out selectedEngineId);
    }

    private static bool TryBuildAlternateEngineFallbackCallCore(
        ToolCall call,
        JsonObject? sourceArguments,
        string selectorArgumentName,
        IReadOnlyList<string> candidateEngineIds,
        string currentEngineId,
        out ToolCall fallbackCall,
        out string selectedEngineId) {
        fallbackCall = call;
        selectedEngineId = string.Empty;

        if (!TrySelectAlternateEngineId(candidateEngineIds, currentEngineId, out selectedEngineId)) {
            return false;
        }

        var fallbackArguments = CloneToolArguments(sourceArguments);
        fallbackArguments[selectorArgumentName] = JsonValue.From(selectedEngineId);
        var fallbackInput = JsonLite.Serialize(fallbackArguments);
        fallbackCall = new ToolCall(call.CallId, call.Name, fallbackInput, fallbackArguments, call.Raw);
        return true;
    }

    private static bool TryBuildAutomaticAlternateEngineRetryCall(
        ToolCall originalCall,
        ToolCall currentCall,
        ToolDefinition? definition,
        ToolRetryProfile profile,
        out ToolCall automaticCall) {
        automaticCall = originalCall;

        if (definition?.Parameters is null || profile.AlternateEngineIds.Count == 0) {
            return false;
        }

        if (!TryResolveAlternateEngineSelectorArgumentName(definition, out var selectorArgumentName, out _)) {
            return false;
        }

        if (!TryParseToolCallArgumentsFromInput(originalCall.Input, out var parsedOriginalArguments)) {
            parsedOriginalArguments = null!;
        }

        if (!TryParseToolCallArgumentsFromInput(currentCall.Input, out var parsedCurrentArguments)) {
            parsedCurrentArguments = null!;
        }

        var originalArguments = originalCall.Arguments ?? parsedOriginalArguments;
        var currentArguments = currentCall.Arguments ?? parsedCurrentArguments;
        if (!IsImplicitAlternateEngineSelection(ReadAlternateEngineSelectorValue(originalArguments, selectorArgumentName))
            || IsImplicitAlternateEngineSelection(ReadAlternateEngineSelectorValue(currentArguments, selectorArgumentName))) {
            return false;
        }

        var automaticArguments = CloneToolArguments(originalArguments);
        var automaticInput = JsonLite.Serialize(automaticArguments);
        automaticCall = new ToolCall(originalCall.CallId, originalCall.Name, automaticInput, automaticArguments, originalCall.Raw);
        return true;
    }

    private bool TrySelectPreferredHealthyAlternateEngineId(
        string threadId,
        string toolName,
        IReadOnlyList<string> candidateEngineIds,
        IReadOnlySet<string>? allowedSelectorValues,
        string currentEngineId,
        out string selectedEngineId) {
        selectedEngineId = string.Empty;

        var orderedAlternateEngineIds = OrderAlternateEngineIdsByHealth(
            threadId,
            toolName,
            candidateEngineIds,
            allowedSelectorValues);
        if (orderedAlternateEngineIds.Length == 0
            || !TrySelectAlternateEngineId(orderedAlternateEngineIds, currentEngineId, out var candidateEngineId)) {
            return false;
        }

        var healthByEngineId = SnapshotAlternateEngineHealthByEngineId(threadId, toolName);
        if (!healthByEngineId.TryGetValue(NormalizeAlternateEngineHealthToken(candidateEngineId), out var snapshot)
            || ComputeAlternateEngineHealthScore(snapshot) <= 0d) {
            return false;
        }

        selectedEngineId = candidateEngineId;
        return true;
    }

    private static bool TryResolveAlternateEngineSelectorArgumentName(
        ToolDefinition definition,
        out string argumentName,
        out JsonObject? selectorProperty) {
        argumentName = string.Empty;
        selectorProperty = null;
        var properties = definition.Parameters?.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return false;
        }

        if (!ToolAlternateEngineSelectorNames.TryResolveSelectorArgumentName(properties, out argumentName)) {
            return false;
        }

        selectorProperty = properties.GetObject(argumentName);
        return selectorProperty is not null;
    }

    private static string ReadAlternateEngineSelectorValue(JsonObject? arguments, string selectorArgumentName) {
        if (arguments is null || selectorArgumentName.Length == 0) {
            return string.Empty;
        }

        if (!arguments.TryGetValue(selectorArgumentName, out var rawValue) || rawValue is null) {
            return string.Empty;
        }

        return (rawValue.AsString() ?? string.Empty).Trim();
    }

    private static bool TryResolveTrackedAlternateEngineId(
        ToolCall call,
        ToolDefinition? definition,
        ToolRetryProfile profile,
        out string engineId) {
        engineId = string.Empty;

        if (definition?.Parameters is null || profile.AlternateEngineIds.Count == 0) {
            return false;
        }

        if (!TryResolveAlternateEngineSelectorArgumentName(definition, out var selectorArgumentName, out _)) {
            return false;
        }

        if (!TryParseToolCallArgumentsFromInput(call.Input, out var parsedInputArguments)) {
            parsedInputArguments = null!;
        }

        var sourceArguments = call.Arguments ?? parsedInputArguments;
        var currentEngineId = ReadAlternateEngineSelectorValue(sourceArguments, selectorArgumentName);
        if (IsImplicitAlternateEngineSelection(currentEngineId)) {
            return false;
        }

        for (var i = 0; i < profile.AlternateEngineIds.Count; i++) {
            var candidate = (profile.AlternateEngineIds[i] ?? string.Empty).Trim();
            if (candidate.Length == 0 || !string.Equals(candidate, currentEngineId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            engineId = candidate;
            return true;
        }

        return false;
    }

    private static bool IsImplicitAlternateEngineSelection(string? currentEngineId) {
        var normalized = (currentEngineId ?? string.Empty).Trim();
        return normalized.Length == 0
               || string.Equals(normalized, AutomaticAlternateEngineSelectorValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySelectAlternateEngineId(
        IReadOnlyList<string> alternateEngineIds,
        string currentEngineId,
        out string selectedEngineId) {
        selectedEngineId = string.Empty;
        if (alternateEngineIds is null || alternateEngineIds.Count == 0) {
            return false;
        }

        var normalizedCurrentEngineId = (currentEngineId ?? string.Empty).Trim();
        for (var i = 0; i < alternateEngineIds.Count; i++) {
            var candidate = (alternateEngineIds[i] ?? string.Empty).Trim();
            if (candidate.Length == 0
                || string.Equals(candidate, normalizedCurrentEngineId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            selectedEngineId = candidate;
            return true;
        }

        return false;
    }

    private static IReadOnlySet<string>? ReadAllowedAlternateEngineSelectorValues(JsonObject? selectorProperty) {
        var enumValues = selectorProperty?.GetArray("enum");
        if (enumValues is null || enumValues.Count == 0) {
            return null;
        }

        var allowedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < enumValues.Count; i++) {
            var normalized = (enumValues[i].AsString() ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                continue;
            }

            allowedValues.Add(normalized);
        }

        return allowedValues.Count == 0 ? null : allowedValues;
    }

    private static JsonObject CloneToolArguments(JsonObject? arguments) {
        var clone = new JsonObject(StringComparer.Ordinal);
        if (arguments is null || arguments.Count == 0) {
            return clone;
        }

        foreach (var pair in arguments) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (TryCloneRecoveryHelperArgumentValue(pair.Value, out var clonedValue)) {
                clone.Add(key, clonedValue);
            } else {
                clone.Add(key, pair.Value);
            }
        }

        return clone;
    }
}
