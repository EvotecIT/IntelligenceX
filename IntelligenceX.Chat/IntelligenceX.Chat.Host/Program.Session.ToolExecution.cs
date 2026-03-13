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
        private async Task<IReadOnlyList<ToolOutput>> ExecuteToolsAsync(IReadOnlyList<ToolCall> calls, CancellationToken cancellationToken) {
            var knownHostTargets = GetRecentHostTargetsSnapshot();
            var runInParallel = ShouldRunParallelToolExecution(calls, out var mutatingToolNames);
            if (_options.LiveProgress
                && _options.ParallelToolCalls
                && calls.Count > 1
                && !runInParallel
                && mutatingToolNames.Length > 0) {
                var listed = string.Join(", ", mutatingToolNames.Take(3));
                var suffix = mutatingToolNames.Length > 3 ? ", ..." : string.Empty;
                _status?.Invoke(
                    $"parallel safety: running sequentially because write-capable tools were requested ({listed}{suffix}). " +
                    "Use --allow-mutating-parallel-tools to override.");
            }

            var nonReusableIndices = GetNonReusableReadOnlyToolCallIndices(calls);
            var canonicalIndices = BuildReadOnlyCallCanonicalIndices(calls, nonReusableIndices, out var dedupedReadOnlyCalls);
            if (_options.LiveProgress && dedupedReadOnlyCalls > 0) {
                _status?.Invoke($"input-repair: deduplicated {dedupedReadOnlyCalls} identical read-only tool call signatures in this turn.");
            }

            var uniqueCanonicalIndices = GetUniqueCanonicalIndices(canonicalIndices);
            if (!runInParallel || calls.Count <= 1) {
                var canonicalOutputs = new Dictionary<int, ToolOutput>(uniqueCanonicalIndices.Length);
                for (var index = 0; index < uniqueCanonicalIndices.Length; index++) {
                    var canonicalIndex = uniqueCanonicalIndices[index];
                    canonicalOutputs[canonicalIndex] = await ExecuteToolAsync(calls[canonicalIndex], cancellationToken, knownHostTargets).ConfigureAwait(false);
                }

                return RehydrateToolOutputsFromCanonical(calls, canonicalIndices, canonicalOutputs);
            }

            var tasks = new Task<ToolOutput>[uniqueCanonicalIndices.Length];
            for (var index = 0; index < uniqueCanonicalIndices.Length; index++) {
                var canonicalIndex = uniqueCanonicalIndices[index];
                tasks[index] = ExecuteToolAsync(calls[canonicalIndex], cancellationToken, knownHostTargets);
            }

            var executed = await Task.WhenAll(tasks).ConfigureAwait(false);
            var outputsByCanonical = new Dictionary<int, ToolOutput>(uniqueCanonicalIndices.Length);
            for (var index = 0; index < uniqueCanonicalIndices.Length; index++) {
                outputsByCanonical[uniqueCanonicalIndices[index]] = executed[index];
            }

            return RehydrateToolOutputsFromCanonical(calls, canonicalIndices, outputsByCanonical);
        }

        private ISet<int> GetNonReusableReadOnlyToolCallIndices(IReadOnlyList<ToolCall> calls) {
            var nonReusable = new HashSet<int>();
            for (var i = 0; i < calls.Count; i++) {
                var toolName = (calls[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0 || !_registry.TryGetDefinition(toolName, out var definition)) {
                    nonReusable.Add(i);
                    continue;
                }

                if (definition.WriteGovernance?.IsWriteCapable == true) {
                    nonReusable.Add(i);
                }
            }

            return nonReusable;
        }

        private static int[] BuildReadOnlyCallCanonicalIndices(
            IReadOnlyList<ToolCall> calls,
            ISet<int> nonReusableIndices,
            out int dedupedCount) {
            dedupedCount = 0;
            var canonicalIndices = new int[calls.Count];
            var bySignature = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < calls.Count; i++) {
                canonicalIndices[i] = i;
                if (nonReusableIndices.Contains(i)
                    || !TryBuildToolCallExecutionSignature(calls[i], out var signature)) {
                    continue;
                }

                if (bySignature.TryGetValue(signature, out var existingCanonicalIndex)) {
                    canonicalIndices[i] = existingCanonicalIndex;
                    dedupedCount++;
                    continue;
                }

                bySignature[signature] = i;
            }

            return canonicalIndices;
        }

        private static int[] GetUniqueCanonicalIndices(IReadOnlyList<int> canonicalIndices) {
            var unique = new List<int>(canonicalIndices.Count);
            var seen = new HashSet<int>();
            for (var i = 0; i < canonicalIndices.Count; i++) {
                var canonical = canonicalIndices[i];
                if (seen.Add(canonical)) {
                    unique.Add(canonical);
                }
            }

            return unique.ToArray();
        }

        private static IReadOnlyList<ToolOutput> RehydrateToolOutputsFromCanonical(
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<int> canonicalIndices,
            IReadOnlyDictionary<int, ToolOutput> outputsByCanonical) {
            var outputs = new List<ToolOutput>(calls.Count);
            for (var i = 0; i < calls.Count; i++) {
                var canonicalIndex = canonicalIndices[i];
                if (!outputsByCanonical.TryGetValue(canonicalIndex, out var canonicalOutput)) {
                    outputs.Add(new ToolOutput(calls[i].CallId, ToolOutputEnvelope.Error(
                        errorCode: "tool_output_missing",
                        error: $"Missing canonical tool output for call '{calls[i].CallId}'.",
                        hints: new[] { "Re-run the turn; this indicates an internal orchestration mismatch." },
                        isTransient: true)));
                    continue;
                }

                if (canonicalIndex == i && string.Equals(canonicalOutput.CallId, calls[i].CallId, StringComparison.Ordinal)) {
                    outputs.Add(canonicalOutput);
                    continue;
                }

                outputs.Add(new ToolOutput(calls[i].CallId, canonicalOutput.Output));
            }

            return outputs;
        }

        private static bool TryBuildToolCallExecutionSignature(ToolCall call, out string signature) {
            signature = string.Empty;
            var toolName = (call.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                return false;
            }

            var normalizedToolName = toolName.ToLowerInvariant();
            if (call.Arguments is not null) {
                signature = normalizedToolName + "|" + JsonLite.Serialize(JsonValue.From(call.Arguments));
                return true;
            }

            var normalizedInput = (call.Input ?? string.Empty).Trim();
            signature = normalizedToolName + "|" + normalizedInput;
            return true;
        }

        private bool ShouldRunParallelToolExecution(IReadOnlyList<ToolCall> calls, out string[] mutatingToolNames) {
            mutatingToolNames = Array.Empty<string>();
            if (!_options.ParallelToolCalls || calls.Count <= 1) {
                return false;
            }

            if (_options.AllowMutatingParallelToolCalls) {
                return true;
            }

            var mutating = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < calls.Count; i++) {
                var toolName = (calls[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                if (!_registry.TryGetDefinition(toolName, out var definition)) {
                    continue;
                }

                if (definition.WriteGovernance?.IsWriteCapable != true) {
                    continue;
                }

                if (seen.Add(toolName)) {
                    mutating.Add(toolName);
                }
            }

            if (mutating.Count == 0) {
                return true;
            }

            mutatingToolNames = mutating.ToArray();
            return false;
        }

        private async Task<ToolOutput> ExecuteToolAsync(
            ToolCall call,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? knownHostTargets = null) {
            var hasSessionCacheKey = TryGetSessionToolOutputCacheKey(call, out var sessionCacheKey);
            if (hasSessionCacheKey && _sessionToolOutputCache.TryGetValue(sessionCacheKey, out var cachedOutput)) {
                if (_options.LiveProgress) {
                    _status?.Invoke($"cache-hit: reused {GetToolDisplayName(call.Name)} metadata from session cache.");
                }

                return new ToolOutput(call.CallId, cachedOutput);
            }

            if (!_registry.TryGet(call.Name, out var tool)) {
                var hints = new List<string> { "Run /tools to list available tools." };
                hints.AddRange(ToolExecutionAvailabilityHints.BuildRegistrationHintLines(
                    _registry.GetDefinitions(),
                    hasKnownHostTargets: knownHostTargets is { Count: > 0 }));
                hints.Add("Check that the correct packs are enabled.");
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_not_registered",
                    error: $"Tool '{call.Name}' is not registered.",
                    hints: hints,
                    isTransient: false));
            }

            _registry.TryGetDefinition(call.Name, out var definition);
            var effectiveCall = ApplyKnownHostTargetFallbacks(call, definition, knownHostTargets);
            if (_options.LiveProgress && !ReferenceEquals(effectiveCall, call)) {
                _status?.Invoke($"input-repair: auto-filled host targets for {GetToolDisplayName(call.Name)} from thread context.");
            }

            if (_options.LiveProgress) {
                var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                _status?.Invoke($"running: {GetToolDisplayName(call.Name)}{id}");
            }
            using var toolCts = CreateTimeoutCts(cancellationToken, _options.ToolTimeoutSeconds);
            var toolToken = toolCts?.Token ?? cancellationToken;

            async Task<(bool TimedOut, string? Output)> InvokeToolWithTimeoutGuardAsync(JsonObject? arguments) {
                var invokeTask = tool.InvokeAsync(arguments, toolToken);
                var invocation = await WaitForToolOutputWithTimeoutAsync(
                    invokeTask,
                    _options.ToolTimeoutSeconds,
                    cancellationToken).ConfigureAwait(false);
                if (!invocation.TimedOut) {
                    return invocation;
                }

                if (toolCts is not null && !toolCts.IsCancellationRequested) {
                    try {
                        toolCts.Cancel();
                    } catch (ObjectDisposedException) {
                        // Best-effort cancellation; safe to ignore if the CTS has already been disposed.
                    }
                }

                ObserveToolInvocationFault(invokeTask);
                return invocation;
            }

            try {
                var invocation = await InvokeToolWithTimeoutGuardAsync(effectiveCall.Arguments).ConfigureAwait(false);
                if (invocation.TimedOut) {
                    return BuildToolTimeoutOutput(effectiveCall.CallId, call.Name);
                }

                var result = invocation.Output ?? string.Empty;
                var output = result;
                TryStoreSessionToolOutputCache(sessionCacheKey, output, hasSessionCacheKey);
                return new ToolOutput(effectiveCall.CallId, output);
            } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                return BuildToolTimeoutOutput(effectiveCall.CallId, call.Name);
            } catch (Exception ex) {
                return new ToolOutput(effectiveCall.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_exception",
                    error: $"{ex.GetType().Name}: {ex.Message}",
                    hints: new[] { "Try again. If it keeps failing, re-run with --echo-tool-outputs to capture details." },
                    isTransient: false));
            }
        }

        private ToolOutput BuildToolTimeoutOutput(string callId, string toolName) {
            return new ToolOutput(callId, ToolOutputEnvelope.Error(
                errorCode: "tool_timeout",
                error: $"Tool '{toolName}' timed out after {_options.ToolTimeoutSeconds}s.",
                hints: new[] { "Increase --tool-timeout-seconds, or narrow the query (OU scoping, tighter filters)." },
                isTransient: true));
        }

        private static void ObserveToolInvocationFault(Task<string> invocationTask) {
            _ = invocationTask.ContinueWith(
                static task => {
                    _ = task.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task<(bool TimedOut, string? Output)> WaitForToolOutputWithTimeoutAsync(
            Task<string> invocationTask,
            int timeoutSeconds,
            CancellationToken cancellationToken) {
            if (timeoutSeconds <= 0) {
                return (false, await invocationTask.ConfigureAwait(false));
            }

            try {
                var output = await invocationTask
                    .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)
                    .ConfigureAwait(false);
                return (false, output);
            } catch (TimeoutException) {
                return (true, null);
            }
        }

    }
}
