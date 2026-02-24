using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static readonly string[] ProjectionFormattingArgumentNames = { "columns", "sort_by", "sort_direction" };
    private static readonly Regex TopValidationRegex = new(
        @"\btop\b\s+(?:must|should|has to|needs to)\b|\b(?:invalid|unsupported|required)\s+(?:value\s+for\s+)?(?:top|top\s+(?:value|argument|parameter))\b|\b(?:top\s+(?:value|argument|parameter)|parameter\s+['""]?top['""]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private const string ProjectionColumnsArgumentName = "columns";
    private const string ProjectionSortByArgumentName = "sort_by";
    private const string ProjectionSortDirectionArgumentName = "sort_direction";
    private const string ProjectionTopArgumentName = "top";
    private const string ProjectionFallbackRecoveredStatusMessage = "Recovered from projection argument failure.";

    private static bool TryBuildProjectionArgsFallbackCall(ToolCall call, ToolOutputDto output, out ToolCall fallbackCall,
        out ProjectionFallbackInfo fallbackInfo) {
        fallbackCall = call;
        fallbackInfo = default;

        if (!IsProjectionViewArgumentFailure(output)) {
            return false;
        }

        var sourceArguments = call.Arguments;
        if (sourceArguments is null && TryParseToolCallArgumentsFromInput(call.Input, out var parsedInputArguments)) {
            sourceArguments = parsedInputArguments;
        }

        var fallbackArguments = CloneWithoutProjectionViewArguments(sourceArguments, output, out var removedArguments);
        if (removedArguments.Length == 0) {
            return false;
        }

        var fallbackInput = fallbackArguments is null ? null : JsonLite.Serialize(fallbackArguments);
        fallbackCall = new ToolCall(call.CallId, call.Name, fallbackInput, fallbackArguments, call.Raw);
        fallbackInfo = new ProjectionFallbackInfo(
            RemovedArguments: removedArguments,
            OriginalErrorCode: (output.ErrorCode ?? string.Empty).Trim(),
            OriginalError: CompactProjectionFallbackReason(output.Error));
        return true;
    }

    private static bool TryParseToolCallArgumentsFromInput(string? input, out JsonObject arguments) {
        arguments = null!;
        var raw = (input ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return false;
        }

        try {
            arguments = JsonLite.Parse(raw)?.AsObject()!;
            return arguments is not null;
        } catch {
            arguments = null!;
            return false;
        }
    }

    private static bool IsProjectionViewArgumentFailure(ToolOutputDto output) {
        if (output.Ok is true) {
            return false;
        }

        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        var hasProjectionSignal = text.Contains("columns", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("sort_by", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("sort direction", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("sort_direction", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("tabular view", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("projection", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("projection argument", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("table view response envelope", StringComparison.OrdinalIgnoreCase);
        if (!hasProjectionSignal) {
            hasProjectionSignal = HasProjectionTopFallbackSignal(output);
            if (!hasProjectionSignal) {
                return false;
            }
        }

        if (string.Equals(output.ErrorCode, "invalid_argument", StringComparison.OrdinalIgnoreCase)
            || string.Equals(output.ErrorCode, "tool_error", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return text.Contains("unsupported value", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must be one of", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject? CloneWithoutProjectionViewArguments(JsonObject? arguments, ToolOutputDto output, out string[] removedArguments) {
        removedArguments = Array.Empty<string>();
        if (arguments is null || arguments.Count == 0) {
            return arguments;
        }

        if (TryBuildSelectiveProjectionFallbackArguments(arguments, output, out var selectiveFallback, out var selectiveRemoved)) {
            removedArguments = selectiveRemoved;
            return selectiveFallback;
        }

        var removeTopArgument = HasProjectionTopFallbackSignal(output);
        var clone = new JsonObject(StringComparer.Ordinal);
        var removed = new List<string>(ProjectionFormattingArgumentNames.Length + 1);
        var seenRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in arguments) {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (IsProjectionFormattingArgumentName(key) || (removeTopArgument && IsProjectionTopArgumentName(key))) {
                if (seenRemoved.Add(key)) {
                    removed.Add(key);
                }
                continue;
            }

            clone.Add(key, kv.Value);
        }

        if (removed.Count == 0
            && !removeTopArgument
            && ShouldDropTopForProjectionEnvelopeFallback(arguments, output)) {
            clone = new JsonObject(StringComparer.Ordinal);
            foreach (var kv in arguments) {
                var key = (kv.Key ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                if (IsProjectionTopArgumentName(key)) {
                    if (seenRemoved.Add(key)) {
                        removed.Add(key);
                    }
                    continue;
                }

                clone.Add(key, kv.Value);
            }
        }

        removedArguments = removed.Count == 0 ? Array.Empty<string>() : removed.ToArray();
        return clone;
    }

    private static bool TryBuildSelectiveProjectionFallbackArguments(
        JsonObject arguments,
        ToolOutputDto output,
        out JsonObject fallbackArguments,
        out string[] removedArguments) {
        fallbackArguments = arguments;
        removedArguments = Array.Empty<string>();
        if (!TryReadProjectionAvailableColumns(output, out var availableColumns) || availableColumns.Count == 0) {
            return false;
        }

        var removeColumns = false;
        JsonArray? columnsReplacement = null;
        if (TryGetArgumentValue(arguments, ProjectionColumnsArgumentName, out var rawColumns)) {
            if (rawColumns?.AsArray() is JsonArray columnsArray) {
                var requestedColumns = ToolArgs.ReadDistinctStringArray(columnsArray);
                if (requestedColumns.Count > 0) {
                    var keptColumns = requestedColumns
                        .Where(availableColumns.Contains)
                        .ToArray();
                    if (keptColumns.Length < requestedColumns.Count) {
                        if (keptColumns.Length == 0) {
                            removeColumns = true;
                        } else {
                            columnsReplacement = new JsonArray().AddRange(keptColumns);
                        }
                    }
                }
            } else if (BuildToolFailureSearchText(output).Contains("columns", StringComparison.OrdinalIgnoreCase)) {
                // When columns is malformed/non-array, remove it as a safe recovery step.
                removeColumns = true;
            }
        }

        var removeSortBy = false;
        if (TryGetArgumentString(arguments, ProjectionSortByArgumentName, out var sortByValue)
            && sortByValue.Length > 0
            && !availableColumns.Contains(sortByValue)) {
            removeSortBy = true;
        }

        var removeSortDirection = removeSortBy && HasArgument(arguments, ProjectionSortDirectionArgumentName);
        if (!removeSortDirection
            && HasArgument(arguments, ProjectionSortDirectionArgumentName)
            && IsSortDirectionValidationFailure(output)) {
            removeSortDirection = true;
        }

        if (!removeColumns && columnsReplacement is null && !removeSortBy && !removeSortDirection) {
            return false;
        }

        var clone = new JsonObject(StringComparer.Ordinal);
        var removed = new List<string>(3);
        var seenRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in arguments) {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (IsProjectionColumnsArgumentName(key)) {
                if (removeColumns) {
                    AddRemovedArgument(removed, seenRemoved, ProjectionColumnsArgumentName);
                    continue;
                }
                if (columnsReplacement is not null) {
                    clone.Add(key, columnsReplacement);
                    AddRemovedArgument(removed, seenRemoved, ProjectionColumnsArgumentName);
                    continue;
                }
            }

            if (IsProjectionSortByArgumentName(key) && removeSortBy) {
                AddRemovedArgument(removed, seenRemoved, ProjectionSortByArgumentName);
                continue;
            }

            if (IsProjectionSortDirectionArgumentName(key) && removeSortDirection) {
                AddRemovedArgument(removed, seenRemoved, ProjectionSortDirectionArgumentName);
                continue;
            }

            clone.Add(key, kv.Value);
        }

        removedArguments = removed.Count == 0 ? Array.Empty<string>() : removed.ToArray();
        fallbackArguments = clone;
        return removedArguments.Length > 0;
    }

    private static void AddRemovedArgument(List<string> target, HashSet<string> seen, string argumentName) {
        if (target is null || seen is null || string.IsNullOrWhiteSpace(argumentName)) {
            return;
        }

        var normalized = argumentName.Trim();
        if (seen.Add(normalized)) {
            target.Add(normalized);
        }
    }

    private static bool TryReadProjectionAvailableColumns(ToolOutputDto output, out HashSet<string> availableColumns) {
        availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observed = TryAppendAvailableProjectionColumns(output.MetaJson, availableColumns);
        observed |= TryAppendAvailableProjectionColumns(output.Output, availableColumns);
        return observed;
    }

    private static bool TryAppendAvailableProjectionColumns(string? rawJson, HashSet<string> destination) {
        if (destination is null || string.IsNullOrWhiteSpace(rawJson)) {
            return false;
        }

        JsonObject? obj;
        try {
            obj = JsonLite.Parse(rawJson)?.AsObject();
        } catch {
            return false;
        }

        if (obj is null) {
            return false;
        }

        var before = destination.Count;
        AppendAvailableColumns(obj, destination);
        if (obj.GetObject("meta") is JsonObject metaObj) {
            AppendAvailableColumns(metaObj, destination);
        }
        if (obj.GetObject("failure") is JsonObject failureObj) {
            AppendAvailableColumns(failureObj, destination);
            if (failureObj.GetObject("meta") is JsonObject failureMetaObj) {
                AppendAvailableColumns(failureMetaObj, destination);
            }
        }
        return destination.Count > before;
    }

    private static void AppendAvailableColumns(JsonObject obj, HashSet<string> destination) {
        var availableColumnsArray = obj.GetArray("available_columns");
        if ((availableColumnsArray is null || availableColumnsArray.Count == 0)
            && obj.GetArray("availableColumns") is JsonArray camelCaseColumns) {
            availableColumnsArray = camelCaseColumns;
        }

        if (availableColumnsArray is null || availableColumnsArray.Count == 0) {
            return;
        }

        for (var i = 0; i < availableColumnsArray.Count; i++) {
            var value = availableColumnsArray[i].AsString();
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            destination.Add(value.Trim());
        }
    }

    private static bool TryGetArgumentValue(JsonObject arguments, string argumentName, out JsonValue? value) {
        value = null;
        if (arguments is null || string.IsNullOrWhiteSpace(argumentName)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (string.Equals((kv.Key ?? string.Empty).Trim(), argumentName, StringComparison.OrdinalIgnoreCase)) {
                value = kv.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetArgumentString(JsonObject arguments, string argumentName, out string value) {
        value = string.Empty;
        if (!TryGetArgumentValue(arguments, argumentName, out var rawValue)) {
            return false;
        }

        value = (rawValue?.AsString() ?? string.Empty).Trim();
        return true;
    }

    private static bool HasArgument(JsonObject arguments, string argumentName) {
        return TryGetArgumentValue(arguments, argumentName, out _);
    }

    private static bool IsSortDirectionValidationFailure(ToolOutputDto output) {
        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        if (!text.Contains("sort_direction", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("sort direction", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return text.Contains("asc", StringComparison.OrdinalIgnoreCase)
               || text.Contains("desc", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must be one of", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectionColumnsArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionColumnsArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectionSortByArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionSortByArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectionSortDirectionArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionSortDirectionArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectionFormattingArgumentName(string argumentName) {
        for (var i = 0; i < ProjectionFormattingArgumentNames.Length; i++) {
            if (string.Equals(argumentName, ProjectionFormattingArgumentNames[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static bool IsProjectionTopArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionTopArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool HasProjectionTopFallbackSignal(ToolOutputDto output) {
        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        if (!text.Contains("top", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return TopValidationRegex.IsMatch(text);
    }

    private static bool ShouldDropTopForProjectionEnvelopeFallback(JsonObject arguments, ToolOutputDto output) {
        if (arguments is null || !HasArgument(arguments, ProjectionTopArgumentName)) {
            return false;
        }

        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        return text.Contains("table view response envelope", StringComparison.OrdinalIgnoreCase)
               || text.Contains("projection argument", StringComparison.OrdinalIgnoreCase)
               || text.Contains("tabular view", StringComparison.OrdinalIgnoreCase);
    }

    private static ToolOutputDto AttachProjectionFallbackMetadata(ToolOutputDto output, ProjectionFallbackInfo info) {
        if (string.IsNullOrWhiteSpace(output.Output)) {
            return output;
        }

        JsonObject? envelope = null;
        try {
            envelope = JsonLite.Parse(output.Output)?.AsObject();
        } catch {
            envelope = null;
        }

        if (envelope is null) {
            return output;
        }

        var meta = envelope.GetObject("meta") ?? new JsonObject(StringComparer.Ordinal);
        meta.Add("projection_fallback_applied", true);

        var removed = new JsonArray();
        for (var i = 0; i < info.RemovedArguments.Length; i++) {
            if (!string.IsNullOrWhiteSpace(info.RemovedArguments[i])) {
                removed.Add(info.RemovedArguments[i].Trim());
            }
        }
        meta.Add("projection_fallback_removed_args", removed);
        if (!string.IsNullOrWhiteSpace(info.OriginalErrorCode)) {
            meta.Add("projection_fallback_reason_code", info.OriginalErrorCode);
        }
        if (!string.IsNullOrWhiteSpace(info.OriginalError)) {
            meta.Add("projection_fallback_reason", info.OriginalError);
        }
        envelope.Add("meta", meta);

        const string fallbackHint = "Projection arguments were adjusted after a view-argument failure.";
        if (envelope.GetArray("hints") is JsonArray rootHints) {
            AddDistinctJsonString(rootHints, fallbackHint);
        } else {
            envelope.Add("hints", new JsonArray().Add(fallbackHint));
        }

        if (envelope.GetObject("failure") is JsonObject failure) {
            if (failure.GetArray("hints") is JsonArray failureHints) {
                AddDistinctJsonString(failureHints, fallbackHint);
            } else {
                failure.Add("hints", new JsonArray().Add(fallbackHint));
            }
            envelope.Add("failure", failure);
        }

        return BuildToolOutputDto(output.CallId, JsonLite.Serialize(envelope));
    }

    private static void AddDistinctJsonString(JsonArray target, string value) {
        if (target is null || string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var normalized = value.Trim();
        for (var i = 0; i < target.Count; i++) {
            var existing = target[i]?.AsString();
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
        }

        target.Add(normalized);
    }

    private static string CompactProjectionFallbackReason(string? errorText) {
        var compact = CompactFailureText(errorText);
        const int maxReasonLength = 320;
        if (compact.Length <= maxReasonLength) {
            return compact;
        }
        return compact[..maxReasonLength].TrimEnd();
    }

    private static bool WasProjectionFallbackApplied(ToolOutputDto output) {
        if (output is null) {
            return false;
        }

        return TryReadProjectionFallbackApplied(output.MetaJson, out var appliedFromMeta) && appliedFromMeta
               || TryReadProjectionFallbackApplied(output.Output, out var appliedFromOutput) && appliedFromOutput;
    }

    private static bool TryReadProjectionFallbackApplied(string? rawJson, out bool applied) {
        applied = false;
        if (string.IsNullOrWhiteSpace(rawJson)) {
            return false;
        }

        JsonObject? obj;
        try {
            obj = JsonLite.Parse(rawJson)?.AsObject();
        } catch {
            return false;
        }

        if (obj is null) {
            return false;
        }

        if (TryReadProjectionFallbackFlag(obj, out applied)) {
            return true;
        }

        if (obj.GetObject("meta") is JsonObject meta && TryReadProjectionFallbackFlag(meta, out applied)) {
            return true;
        }

        return false;
    }

    private static bool TryReadProjectionFallbackFlag(JsonObject obj, out bool applied) {
        applied = false;
        if (!obj.TryGetValue("projection_fallback_applied", out var raw)
            || raw is null
            || raw.Kind != IntelligenceX.Json.JsonValueKind.Boolean) {
            return false;
        }

        applied = raw.AsBoolean();
        return true;
    }

    private readonly record struct ProjectionFallbackInfo(string[] RemovedArguments, string OriginalErrorCode, string OriginalError);
}
