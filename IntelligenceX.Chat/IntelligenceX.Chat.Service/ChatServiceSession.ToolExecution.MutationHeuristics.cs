using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static IReadOnlyDictionary<string, bool> BuildMutatingToolHintsByName(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions.Count == 0) {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        var hints = new Dictionary<string, bool>(definitions.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var capability = ClassifyMutatingCapabilityFromDefinition(definition);
            if (!capability.HasValue) {
                continue;
            }

            hints[definition.Name.Trim()] = capability.Value;
        }

        return hints;
    }

    private static bool? ClassifyMutatingCapabilityFromDefinition(ToolDefinition definition) {
        if (definition.WriteGovernance is not null) {
            return definition.WriteGovernance.IsWriteCapable;
        }

        if (TryClassifyMutatingCapabilityFromTags(definition.Tags, out var fromTags)) {
            return fromTags;
        }

        if (TryClassifyMutatingCapabilityFromSchema(definition.Parameters, out var fromSchema)) {
            return fromSchema;
        }

        return null;
    }

    private static bool TryClassifyMutatingCapabilityFromTags(IReadOnlyList<string> tags, out bool isMutating) {
        isMutating = false;
        if (tags is not { Count: > 0 }) {
            return false;
        }

        var sawReadOnlyHint = false;
        var sawMutatingHint = false;
        for (var i = 0; i < tags.Count; i++) {
            var token = ToolMutabilityHintNames.NormalizeHintToken(tags[i]);
            if (token.Length == 0) {
                continue;
            }

            if (IsMutatingMetadataToken(token)) {
                sawMutatingHint = true;
                continue;
            }

            if (IsReadOnlyMetadataToken(token)) {
                sawReadOnlyHint = true;
            }
        }

        if (!sawReadOnlyHint && !sawMutatingHint) {
            return false;
        }

        isMutating = sawMutatingHint;
        return true;
    }

    private static bool TryClassifyMutatingCapabilityFromSchema(JsonObject? parameters, out bool isMutating) {
        isMutating = false;
        var properties = parameters?.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return false;
        }

        var sawReadOnlyHint = false;
        var sawMutatingHint = false;
        foreach (var property in properties) {
            var keyToken = ToolMutabilityHintNames.NormalizeHintToken(property.Key);
            if (keyToken.Length > 0) {
                sawMutatingHint |= IsMutatingMetadataToken(keyToken);
                sawReadOnlyHint |= IsReadOnlyMetadataToken(keyToken);
            }

            var propertySchema = property.Value?.AsObject();
            var enumValues = propertySchema?.GetArray("enum");
            if (enumValues is null || enumValues.Count == 0) {
                continue;
            }

            foreach (var enumValue in enumValues) {
                var enumToken = ToolMutabilityHintNames.NormalizeHintToken(enumValue?.AsString());
                if (enumToken.Length == 0) {
                    continue;
                }

                sawMutatingHint |= IsMutatingMetadataToken(enumToken);
                sawReadOnlyHint |= IsReadOnlyMetadataToken(enumToken);
            }
        }

        if (!sawReadOnlyHint && !sawMutatingHint) {
            return false;
        }

        isMutating = sawMutatingHint;
        return true;
    }

    private static bool IsMutatingMetadataToken(string token) {
        return ToolMutabilityHintNames.LooksLikeMutatingHint(token);
    }

    private static bool IsReadOnlyMetadataToken(string token) {
        return ToolMutabilityHintNames.LooksLikeReadOnlyHint(token);
    }

    private static bool HasMutatingToolCallsWithHints(IReadOnlyList<ToolCall> calls, IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out string[] mutatingToolNames) {
        mutatingToolNames = Array.Empty<string>();
        if (calls.Count == 0) {
            return false;
        }

        var mutating = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            if (!IsLikelyMutatingToolCallWithHints(calls[i], mutatingToolHintsByName)) {
                continue;
            }

            var name = (calls[i].Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                name = "tool";
            }

            if (seen.Add(name)) {
                mutating.Add(name);
            }
        }

        if (mutating.Count == 0) {
            return false;
        }

        mutatingToolNames = mutating.ToArray();
        return true;
    }

    private static bool IsLikelyMutatingToolCallWithHints(ToolCall call, IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        var toolName = (call.Name ?? string.Empty).Trim();
        if (toolName.Length > 0 && mutatingToolHintsByName is not null && mutatingToolHintsByName.TryGetValue(toolName, out var isMutating)) {
            return isMutating;
        }

        return false;
    }

    private static string BuildToolParallelSafetyOffMessage(string[] mutatingToolNames) {
        if (mutatingToolNames.Length == 0) {
            return "Parallel execution paused for mutating tools; running calls sequentially.";
        }

        var listed = string.Join(", ", mutatingToolNames.Take(3));
        var suffix = mutatingToolNames.Length > 3 ? ", ..." : string.Empty;
        return $"Parallel execution paused for mutating tools ({listed}{suffix}); running calls sequentially.";
    }

    private static string BuildToolParallelForcedMessage(string[] mutatingToolNames) {
        if (mutatingToolNames.Length == 0) {
            return "Parallel mode forced by caller, including mutating tools.";
        }

        var listed = string.Join(", ", mutatingToolNames.Take(3));
        var suffix = mutatingToolNames.Length > 3 ? ", ..." : string.Empty;
        return $"Parallel mode forced by caller for mutating tools ({listed}{suffix}).";
    }

    private static int[] CollectLowConcurrencyRecoveryIndexesWithHints(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        var max = Math.Min(calls.Count, outputs.Count);
        if (max <= 0) {
            return Array.Empty<int>();
        }

        var list = new List<int>(Math.Min(max, MaxLowConcurrencyRecoveryCalls));
        for (var i = 0; i < max; i++) {
            if (!ShouldReplayToolCallAtLowConcurrencyWithHints(calls[i], outputs[i], mutatingToolHintsByName)) {
                continue;
            }

            list.Add(i);
            if (list.Count >= MaxLowConcurrencyRecoveryCalls) {
                break;
            }
        }

        return list.Count == 0 ? Array.Empty<int>() : list.ToArray();
    }

    private static bool ShouldReplayToolCallAtLowConcurrencyWithHints(ToolCall call, ToolOutputDto output,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        if (output.Ok is true) {
            return false;
        }

        if (IsLikelyMutatingToolCallWithHints(call, mutatingToolHintsByName)) {
            return false;
        }

        if (string.Equals(output.ErrorCode, "tool_not_registered", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (output.IsTransient is true) {
            return true;
        }

        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("transport", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("transient", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return false;
    }

    private static int[] CollectLowConcurrencyRecoveryIndexes(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        return CollectLowConcurrencyRecoveryIndexesWithHints(calls, outputs, mutatingToolHintsByName: null);
    }

    private static bool ShouldReplayToolCallAtLowConcurrency(ToolCall call, ToolOutputDto output) {
        return ShouldReplayToolCallAtLowConcurrencyWithHints(call, output, mutatingToolHintsByName: null);
    }

    private static bool IsLikelyMutatingToolName(string? toolName) {
        return false;
    }

    private static bool HasLikelyMutatingToolCalls(IReadOnlyList<ToolCall> calls) {
        return HasMutatingToolCallsWithHints(calls, mutatingToolHintsByName: null, out _);
    }

    private static int ResolveParallelToolConcurrency(int callCount) {
        var normalizedCount = Math.Max(0, callCount);
        if (normalizedCount <= 1) {
            return 1;
        }

        var processorCap = Math.Clamp(Environment.ProcessorCount, MinParallelToolConcurrencyCap, MaxParallelToolConcurrencyCap);
        return Math.Clamp(normalizedCount, 1, processorCap);
    }

    private static string BuildToolBatchStartedMessage(int totalCalls, int maxConcurrency) {
        var total = Math.Max(0, totalCalls);
        var concurrency = Math.Max(1, maxConcurrency);
        if (total <= 1) {
            return "Running tool call...";
        }

        if (concurrency >= total) {
            return $"Running {total} tool calls in parallel.";
        }

        return $"Running {total} tool calls in parallel batches ({concurrency} at a time).";
    }

    private static string BuildToolBatchProgressMessage(int completedCalls, int totalCalls, int maxConcurrency, int failedCalls) {
        var total = Math.Max(0, totalCalls);
        var completed = Math.Clamp(completedCalls, 0, total);
        var failed = Math.Clamp(failedCalls, 0, completed);
        var concurrency = Math.Max(1, maxConcurrency);
        if (failed <= 0) {
            return $"Tool batch progress: {completed}/{total} complete ({concurrency} max parallel).";
        }

        return $"Tool batch progress: {completed}/{total} complete ({failed} failed, {concurrency} max parallel).";
    }

    private static string BuildToolBatchHeartbeatMessage(int completedCalls, int totalCalls, int maxConcurrency, int startedCalls, int failedCalls,
        int elapsedSeconds) {
        var total = Math.Max(0, totalCalls);
        var completed = Math.Clamp(completedCalls, 0, total);
        var started = Math.Clamp(startedCalls, completed, total);
        var failed = Math.Clamp(failedCalls, 0, completed);
        var active = Math.Clamp(started - completed, 0, Math.Max(1, maxConcurrency));
        var queued = Math.Max(0, total - started);
        var concurrency = Math.Max(1, maxConcurrency);
        var elapsed = Math.Max(1, elapsedSeconds);

        if (failed <= 0) {
            return
                $"Tool batch still running: {completed}/{total} complete ({active} active, {queued} queued, {concurrency} max parallel, {elapsed}s elapsed).";
        }

        return
            $"Tool batch still running: {completed}/{total} complete ({active} active, {queued} queued, {failed} failed, {concurrency} max parallel, {elapsed}s elapsed).";
    }

    private static string BuildToolBatchCompletedMessage(int totalCalls, int maxConcurrency, int failedCalls) {
        var total = Math.Max(0, totalCalls);
        var failed = Math.Clamp(failedCalls, 0, total);
        var concurrency = Math.Max(1, maxConcurrency);
        if (failed <= 0) {
            return $"Tool batch finished: {total}/{total} complete ({concurrency} max parallel).";
        }

        return $"Tool batch finished: {total}/{total} complete ({failed} failed, {concurrency} max parallel).";
    }
}
