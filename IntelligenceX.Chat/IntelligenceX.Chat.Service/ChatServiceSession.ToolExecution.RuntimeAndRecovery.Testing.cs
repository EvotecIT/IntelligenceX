using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal readonly record struct RetryProfileSnapshot(
        int MaxAttempts,
        int DelayBaseMs,
        bool RetryOnTimeout,
        bool RetryOnTransport,
        string[] RetryableErrorCodes,
        string[] RecoveryToolNames,
        string[] AlternateEngineIds);

    internal static RetryProfileSnapshot ResolveRetryProfileForTesting(ToolDefinition? definition) {
        var profile = ResolveRetryProfile(definition);
        var retryableCodes = profile.RetryableErrorCodes is { Count: > 0 }
            ? profile.RetryableErrorCodes.ToArray()
            : Array.Empty<string>();
        var recoveryToolNames = profile.RecoveryToolNames is { Count: > 0 }
            ? profile.RecoveryToolNames.ToArray()
            : Array.Empty<string>();
        var alternateEngineIds = profile.AlternateEngineIds is { Count: > 0 }
            ? profile.AlternateEngineIds.ToArray()
            : Array.Empty<string>();
        return new RetryProfileSnapshot(
            MaxAttempts: profile.MaxAttempts,
            DelayBaseMs: profile.DelayBaseMs,
            RetryOnTimeout: profile.RetryOnTimeout,
            RetryOnTransport: profile.RetryOnTransport,
            RetryableErrorCodes: retryableCodes,
            RecoveryToolNames: recoveryToolNames,
            AlternateEngineIds: alternateEngineIds);
    }

    internal static bool ShouldRetryToolCallForTesting(ToolOutputDto output, RetryProfileSnapshot retryProfile, int attemptIndex) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return ShouldRetryToolCall(output, profile, attemptIndex);
    }

    internal static bool ShouldAttemptRecoveryHelperToolsForTesting(
        ToolOutputDto output,
        RetryProfileSnapshot retryProfile,
        bool recoveryHelperAttempted = false) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return ShouldAttemptRecoveryHelperTools(output, profile, recoveryHelperAttempted);
    }

    internal static bool TryBuildAlternateEngineFallbackCallForTesting(
        ToolCall call,
        ToolDefinition? definition,
        RetryProfileSnapshot retryProfile,
        out ToolCall fallbackCall,
        out string selectedEngineId) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return TryBuildAlternateEngineFallbackCall(call, definition, profile, out fallbackCall, out selectedEngineId);
    }

    internal bool TryBuildPreferredHealthyAlternateEngineCallForTesting(
        string threadId,
        ToolCall call,
        ToolDefinition? definition,
        RetryProfileSnapshot retryProfile,
        out ToolCall preferredCall,
        out string selectedEngineId) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return TryBuildPreferredHealthyAlternateEngineCall(threadId, call, definition, profile, out preferredCall, out selectedEngineId);
    }

    internal Task<ToolOutputDto> ExecuteToolAsyncForTesting(
        string threadId,
        string userRequest,
        ToolCall call,
        int toolTimeoutSeconds,
        CancellationToken cancellationToken) {
        return ExecuteToolAsync(threadId, userRequest, call, toolTimeoutSeconds, cancellationToken);
    }

    internal void SetStartupToolingBootstrapTaskForTesting(Task? startupToolingBootstrapTask) {
        Volatile.Write(ref _startupToolingBootstrapTask, startupToolingBootstrapTask);
    }

    internal Task? GetStartupToolingBootstrapTaskForTesting() {
        return Volatile.Read(ref _startupToolingBootstrapTask);
    }

    internal void SetCachedToolDefinitionsForTesting(IReadOnlyList<ToolDefinitionDto> toolDefinitions) {
        ArgumentNullException.ThrowIfNull(toolDefinitions);
        Volatile.Write(ref _cachedToolDefinitions, toolDefinitions.ToArray());
    }

    internal string[] ResolveDeferredActivationPackIdsForChatRequestForTesting(string requestText, ChatRequestOptions? options = null) {
        return TryResolveDeferredActivationPackIdsForChatRequest(requestText, options, out var packIds)
            ? packIds
            : Array.Empty<string>();
    }

    internal Task<bool> TryPrepareDeferredChatToolingForRequestAsyncForTesting(ChatRequest request, CancellationToken cancellationToken = default) {
        return TryPrepareDeferredChatToolingForRequestAsync(new StreamWriter(Stream.Null) { AutoFlush = true }, request.RequestId, request, cancellationToken);
    }

    internal async Task<ChatStatusMessage[]> CaptureDeferredChatToolingStatusesForTesting(
        ChatRequest request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true }) {
            await TryPrepareDeferredChatToolingForRequestAsync(writer, request.RequestId, request, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        stream.Position = 0;
        var statuses = new List<ChatStatusMessage>();
        using var reader = new StreamReader(stream, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line) {
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            var message = JsonSerializer.Deserialize(line, ChatServiceJsonContext.Default.ChatServiceMessage);
            if (message is ChatStatusMessage statusMessage) {
                statuses.Add(statusMessage);
            }
        }

        return statuses.ToArray();
    }

    internal string[] GetRegisteredToolNamesForTesting() {
        return _registry.GetDefinitions()
            .Select(static definition => definition.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    internal ToolDefinition[] GetRegisteredToolDefinitionsForTesting() {
        return _registry.GetDefinitions().ToArray();
    }

    internal static bool TryBuildRecoveryHelperArgumentsForTesting(
        ToolCall failedCall,
        ToolDefinition helperDefinition,
        out JsonObject helperArguments) {
        return TryBuildRecoveryHelperArguments(failedCall, helperDefinition, out helperArguments);
    }

    internal bool TryBuildRecoveryHelperInvocationForTesting(
        ToolCall failedCall,
        string helperToolName,
        out ToolCall helperCall) {
        return TryBuildRecoveryHelperInvocation(failedCall, helperToolName, out _, out helperCall);
    }
}
