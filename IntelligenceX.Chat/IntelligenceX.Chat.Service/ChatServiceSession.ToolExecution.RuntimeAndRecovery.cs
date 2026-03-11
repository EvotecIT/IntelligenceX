using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private const string DomainScopeHostGuardrailErrorCode = "domain_scope_host_guardrail";
    private static readonly string[] RecoveryHelperErrorCodeHints = {
        "probe",
        "discovery",
        "connect",
        "unreachable"
    };
    private delegate Task ToolExecutionStatusWriter(string status, ToolCall call, string message);

    private static string BuildToolRoundStartedMessage(int roundNumber, int maxRounds, int callCount, bool parallelTools, bool allowMutatingParallel) {
        var round = Math.Max(1, roundNumber);
        var rounds = Math.Max(round, maxRounds);
        var calls = Math.Max(0, callCount);

        var mode = "sequential";
        if (parallelTools && calls > 1) {
            mode = allowMutatingParallel ? "parallel (mutating override enabled)" : "parallel";
        }

        return $"Tool round {round}/{rounds}: executing {calls} call(s) in {mode} mode.";
    }

    private static string BuildToolRoundCapAppliedMessage(int requestedMaxRounds, int effectiveMaxRounds) {
        var requested = Math.Max(1, requestedMaxRounds);
        var effective = Math.Clamp(effectiveMaxRounds, 1, requested);
        return $"Requested max tool rounds ({requested}) exceeds safety limit. Using {effective} for this turn.";
    }

    private static string BuildToolRoundCompletedMessage(int roundNumber, int maxRounds, int callCount, int failedCalls) {
        var round = Math.Max(1, roundNumber);
        var rounds = Math.Max(round, maxRounds);
        var calls = Math.Max(0, callCount);
        var failed = Math.Clamp(failedCalls, 0, calls);

        if (failed <= 0) {
            return $"Tool round {round}/{rounds} completed: {calls} call(s), all succeeded.";
        }

        return $"Tool round {round}/{rounds} completed: {calls} call(s), {failed} failed.";
    }

    private static string BuildToolRoundLimitReachedMessage(int maxRounds, int totalToolCalls, int totalToolOutputs) {
        var rounds = Math.Max(1, maxRounds);
        var calls = Math.Max(0, totalToolCalls);
        var outputs = Math.Max(0, totalToolOutputs);
        return $"Reached max tool rounds ({rounds}). Executed {calls} call(s) and collected {outputs} output(s).";
    }

    internal Task WriteToolRoundStartedStatusAsync(StreamWriter writer, string requestId, string threadId, int roundNumber, int maxRounds, int callCount,
        bool parallelTools, bool allowMutatingParallel) {
        return TryWriteStatusAsync(
            writer,
            requestId,
            threadId,
            status: ChatStatusCodes.ToolRoundStarted,
            message: BuildToolRoundStartedMessage(roundNumber, maxRounds, callCount, parallelTools, allowMutatingParallel));
    }

    internal Task WriteToolRoundCompletedStatusAsync(StreamWriter writer, string requestId, string threadId, int roundNumber, int maxRounds, int callCount,
        int failedCalls) {
        return TryWriteStatusAsync(
            writer,
            requestId,
            threadId,
            status: ChatStatusCodes.ToolRoundCompleted,
            message: BuildToolRoundCompletedMessage(roundNumber, maxRounds, callCount, failedCalls));
    }

    internal Task WriteToolRoundLimitReachedStatusAsync(StreamWriter writer, string requestId, string threadId, int maxRounds, int totalToolCalls,
        int totalToolOutputs) {
        return TryWriteStatusAsync(
            writer,
            requestId,
            threadId,
            status: ChatStatusCodes.ToolRoundLimitReached,
            message: BuildToolRoundLimitReachedMessage(maxRounds, totalToolCalls, totalToolOutputs));
    }

    private static string BuildToolBatchRecoveringMessage(int recoveredCalls, int totalCalls) {
        var recovered = Math.Max(0, recoveredCalls);
        var total = Math.Max(0, totalCalls);
        return $"Retrying {recovered} transient tool failure(s) at low concurrency ({total} total calls).";
    }

    private static string BuildToolBatchRecoveredMessage(int recoveredCalls, int remainingFailures) {
        var recovered = Math.Max(0, recoveredCalls);
        var failures = Math.Max(0, remainingFailures);
        if (failures <= 0) {
            return $"Low-concurrency recovery completed ({recovered} retried).";
        }

        return $"Low-concurrency recovery completed ({recovered} retried, {failures} failure(s) remain).";
    }

    private static string BuildToolHeartbeatMessage(string? toolName, int elapsedSeconds) {
        var label = (toolName ?? string.Empty).Trim();
        if (label.Length == 0) {
            label = "tool";
        }

        return $"Still running {label} ({Math.Max(1, elapsedSeconds)}s)...";
    }

    private static string BuildPreferredAlternateEngineStatusMessage(string engineId) {
        var normalizedEngineId = (engineId ?? string.Empty).Trim();
        if (normalizedEngineId.Length == 0) {
            normalizedEngineId = "alternate";
        }

        return $"Using remembered healthy backend '{normalizedEngineId}' for this tool call.";
    }

    private static string BuildAlternateEngineRetryStatusMessage(string engineId) {
        var normalizedEngineId = (engineId ?? string.Empty).Trim();
        if (normalizedEngineId.Length == 0) {
            normalizedEngineId = "alternate";
        }

        return $"Retrying with alternate backend '{normalizedEngineId}'.";
    }

    private static string BuildAutomaticAlternateEngineRetryStatusMessage(string engineId) {
        var normalizedEngineId = (engineId ?? string.Empty).Trim();
        if (normalizedEngineId.Length == 0) {
            normalizedEngineId = "remembered";
        }

        return $"Backend '{normalizedEngineId}' failed; retrying with automatic backend selection.";
    }

    private static int CountFailedToolOutputs(IReadOnlyList<ToolOutputDto> outputs) {
        if (outputs.Count == 0) {
            return 0;
        }

        var failed = 0;
        for (var i = 0; i < outputs.Count; i++) {
            if (outputs[i].Ok is false) {
                failed++;
            }
        }

        return failed;
    }

    private async Task<ToolOutputDto> ExecuteToolWithStatusAsync(StreamWriter writer, string requestId, string threadId, ToolCall call,
        int toolTimeoutSeconds, string userRequest, CancellationToken cancellationToken) {
        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: ChatStatusCodes.ToolRunning,
                toolName: call.Name,
                toolCallId: call.CallId)
            .ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var executeTask = ExecuteToolAsync(
            threadId,
            userRequest,
            call,
            toolTimeoutSeconds,
            cancellationToken,
            (status, statusCall, message) => TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: status,
                toolName: statusCall.Name,
                toolCallId: statusCall.CallId,
                message: message));
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var canceledByTurn = false;
        while (!executeTask.IsCompleted) {
            // Use a non-cancelable heartbeat delay and a separate cancellation task.
            // This avoids a cancellation race where Task.Delay(..., token) can complete immediately
            // and trigger a tight heartbeat loop while the tool task is still finishing.
            var heartbeatDelayTask = Task.Delay(ToolHeartbeatInterval);
            var completedTask = await Task.WhenAny(executeTask, heartbeatDelayTask, cancellationTask).ConfigureAwait(false);
            if (ReferenceEquals(completedTask, executeTask)) {
                break;
            }

            if (ReferenceEquals(completedTask, cancellationTask)) {
                canceledByTurn = true;
                break;
            }

            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.ToolHeartbeat,
                    toolName: call.Name,
                    toolCallId: call.CallId,
                    durationMs: sw.ElapsedMilliseconds,
                    message: BuildToolHeartbeatMessage(call.Name, Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds))))
                .ConfigureAwait(false);
        }

        if (canceledByTurn && !executeTask.IsCompleted) {
            ObserveBackgroundToolCompletion(executeTask);
            sw.Stop();
            var canceledOutput = BuildToolOutputDto(
                call.CallId,
                ToolOutputEnvelope.Error(
                    errorCode: "tool_canceled",
                    error: $"Tool '{call.Name}' was canceled before completion.",
                    hints: new[] { "Retry the request when ready." },
                    isTransient: true));
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.ToolCanceled,
                    toolName: call.Name,
                    toolCallId: call.CallId,
                    durationMs: sw.ElapsedMilliseconds,
                    message: BuildToolCanceledMessage(call.Name))
                .ConfigureAwait(false);
            return canceledOutput;
        }

        var output = await executeTask.ConfigureAwait(false);
        sw.Stop();
        await TryWriteToolRecoveredStatusAsync(writer, requestId, threadId, call, output).ConfigureAwait(false);
        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: ChatStatusCodes.ToolCompleted,
                toolName: call.Name,
                toolCallId: call.CallId,
                durationMs: sw.ElapsedMilliseconds)
            .ConfigureAwait(false);
        return output;
    }

    private async Task TryWriteToolRecoveredStatusAsync(StreamWriter writer, string requestId, string threadId, ToolCall call, ToolOutputDto output) {
        if (!WasProjectionFallbackApplied(output)) {
            return;
        }

        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: ChatStatusCodes.ToolRecovered,
                toolName: call.Name,
                toolCallId: call.CallId,
                message: ProjectionFallbackRecoveredStatusMessage)
            .ConfigureAwait(false);
    }

    private static string BuildToolCanceledMessage(string? toolName) {
        var label = (toolName ?? string.Empty).Trim();
        if (label.Length == 0) {
            label = "tool";
        }

        return $"Canceled {label} due to request cancellation.";
    }

    private static void ObserveBackgroundToolCompletion(Task task) {
        _ = task.ContinueWith(
            static completedTask => {
                // Observe faulted task exceptions so cancellation short-circuiting does not surface
                // unobserved background exceptions later on finalizer threads.
                _ = completedTask.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<ToolOutputDto> ExecuteToolAsync(string threadId, string userRequest, ToolCall call, int toolTimeoutSeconds,
        CancellationToken cancellationToken, ToolExecutionStatusWriter? statusWriter = null) {
        if (!_registry.TryGet(call.Name, out var tool)) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_not_registered",
                error: $"Tool '{call.Name}' is not registered.",
                hints: new[] { "Call list_tools to list available tools.", "Check that the correct packs are enabled." },
                isTransient: false);

            return BuildToolOutputDto(call.CallId, output);
        }

        if (TryBuildDomainIntentHostScopeGuardrailOutput(threadId, userRequest, call, out var guardrailOutput)) {
            return guardrailOutput;
        }

        // Retry profile wiring is enforced in this execution loop.
        ToolDefinition? toolDefinition = null;
        if (_registry.TryGetDefinition(call.Name, out var registeredDefinition) && registeredDefinition is not null) {
            toolDefinition = ToolSelectionMetadata.Enrich(registeredDefinition, tool.GetType());
        }
        var profile = ResolveRetryProfile(toolDefinition);
        var currentCall = call;
        var projectionFallbackAttempted = false;
        var attemptedAlternateEngineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recoveryHelperAttempted = false;
        var preferredAutomaticFallbackPending = false;
        var preferredAutomaticFallbackAttempted = false;
        var preferredAlternateEngineId = string.Empty;
        ToolOutputDto? lastFailure = null;
        if (TryBuildPreferredHealthyAlternateEngineCall(
                threadId,
                currentCall,
                toolDefinition,
                profile,
                out var preferredAlternateEngineCall,
                out preferredAlternateEngineId)) {
            attemptedAlternateEngineIds.Add(preferredAlternateEngineId);
            currentCall = preferredAlternateEngineCall;
            preferredAutomaticFallbackPending = true;
            await TryWriteToolExecutionTransitionStatusAsync(
                    statusWriter,
                    ChatStatusCodes.ToolCall,
                    currentCall,
                    BuildPreferredAlternateEngineStatusMessage(preferredAlternateEngineId))
                .ConfigureAwait(false);
        }

        for (var attemptIndex = 0; attemptIndex < profile.MaxAttempts; attemptIndex++) {
            var output = await ExecuteToolAttemptAsync(tool, currentCall, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (TryResolveTrackedAlternateEngineId(currentCall, toolDefinition, profile, out var trackedAlternateEngineId)) {
                RememberAlternateEngineOutcome(threadId, currentCall.Name, trackedAlternateEngineId, output);
            }
            if (!projectionFallbackAttempted
                && TryBuildProjectionArgsFallbackCall(currentCall, output, out var fallbackCall, out var fallbackInfo)) {
                projectionFallbackAttempted = true;
                currentCall = fallbackCall;

                // One deterministic self-heal pass for view-projection failures: retry with bare/default view args.
                var fallbackOutput = await ExecuteToolAttemptAsync(tool, currentCall, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                output = AttachProjectionFallbackMetadata(fallbackOutput, fallbackInfo);
                if (output.Ok is true) {
                    return output;
                }
            }

            var shouldRetry = ShouldRetryToolCall(output, profile, attemptIndex);
            if (!shouldRetry
                && preferredAutomaticFallbackPending
                && !preferredAutomaticFallbackAttempted
                && TryBuildAutomaticAlternateEngineRetryCall(
                    call,
                    currentCall,
                    toolDefinition,
                    profile,
                    out var automaticAlternateEngineCall)) {
                preferredAutomaticFallbackAttempted = true;
                preferredAutomaticFallbackPending = false;
                currentCall = automaticAlternateEngineCall;
                await TryWriteToolExecutionTransitionStatusAsync(
                        statusWriter,
                        ChatStatusCodes.ToolCall,
                        currentCall,
                        BuildAutomaticAlternateEngineRetryStatusMessage(preferredAlternateEngineId))
                    .ConfigureAwait(false);
                continue;
            }

            preferredAutomaticFallbackPending = false;
            if (!shouldRetry) {
                return output;
            }

            lastFailure = output;
            if (TryBuildAlternateEngineFallbackCall(
                    threadId,
                    currentCall,
                    toolDefinition,
                    profile,
                    attemptedAlternateEngineIds,
                    out var alternateEngineCall,
                    out var selectedAlternateEngineId)) {
                attemptedAlternateEngineIds.Add(selectedAlternateEngineId);
                currentCall = alternateEngineCall;
                await TryWriteToolExecutionTransitionStatusAsync(
                        statusWriter,
                        ChatStatusCodes.ToolCall,
                        currentCall,
                        BuildAlternateEngineRetryStatusMessage(selectedAlternateEngineId))
                    .ConfigureAwait(false);
                continue;
            }

            if (ShouldAttemptRecoveryHelperTools(output, profile, recoveryHelperAttempted)) {
                recoveryHelperAttempted = true;
                await ExecuteRecoveryHelperToolsAsync(threadId, userRequest, currentCall, profile, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            }

            if (profile.DelayBaseMs > 0) {
                var delayMs = Math.Min(800, profile.DelayBaseMs * (attemptIndex + 1));
                try {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return output;
                }
            }
        }

        return lastFailure ?? await ExecuteToolAttemptAsync(tool, call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    private static Task TryWriteToolExecutionTransitionStatusAsync(
        ToolExecutionStatusWriter? statusWriter,
        string status,
        ToolCall call,
        string message) {
        if (statusWriter is null || string.IsNullOrWhiteSpace(message)) {
            return Task.CompletedTask;
        }

        return statusWriter(status, call, message);
    }


    private async Task<ToolOutputDto> ExecuteToolAttemptAsync(ITool tool, ToolCall call, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        using var toolCts = CreateTimeoutCts(cancellationToken, toolTimeoutSeconds);
        var toolToken = toolCts?.Token ?? cancellationToken;
        try {
            var output = await tool.InvokeAsync(call.Arguments, toolToken).ConfigureAwait(false);
            var text = output ?? string.Empty;
            if (_options.Redact) {
                text = RedactText(text);
            }
            return BuildToolOutputDto(call.CallId, text);
        } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_timeout",
                error: $"Tool '{call.Name}' timed out after {toolTimeoutSeconds}s.",
                hints: new[] { "Increase toolTimeoutSeconds, or narrow the query (OU scoping, tighter filters)." },
                isTransient: true);
            return BuildToolOutputDto(call.CallId, output);
        } catch (Exception ex) {
            var isTransient = IsLikelyTransientToolException(ex);
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_exception",
                error: $"{ex.GetType().Name}: {ex.Message}",
                hints: new[] {
                    "Try again. If it keeps failing, narrow the query and capture tool args/output.",
                    "Check tool parameter names and value types in the tool details panel."
                },
                isTransient: isTransient);
            return BuildToolOutputDto(call.CallId, output);
        }
    }

    private static ToolOutputDto BuildToolOutputDto(string callId, string output) {
        var meta = TryExtractToolOutputMetadata(output);
        return new ToolOutputDto {
            CallId = callId,
            Output = output,
            Ok = meta.Ok,
            ErrorCode = meta.ErrorCode,
            Error = meta.Error,
            Hints = meta.Hints,
            IsTransient = meta.IsTransient,
            SummaryMarkdown = meta.SummaryMarkdown,
            MetaJson = meta.MetaJson,
            RenderJson = meta.RenderJson,
            FailureJson = meta.FailureJson
        };
    }

    private async Task ExecuteRecoveryHelperToolsAsync(
        string threadId,
        string userRequest,
        ToolCall failedCall,
        ToolRetryProfile profile,
        int toolTimeoutSeconds,
        CancellationToken cancellationToken) {
        if (profile.RecoveryToolNames is not { Count: > 0 }) {
            return;
        }

        var suppressedHelperToolNames = SnapshotRecentHostBootstrapFailureToolNames(threadId);
        var orderedHelperToolNames = OrderBootstrapToolNamesByHealth(profile.RecoveryToolNames, suppressedHelperToolNames);
        for (var i = 0; i < orderedHelperToolNames.Length; i++) {
            var helperToolName = orderedHelperToolNames[i];

            if (!TryBuildRecoveryHelperInvocation(failedCall, helperToolName, out var helperTool, out var helperCall)) {
                continue;
            }

            if (TryBuildDomainIntentHostScopeGuardrailOutput(threadId, userRequest, helperCall, out _)) {
                continue;
            }

            var helperOutput = await ExecuteToolAttemptAsync(helperTool, helperCall, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (IsSuccessfulToolOutput(helperOutput)) {
                ClearHostBootstrapFailure(threadId, helperCall.Name);
                return;
            }

            RememberHostBootstrapFailure(threadId, helperCall.Name, HostBootstrapFailureKindRecoveryHelper, helperOutput);
        }
    }

    private bool TryBuildRecoveryHelperInvocation(ToolCall failedCall, string? helperToolName, out ITool helperTool, out ToolCall helperCall) {
        helperTool = default!;
        helperCall = default!;

        var normalizedHelperToolName = (helperToolName ?? string.Empty).Trim();
        if (normalizedHelperToolName.Length == 0
            || string.Equals(normalizedHelperToolName, failedCall.Name, StringComparison.OrdinalIgnoreCase)
            || !_registry.TryGet(normalizedHelperToolName, out helperTool)
            || !_registry.TryGetDefinition(normalizedHelperToolName, out var helperDefinition)
            || helperDefinition is null
            || helperDefinition.WriteGovernance?.IsWriteCapable == true
            || !TryBuildRecoveryHelperArguments(failedCall, helperDefinition, out var helperArguments)) {
            return false;
        }

        var callId = BuildHostGeneratedToolCallId("host_recovery_helper", normalizedHelperToolName);
        var serializedArguments = JsonLite.Serialize(helperArguments);
        var raw = new JsonObject(StringComparer.Ordinal)
            .Add("type", "tool_call")
            .Add("call_id", callId)
            .Add("name", normalizedHelperToolName)
            .Add("arguments", serializedArguments);

        helperCall = new ToolCall(
            callId: callId,
            name: normalizedHelperToolName,
            input: serializedArguments,
            arguments: helperArguments,
            raw: raw);
        return true;
    }

    private static bool TryBuildRecoveryHelperArguments(ToolCall failedCall, ToolDefinition helperDefinition, out JsonObject helperArguments) {
        helperArguments = new JsonObject(StringComparer.Ordinal);
        if (helperDefinition?.Parameters is null) {
            return true;
        }

        var sourceArguments = ResolveRecoveryHelperSourceArguments(failedCall);
        var properties = helperDefinition.Parameters.GetObject("properties");
        if (sourceArguments is not null && properties is not null) {
            foreach (var property in properties) {
                if (sourceArguments.TryGetValue(property.Key, out var value) && value is not null) {
                    helperArguments.Add(property.Key, value);
                }
            }
        }

        var required = helperDefinition.Parameters.GetArray("required");
        if (required is not { Count: > 0 }) {
            return true;
        }

        for (var i = 0; i < required.Count; i++) {
            var requiredName = (required[i]?.AsString() ?? string.Empty).Trim();
            if (requiredName.Length == 0) {
                continue;
            }

            if (!helperArguments.TryGetValue(requiredName, out _)) {
                helperArguments = new JsonObject(StringComparer.Ordinal);
                return false;
            }
        }

        return true;
    }

    private static JsonObject? ResolveRecoveryHelperSourceArguments(ToolCall failedCall) {
        if (failedCall.Arguments is not null) {
            return failedCall.Arguments;
        }

        var input = (failedCall.Input ?? string.Empty).Trim();
        if (input.Length == 0) {
            return null;
        }

        try {
            return JsonLite.Parse(input)?.AsObject();
        } catch {
            return null;
        }
    }

    private static bool ShouldRetryToolCall(ToolOutputDto output, ToolRetryProfile profile, int attemptIndex) {
        // attemptIndex is zero-based current attempt. We can only retry when there is another slot left.
        if (attemptIndex + 1 >= profile.MaxAttempts) {
            return false;
        }
        if (output.Ok is true) {
            return false;
        }

        if (string.Equals(output.ErrorCode, "tool_not_registered", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (string.Equals(output.ErrorCode, DomainScopeHostGuardrailErrorCode, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (output.IsTransient is true && string.IsNullOrWhiteSpace(output.ErrorCode)) {
            // Some providers/tools mark failures transient without a structured error code.
            // Keep retry behavior resilient for those adapters when retry slots remain.
            return true;
        }
        if (string.Equals(output.ErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase) && profile.RetryOnTimeout) {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(output.ErrorCode)) {
            var code = output.ErrorCode.Trim();
            if (IsRetryableErrorCode(profile, code)) {
                return true;
            }

            var transientTransportCode = code.Contains("transport", StringComparison.OrdinalIgnoreCase)
                                         || code.Contains("transient", StringComparison.OrdinalIgnoreCase)
                                         || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
            if (transientTransportCode && profile.RetryOnTransport) {
                return true;
            }
        }
        if (IsLikelyPermanentToolFailure(output)) {
            return false;
        }
        return output.IsTransient is true;
    }

    private static bool ShouldAttemptRecoveryHelperTools(ToolOutputDto output, ToolRetryProfile profile, bool recoveryHelperAttempted) {
        if (recoveryHelperAttempted
            || profile.RecoveryToolNames is not { Count: > 0 }
            || output.Ok is true
            || string.Equals(output.ErrorCode, "tool_not_registered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(output.ErrorCode, DomainScopeHostGuardrailErrorCode, StringComparison.OrdinalIgnoreCase)
            || IsLikelyPermanentToolFailure(output)) {
            return false;
        }

        var normalizedCode = NormalizeRetryableErrorCode(output.ErrorCode);
        if (normalizedCode.Length == 0) {
            return false;
        }

        if (string.Equals(normalizedCode, "timeout", StringComparison.OrdinalIgnoreCase)) {
            return profile.RetryOnTimeout;
        }

        if (IsTransportRetryableCode(normalizedCode)) {
            return profile.RetryOnTransport;
        }

        for (var i = 0; i < RecoveryHelperErrorCodeHints.Length; i++) {
            if (normalizedCode.Contains(RecoveryHelperErrorCodeHints[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyPermanentToolFailure(ToolOutputDto output) {
        var code = (output.ErrorCode ?? string.Empty).Trim();
        if (code.Length == 0) {
            return false;
        }

        return code.Contains("invalid", StringComparison.OrdinalIgnoreCase)
               || code.Contains("argument", StringComparison.OrdinalIgnoreCase)
               || code.Contains("validation", StringComparison.OrdinalIgnoreCase)
               || code.Contains("permission", StringComparison.OrdinalIgnoreCase)
               || code.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
               || code.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
               || code.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, DomainScopeHostGuardrailErrorCode, StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "auth_failed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "authentication_failed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "authorization_failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildToolFailureSearchText(ToolOutputDto output) {
        var parts = new List<string>(8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFailureSearchPart(parts, seen, output.ErrorCode);
        AddFailureSearchPart(parts, seen, output.Error);
        AppendFailureSearchContext(parts, seen, output.FailureJson);
        AppendFailureSearchContext(parts, seen, output.MetaJson);
        AppendFailureSearchContext(parts, seen, output.Output, includeRawFallback: false);
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static void AppendFailureSearchContext(List<string> parts, HashSet<string> seen, string? rawText, bool includeRawFallback = true) {
        if (string.IsNullOrWhiteSpace(rawText)) {
            return;
        }

        if (TryAppendFailureJsonSignals(parts, seen, rawText!)) {
            return;
        }

        if (includeRawFallback) {
            AddFailureSearchPart(parts, seen, rawText);
        }
    }

    private static bool TryAppendFailureJsonSignals(List<string> parts, HashSet<string> seen, string rawText) {
        try {
            var parsed = JsonLite.Parse(rawText);
            var obj = parsed?.AsObject();
            if (obj is null) {
                return false;
            }

            var before = parts.Count;
            AppendFailureSignalsFromObject(parts, seen, obj);
            return parts.Count > before;
        } catch {
            return false;
        }
    }

    private static void AppendFailureSignalsFromObject(List<string> parts, HashSet<string> seen, JsonObject obj) {
        AddFailureSearchPart(parts, seen, obj.GetString("error_code"));
        AddFailureSearchPart(parts, seen, obj.GetString("code"));
        AddFailureSearchPart(parts, seen, obj.GetString("error"));
        AddFailureSearchPart(parts, seen, obj.GetString("message"));
        AddFailureSearchPart(parts, seen, obj.GetString("reason"));
        AddFailureSearchPart(parts, seen, obj.GetString("exception"));
        AddFailureSearchPart(parts, seen, obj.GetString("exception_type"));
        AddFailureSearchPart(parts, seen, obj.GetString("exceptionType"));
        AddFailureSearchPart(parts, seen, obj.GetString("details"));

        try {
            if (obj.GetObject("failure") is JsonObject failureObj) {
                AddFailureSearchPart(parts, seen, failureObj.GetString("code"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("error"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("message"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("reason"));
            }
        } catch {
            // best-effort extraction only
        }

        try {
            if (obj.GetObject("meta") is JsonObject metaObj) {
                AddFailureSearchPart(parts, seen, metaObj.GetString("error_code"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("error"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("message"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("reason"));
            }
        } catch {
            // best-effort extraction only
        }
    }

    private static void AddFailureSearchPart(List<string> parts, HashSet<string> seen, string? rawText) {
        var compact = CompactFailureText(rawText);
        if (compact.Length == 0) {
            return;
        }

        if (seen.Add(compact)) {
            parts.Add(compact);
        }
    }

    private static string CompactFailureText(string? rawText) {
        if (string.IsNullOrWhiteSpace(rawText)) {
            return string.Empty;
        }

        var compact = CollapseWhitespace(rawText);
        const int maxLength = 768;
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static bool IsLikelyTransientToolException(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }

        if (HasKnownTransientExceptionInChain(ex)) {
            return true;
        }

        return false;
    }

    private static bool HasKnownTransientExceptionInChain(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            if (current is OperationCanceledException) {
                return false;
            }
            if (current is TimeoutException || current is IOException) {
                return true;
            }

            var name = current.GetType().FullName ?? current.GetType().Name;
            if (name.IndexOf("SocketException", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("HttpRequestException", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static ToolRetryProfile ResolveRetryProfile(ToolDefinition? definition) {
        var recovery = definition?.Recovery;
        if (recovery is not { IsRecoveryAware: true } || !recovery.SupportsTransientRetry || recovery.MaxRetryAttempts <= 0) {
            return new ToolRetryProfile(
                MaxAttempts: 1,
                DelayBaseMs: 0,
                RetryOnTimeout: false,
                RetryOnTransport: false,
                RetryableErrorCodes: Array.Empty<string>(),
                RecoveryToolNames: NormalizeRecoveryToolNames(recovery?.RecoveryToolNames),
                AlternateEngineIds: NormalizeAlternateEngineIds(recovery));
        }

        var retryableCodes = NormalizeRetryableErrorCodes(recovery.RetryableErrorCodes);
        var recoveryToolNames = NormalizeRecoveryToolNames(recovery.RecoveryToolNames);
        var alternateEngineIds = NormalizeAlternateEngineIds(recovery);
        var retryOnTimeout = retryableCodes.Any(static code => IsTimeoutRetryableCode(code));
        var retryOnTransport = retryableCodes.Any(static code => IsTransportRetryableCode(code));
        var maxAttempts = Math.Clamp(recovery.MaxRetryAttempts + 1, 2, 6);
        var delayBaseMs = ResolveRetryDelayBaseMs(maxAttempts, retryOnTransport);
        return new ToolRetryProfile(
            MaxAttempts: maxAttempts,
            DelayBaseMs: delayBaseMs,
            RetryOnTimeout: retryOnTimeout,
            RetryOnTransport: retryOnTransport,
            RetryableErrorCodes: retryableCodes,
            RecoveryToolNames: recoveryToolNames,
            AlternateEngineIds: alternateEngineIds);
    }

    private static string[] NormalizeRetryableErrorCodes(IReadOnlyList<string>? values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var token = NormalizeRetryableErrorCode(values[i]);
            if (token.Length == 0 || !seen.Add(token)) {
                continue;
            }

            normalized.Add(token);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    private static string[] NormalizeRecoveryToolNames(IReadOnlyList<string>? values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var token = (values[i] ?? string.Empty).Trim();
            if (token.Length == 0 || !seen.Add(token)) {
                continue;
            }

            normalized.Add(token);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    private static string[] NormalizeAlternateEngineIds(ToolRecoveryContract? recovery) {
        if (recovery is not { SupportsAlternateEngines: true } || recovery.AlternateEngineIds.Count == 0) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(recovery.AlternateEngineIds.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < recovery.AlternateEngineIds.Count; i++) {
            var token = (recovery.AlternateEngineIds[i] ?? string.Empty).Trim();
            if (token.Length == 0 || !seen.Add(token)) {
                continue;
            }

            normalized.Add(token);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    private static string NormalizeRetryableErrorCode(string? value) {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        return normalized switch {
            "tool_timeout" => "timeout",
            "transport" => "transport_unavailable",
            _ => normalized
        };
    }

    private static bool IsTimeoutRetryableCode(string code) {
        return string.Equals(code, "timeout", StringComparison.OrdinalIgnoreCase)
               || code.EndsWith("_timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransportRetryableCode(string code) {
        return string.Equals(code, "transport_unavailable", StringComparison.OrdinalIgnoreCase)
               || code.Contains("transport", StringComparison.OrdinalIgnoreCase)
               || code.Contains("transient", StringComparison.OrdinalIgnoreCase)
               || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
               || code.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetryableErrorCode(ToolRetryProfile profile, string errorCode) {
        var normalizedCode = NormalizeRetryableErrorCode(errorCode);
        if (normalizedCode.Length == 0) {
            return false;
        }

        for (var i = 0; i < profile.RetryableErrorCodes.Count; i++) {
            var candidate = profile.RetryableErrorCodes[i];
            if (candidate.Length == 0) {
                continue;
            }

            if (string.Equals(normalizedCode, candidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (string.Equals(candidate, "timeout", StringComparison.OrdinalIgnoreCase)
                && IsTimeoutRetryableCode(normalizedCode)) {
                return true;
            }

            if (string.Equals(candidate, "transport_unavailable", StringComparison.OrdinalIgnoreCase)
                && IsTransportRetryableCode(normalizedCode)) {
                return true;
            }
        }

        return false;
    }

    private static int ResolveRetryDelayBaseMs(int maxAttempts, bool retryOnTransport) {
        // Keep Chat generic: derive backoff from declared retry budget and transient transport capability.
        if (maxAttempts <= 1) {
            return 0;
        }

        var budgetOffset = Math.Max(0, maxAttempts - 2);
        var baseDelay = 120 + (budgetOffset * 30);
        if (retryOnTransport) {
            baseDelay += 40;
        }

        return Math.Clamp(baseDelay, 90, 320);
    }

}
