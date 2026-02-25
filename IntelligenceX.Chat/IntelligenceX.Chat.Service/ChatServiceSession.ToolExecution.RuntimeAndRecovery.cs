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
        var executeTask = ExecuteToolAsync(threadId, userRequest, call, toolTimeoutSeconds, cancellationToken);
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
        CancellationToken cancellationToken) {
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
        var profile = ResolveRetryProfile(call.Name);
        var currentCall = call;
        var projectionFallbackAttempted = false;
        ToolOutputDto? lastFailure = null;
        for (var attemptIndex = 0; attemptIndex < profile.MaxAttempts; attemptIndex++) {
            var output = await ExecuteToolAttemptAsync(tool, currentCall, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
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

            if (!ShouldRetryToolCall(output, profile, attemptIndex)) {
                return output;
            }

            lastFailure = output;
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

    private bool TryBuildDomainIntentHostScopeGuardrailOutput(string threadId, string userRequest, ToolCall call, out ToolOutputDto output) {
        output = null!;

        if (!TryGetCurrentDomainIntentFamily(threadId, out var family)
            || !string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
            || !IsDomainIntentHostGuardrailCandidateTool(call.Name)) {
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
            errorCode: "domain_scope_host_guardrail",
            error:
            $"Blocked '{call.Name}' host target(s) in ad_domain scope because they match prior public_domain evidence: {blockedPreview}.",
            hints: new[] {
                "Run ad_scope_discovery or ad_domain_controllers first, then retry AD/eventlog checks with AD-derived hosts.",
                "If this exact host is intended, include it explicitly in this turn's user request."
            },
            isTransient: true);
        output = BuildToolOutputDto(call.CallId, guardrail);
        return true;
    }

    private static bool IsDomainIntentHostGuardrailCandidateTool(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return false;
        }

        return normalizedToolName.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
               || normalizedToolName.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase);
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

    private static ToolRetryProfile ResolveRetryProfile(string? toolName) {
        var normalized = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("ad_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 200, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("eventlog_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 150, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("system_", StringComparison.Ordinal)
            || normalized.StartsWith("wsl_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 120, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("fs_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 90, RetryOnTimeout: true, RetryOnTransport: false);
        }

        return new ToolRetryProfile(MaxAttempts: 1, DelayBaseMs: 0, RetryOnTimeout: false, RetryOnTransport: false);
    }

}
