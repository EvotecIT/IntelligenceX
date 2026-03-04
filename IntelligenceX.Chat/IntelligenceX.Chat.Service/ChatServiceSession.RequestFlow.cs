using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> GlobalExecutionLanes = new();

    internal static int ResolveSessionExecutionQueueLimit(int configuredLimit) {
        return configuredLimit < 0 ? 0 : configuredLimit;
    }

    internal static int ResolveGlobalExecutionLaneConcurrency(int configuredConcurrency) {
        return configuredConcurrency < 0 ? 0 : configuredConcurrency;
    }

    private static SemaphoreSlim? ResolveGlobalExecutionLane(int maxConcurrency) {
        if (maxConcurrency <= 0) {
            return null;
        }

        return GlobalExecutionLanes.GetOrAdd(maxConcurrency, static value => new SemaphoreSlim(value, value));
    }

    private async Task HandleListToolsAsync(StreamWriter writer, string requestId, CancellationToken cancellationToken) {
        var defs = _registry.GetDefinitions();
        var startupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
        var startupToolingBootstrapInProgress = startupToolingBootstrapTask is { IsCompleted: false };
        ToolDefinitionDto[] tools;
        if (defs.Count > 0) {
            tools = BuildToolDefinitionDtosFromRegistryDefinitions(defs);
            Volatile.Write(ref _cachedToolDefinitions, tools);
        } else if (!ShouldUseCachedToolCatalogFallbackForListTools(startupToolingBootstrapInProgress)
                   || !TryGetCachedToolCatalogForListTools(out tools)) {
            tools = Array.Empty<ToolDefinitionDto>();
        }

        await WriteAsync(writer, new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = requestId,
            Tools = tools
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static bool ShouldUseCachedToolCatalogFallbackForListTools(bool startupToolingBootstrapInProgress) {
        return !startupToolingBootstrapInProgress;
    }

    private bool TryGetCachedToolCatalogForListTools(out ToolDefinitionDto[] tools) {
        var inMemory = Volatile.Read(ref _cachedToolDefinitions);
        if (inMemory.Length > 0) {
            tools = inMemory;
            return true;
        }

        var runtimePolicyOptions = BuildRuntimePolicyOptions(_options);
        var resolvedRuntimePolicyOptions = ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions);
        var cacheKey = BuildToolingBootstrapCacheKey(_options, runtimePolicyOptions, resolvedRuntimePolicyOptions);
        if (_toolingBootstrapCache is not null
            && _toolingBootstrapCache.TryGetPersistedSnapshot(cacheKey, out var persisted)
            && persisted.ToolDefinitions.Length > 0) {
            tools = persisted.ToolDefinitions;
            Volatile.Write(ref _cachedToolDefinitions, tools);
            return true;
        }

        tools = Array.Empty<ToolDefinitionDto>();
        return false;
    }

    private ToolDefinitionDto[] BuildToolDefinitionDtosFromRegistryDefinitions(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions is null || definitions.Count == 0) {
            return Array.Empty<ToolDefinitionDto>();
        }

        var tools = new ToolDefinitionDto[definitions.Count];
        for (var i = 0; i < definitions.Count; i++) {
            tools[i] = BuildToolDefinitionDto(definitions[i]);
        }

        return tools;
    }

    private ToolDefinitionDto BuildToolDefinitionDto(ToolDefinition definition) {
        var parametersJson = definition.Parameters is null ? "{}" : JsonLite.Serialize(definition.Parameters);
        var required = ExtractRequiredArguments(parametersJson);
        var parameters = ExtractToolParameters(parametersJson, required);
        var packId = string.Empty;
        if (_toolOrchestrationCatalog.TryGetPackId(definition.Name, out var catalogPackId)) {
            packId = catalogPackId;
        }

        string? packName = null;
        string? packDescription = null;
        ToolPackSourceKind? packSourceKind = null;
        if (packId.Length > 0 && _packDisplayNamesById.TryGetValue(packId, out var resolvedPackName)) {
            packName = resolvedPackName;
        }
        if (packId.Length > 0 && _packDescriptionsById.TryGetValue(packId, out var resolvedPackDescription)) {
            packDescription = resolvedPackDescription;
        }
        if (packId.Length > 0 && _packSourceKindsById.TryGetValue(packId, out var resolvedPackSourceKind)) {
            packSourceKind = resolvedPackSourceKind;
        }

        return new ToolDefinitionDto {
            Name = definition.Name,
            Description = definition.Description ?? string.Empty,
            DisplayName = ResolveToolDisplayName(definition),
            Category = ResolveToolListCategory(ResolveToolCategory(definition)),
            Tags = definition.Tags.Count == 0 ? null : definition.Tags.ToArray(),
            PackId = packId.Length == 0 ? null : packId,
            PackName = string.IsNullOrWhiteSpace(packName) ? null : packName,
            PackDescription = string.IsNullOrWhiteSpace(packDescription) ? null : packDescription,
            PackSourceKind = packSourceKind,
            IsWriteCapable = definition.WriteGovernance?.IsWriteCapable == true,
            ParametersJson = parametersJson,
            RequiredArguments = required,
            Parameters = parameters
        };
    }

    private async Task HandleInvokeToolAsync(StreamWriter writer, InvokeToolRequest request, CancellationToken cancellationToken) {
        var toolName = (request.ToolName ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolName is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        JsonObject? arguments = null;
        if (!string.IsNullOrWhiteSpace(request.ArgumentsJson)) {
            try {
                var parsed = JsonLite.Parse(request.ArgumentsJson!);
                arguments = parsed?.AsObject();
                if (parsed is not null && arguments is null) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = "argumentsJson must be a JSON object.",
                        Code = "invalid_argument"
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }
            } catch (Exception ex) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = $"Invalid argumentsJson: {ex.Message}",
                    Code = "invalid_json"
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var timeoutSeconds = request.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        if (timeoutSeconds < 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolTimeoutSeconds must be a non-negative integer.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var call = new ToolCall(
            callId: request.RequestId + ":invoke",
            name: toolName,
            input: request.ArgumentsJson,
            arguments: arguments,
            raw: new JsonObject());
        var output = await ExecuteToolAsync(
                threadId: string.Empty,
                userRequest: string.Empty,
                call: call,
                toolTimeoutSeconds: timeoutSeconds,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await WriteAsync(writer, new InvokeToolResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            ToolName = toolName,
            Output = output
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleChatRequestAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, CancellationToken cancellationToken) {
        var requestId = (request.RequestId ?? string.Empty).Trim();
        if (requestId.Length == 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = null,
                Error = "requestId is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Text)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = requestId,
                Error = "Text cannot be empty.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!TryValidateChatRequestOptions(request.Options, out var optionsValidationError)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = requestId,
                Error = optionsValidationError ?? "Invalid chat options.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var run = new ChatRun(
            chatRequestId: requestId,
            cts: CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
            client: client,
            writer: writer,
            request: request);
        var queueLimit = ResolveSessionExecutionQueueLimit(_options.SessionExecutionQueueLimit);
        var queuePosition = 0;
        var queueRejected = false;
        var duplicateRequestId = false;
        lock (_chatRunLock) {
            var queuedCount = GetQueuedChatRunCountNoLock();
            if (queueLimit > 0 && queuedCount >= queueLimit) {
                queueRejected = true;
            } else if (!_chatRunsByRequestId.TryAdd(run.ChatRequestId, run)) {
                duplicateRequestId = true;
            } else {
                var turnsAhead = (_activeChat is { IsCompleted: false } ? 1 : 0) + queuedCount;
                queuePosition = turnsAhead + 1;
                run.QueuePositionAtEnqueue = queuePosition;
                _queuedChats.Enqueue(run);
                if (_chatRunPumpTask is null || _chatRunPumpTask.IsCompleted) {
                    _chatRunPumpTask = Task.Run(() => RunQueuedChatPumpAsync(cancellationToken), CancellationToken.None);
                }
            }
        }

        if (duplicateRequestId) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = requestId,
                Error = $"A chat request with requestId '{requestId}' is already queued or running.",
                Code = "chat_request_id_conflict"
            }, cancellationToken).ConfigureAwait(false);
            run.MarkCompleted();
            return;
        }

        if (queueRejected) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = requestId,
                Error = $"Chat execution queue is full (limit={queueLimit}).",
                Code = "chat_queue_full"
            }, cancellationToken).ConfigureAwait(false);
            run.Cancel();
            run.MarkCompleted();
            return;
        }

        if (queuePosition > 1) {
            var threadHint = ResolveRecoveredThreadAlias(request.ThreadId);
            if (string.IsNullOrWhiteSpace(threadHint)) {
                threadHint = ResolveRecoveredThreadAlias(GetActiveThreadIdSnapshot());
            }

            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadHint ?? string.Empty,
                    status: ChatStatusCodes.TurnQueued,
                    message: $"Queued turn request ({queuePosition} in session lane).")
                .ConfigureAwait(false);
        } else {
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    string.Empty,
                    status: ChatStatusCodes.Thinking,
                    message: "Request accepted. Entering execution lane...")
                .ConfigureAwait(false);
        }
    }

    private async Task RunQueuedChatPumpAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            ChatRun? run;
            lock (_chatRunLock) {
                if (_queuedChats.Count == 0) {
                    _chatRunPumpTask = null;
                    return;
                }

                run = _queuedChats.Dequeue();
                _activeChat = run;
                run.MarkStarted();
            }

            run.Task = ExecuteQueuedChatRunAsync(run, cancellationToken);
            try {
                await run.Task.ConfigureAwait(false);
            } catch {
                // Request-scoped execution writes its own error responses; keep lane pump alive.
            } finally {
                run.MarkCompleted();
                lock (_chatRunLock) {
                    if (ReferenceEquals(_activeChat, run)) {
                        _activeChat = null;
                    }
                    _chatRunsByRequestId.Remove(run.ChatRequestId);
                }
            }
        }

        lock (_chatRunLock) {
            _chatRunPumpTask = null;
        }
    }

    private async Task ExecuteQueuedChatRunAsync(ChatRun run, CancellationToken sessionCancellationToken) {
        var request = run.Request with {
            RequestId = run.ChatRequestId
        };
        var writer = run.Writer;
        var client = run.Client;

        var requestThreadId = ResolveRecoveredThreadAlias(request.ThreadId);
        var routedActiveThreadId = ResolveRecoveredThreadAlias(GetActiveThreadIdSnapshot());
        var preflightThreadId = !string.IsNullOrWhiteSpace(requestThreadId)
            ? requestThreadId
            : !string.IsNullOrWhiteSpace(routedActiveThreadId)
                ? routedActiveThreadId
                : string.Empty;
        long? ensureThreadMs = null;

        var globalLane = ResolveGlobalExecutionLane(ResolveGlobalExecutionLaneConcurrency(_options.GlobalExecutionLaneConcurrency));
        var enteredGlobalLane = false;

        try {
            if (globalLane is not null) {
                if (globalLane.Wait(0)) {
                    enteredGlobalLane = true;
                } else {
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            preflightThreadId,
                            status: ChatStatusCodes.ExecutionLaneWaiting,
                            message: "Waiting for global execution lane...")
                        .ConfigureAwait(false);
                    await globalLane.WaitAsync(run.Cts.Token).ConfigureAwait(false);
                    enteredGlobalLane = true;
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            preflightThreadId,
                            status: ChatStatusCodes.ExecutionLaneAcquired,
                            message: "Global execution lane acquired.")
                        .ConfigureAwait(false);
                }
            }

            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    preflightThreadId,
                    status: ChatStatusCodes.Thinking,
                    message: "Request accepted. Preparing conversation context...")
                .ConfigureAwait(false);

            try {
                var ensureThreadStopwatch = Stopwatch.StartNew();
                var activeThreadId = await EnsureThreadAsync(client, requestThreadId, routedActiveThreadId, request.Options?.Model, run.Cts.Token)
                    .ConfigureAwait(false);
                ensureThreadMs = Math.Max(0L, ensureThreadStopwatch.ElapsedMilliseconds);
                run.ThreadId = activeThreadId;
                SetActiveThreadId(activeThreadId);

                var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
                if (normalizedActiveThreadId.Length > 0) {
                    if (!string.IsNullOrWhiteSpace(requestThreadId)
                        && !string.Equals(requestThreadId, normalizedActiveThreadId, StringComparison.Ordinal)) {
                        RememberRecoveredThreadAlias(requestThreadId, normalizedActiveThreadId);
                    }

                    if (!string.IsNullOrWhiteSpace(routedActiveThreadId)
                        && !string.Equals(routedActiveThreadId, normalizedActiveThreadId, StringComparison.Ordinal)) {
                        RememberRecoveredThreadAlias(routedActiveThreadId, normalizedActiveThreadId);
                    }
                }

                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        normalizedActiveThreadId,
                        status: ChatStatusCodes.Thinking,
                        message: "Conversation context ready. Starting routing and planning...")
                    .ConfigureAwait(false);
            } catch (OperationCanceledException) when (run.Cts.IsCancellationRequested && !sessionCancellationToken.IsCancellationRequested) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Chat canceled by client.",
                    Code = "chat_canceled"
                }, CancellationToken.None).ConfigureAwait(false);
                return;
            } catch (OperationCanceledException) when (sessionCancellationToken.IsCancellationRequested) {
                // Session shutting down.
                return;
            } catch (OpenAIAuthenticationRequiredException) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Not authenticated. Run ChatGPT login in a client that can persist ~/.intelligencex/auth.json, then reconnect.",
                    Code = "not_authenticated"
                }, CancellationToken.None).ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = $"Chat failed: {ex.Message}",
                    Code = "chat_failed"
                }, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            IDisposable? deltaSubscription = null;
            var startedAtUtc = DateTime.UtcNow;
            long firstDeltaUtcTicks = 0;
            TokenUsageDto? usageDto = null;
            var toolCallsCount = 0;
            var toolRounds = 0;
            var projectionFallbackCount = 0;
            long? weightedSubsetSelectionMs = null;
            long? resolveModelMs = null;
            IReadOnlyList<ToolErrorMetricDto>? toolErrors = null;
            IReadOnlyList<TurnCounterMetricDto>? autonomyCounters = null;
            var outcome = "ok";
            string? outcomeCode = null;
            var threadIdForDelta = run.ThreadId ?? string.Empty;
            var requestedModel = NormalizeModelTelemetryValue(request.Options?.Model);
            var runtimeDefaultModel = NormalizeModelTelemetryValue(_options.Model);
            var telemetryModel = ResolveEffectiveTurnModelForTelemetry(
                resolvedModel: null,
                requestedModel,
                runtimeDefaultModel);
            var bufferDraftDeltasForSmartReview = ShouldBufferDraftDeltasForSmartReview(request);
            BeginTurnTimelineCapture(request.RequestId);
            try {
                if (bufferDraftDeltasForSmartReview) {
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadIdForDelta,
                            status: ChatStatusCodes.PhaseReview,
                            message: "Drafting response and applying quality review before display...")
                        .ConfigureAwait(false);
                }

                deltaSubscription = client.SubscribeDelta(delta => {
                    // Best-effort TTFT tracking: latch the first delta timestamp once.
                    if (firstDeltaUtcTicks == 0) {
                        _ = Interlocked.CompareExchange(ref firstDeltaUtcTicks, DateTime.UtcNow.Ticks, 0);
                    }
                    if (bufferDraftDeltasForSmartReview) {
                        return;
                    }
                    _ = TryWriteDeltaAsync(writer, request.RequestId, threadIdForDelta, delta);
                });

                var result = await RunChatOnCurrentThreadAsync(client, writer, request, threadIdForDelta, run.Cts.Token)
                    .ConfigureAwait(false);
                usageDto = MapUsage(result.Usage);
                toolCallsCount = result.ToolCallsCount;
                toolRounds = result.ToolRounds;
                projectionFallbackCount = result.ProjectionFallbackCount;
                weightedSubsetSelectionMs = result.WeightedSubsetSelectionMs;
                resolveModelMs = result.ResolveModelMs;
                toolErrors = result.ToolErrors;
                autonomyCounters = result.AutonomyCounters;
                telemetryModel = ResolveEffectiveTurnModelForTelemetry(
                    result.ResolvedModel,
                    requestedModel,
                    runtimeDefaultModel);
                await WriteAsync(writer, result.Result, CancellationToken.None).ConfigureAwait(false);

                await TryRecordRecentModelAsync(telemetryModel, CancellationToken.None).ConfigureAwait(false);
            } catch (OpenAIAuthenticationRequiredException) {
                outcome = "error";
                outcomeCode = "not_authenticated";
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Not authenticated. Run ChatGPT login in a client that can persist ~/.intelligencex/auth.json, then reconnect.",
                    Code = "not_authenticated"
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) when (run.Cts.IsCancellationRequested && !sessionCancellationToken.IsCancellationRequested) {
                outcome = "canceled";
                outcomeCode = "chat_canceled";
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Chat canceled by client.",
                    Code = "chat_canceled"
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) when (sessionCancellationToken.IsCancellationRequested) {
                // Session shutting down.
                outcome = "canceled";
                outcomeCode = "session_canceled";
            } catch (Exception ex) {
                outcome = "error";
                outcomeCode = "chat_failed";
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = $"Chat failed: {ex.Message}",
                    Code = "chat_failed"
                }, CancellationToken.None).ConfigureAwait(false);
            } finally {
                var completedAtUtc = DateTime.UtcNow;
                var durationMs = (long)Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds);
                DateTime? firstDeltaAtUtc = null;
                long? ttftMs = null;
                if (firstDeltaUtcTicks != 0) {
                    firstDeltaAtUtc = new DateTime(firstDeltaUtcTicks, DateTimeKind.Utc);
                    ttftMs = (long)Math.Max(0, TimeSpan.FromTicks(firstDeltaUtcTicks - startedAtUtc.Ticks).TotalMilliseconds);
                }

                try {
                    var metricsTransport = ResolveMetricsTransport();
                    var metricsEndpointHost = ResolveMetricsEndpointHost();
                    await WriteAsync(writer, new ChatMetricsMessage {
                        Kind = ChatServiceMessageKind.Event,
                        RequestId = request.RequestId,
                        ThreadId = threadIdForDelta,
                        StartedAtUtc = startedAtUtc,
                        FirstDeltaAtUtc = firstDeltaAtUtc,
                        CompletedAtUtc = completedAtUtc,
                        DurationMs = durationMs,
                        TtftMs = ttftMs,
                        EnsureThreadMs = ensureThreadMs,
                        WeightedSubsetSelectionMs = weightedSubsetSelectionMs,
                        ResolveModelMs = resolveModelMs,
                        Usage = usageDto,
                        ToolCallsCount = toolCallsCount,
                        ToolRounds = toolRounds,
                        ProjectionFallbackCount = projectionFallbackCount,
                        ToolErrors = toolErrors is { Count: > 0 } ? toolErrors : null,
                        AutonomyCounters = autonomyCounters is { Count: > 0 } ? autonomyCounters : null,
                        Model = telemetryModel,
                        RequestedModel = requestedModel,
                        Transport = metricsTransport,
                        EndpointHost = metricsEndpointHost,
                        Outcome = outcome,
                        ErrorCode = outcomeCode
                    }, CancellationToken.None).ConfigureAwait(false);
                } catch {
                    // Best-effort; ignore pipe failures.
                }

                deltaSubscription?.Dispose();
                EndTurnTimelineCapture(request.RequestId);
            }
        } catch (OperationCanceledException) when (run.Cts.IsCancellationRequested && !sessionCancellationToken.IsCancellationRequested) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Chat canceled by client.",
                Code = "chat_canceled"
            }, CancellationToken.None).ConfigureAwait(false);
        } catch (OperationCanceledException) when (sessionCancellationToken.IsCancellationRequested) {
            // Session shutting down.
        } finally {
            if (enteredGlobalLane) {
                try {
                    globalLane?.Release();
                } catch {
                    // Ignore release errors if lane ownership changed due cancellation races.
                }
            }
        }
    }

    private int GetQueuedChatRunCountNoLock() {
        return _queuedChats.Count;
    }

    private string? GetActiveThreadIdSnapshot() {
        lock (_chatRunLock) {
            return _activeThreadId;
        }
    }

    private void SetActiveThreadId(string? activeThreadId) {
        var normalized = (activeThreadId ?? string.Empty).Trim();
        lock (_chatRunLock) {
            _activeThreadId = normalized.Length == 0 ? null : normalized;
        }
    }

    private void ClearActiveThreadId() {
        lock (_chatRunLock) {
            _activeThreadId = null;
        }
    }

    private static bool TryValidateChatRequestOptions(ChatRequestOptions? options, out string? error) {
        error = null;
        if (options is null) {
            return true;
        }

        if (options.MaxToolRounds < ChatRequestOptionLimits.MinToolRounds || options.MaxToolRounds > ChatRequestOptionLimits.MaxToolRounds) {
            error = $"maxToolRounds must be between {ChatRequestOptionLimits.MinToolRounds} and {ChatRequestOptionLimits.MaxToolRounds}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ParallelToolMode)) {
            var normalizedParallelMode = NormalizeParallelToolMode(options.ParallelToolMode);
            var explicitAuto = string.Equals(options.ParallelToolMode.Trim(), ParallelToolModeAuto, StringComparison.OrdinalIgnoreCase);
            if (normalizedParallelMode == ParallelToolModeAuto && !explicitAuto) {
                error = "parallelToolMode must be one of: auto, force_serial, allow_parallel.";
                return false;
            }
        }

        if (options.MaxCandidateTools.HasValue) {
            var maxCandidateTools = options.MaxCandidateTools.Value;
            if (maxCandidateTools < ChatRequestOptionLimits.MinCandidateTools || maxCandidateTools > ChatRequestOptionLimits.MaxCandidateTools) {
                error = $"maxCandidateTools must be between {ChatRequestOptionLimits.MinCandidateTools} and {ChatRequestOptionLimits.MaxCandidateTools}.";
                return false;
            }
        }

        if (!TryValidateSelectorOptionValues(options.EnabledTools, "enabledTools", out error)) {
            return false;
        }

        if (!TryValidateSelectorOptionValues(options.DisabledTools, "disabledTools", out error)) {
            return false;
        }

        if (!TryValidateSelectorOptionValues(options.EnabledPackIds, "enabledPackIds", out error)) {
            return false;
        }

        if (!TryValidateSelectorOptionValues(options.DisabledPackIds, "disabledPackIds", out error)) {
            return false;
        }

        if (options.TurnTimeoutSeconds.HasValue) {
            var turnTimeout = options.TurnTimeoutSeconds.Value;
            if (turnTimeout < ChatRequestOptionLimits.MinTimeoutSeconds || turnTimeout > ChatRequestOptionLimits.MaxTimeoutSeconds) {
                error = $"turnTimeoutSeconds must be between {ChatRequestOptionLimits.MinTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.";
                return false;
            }
        }

        if (options.ToolTimeoutSeconds.HasValue) {
            var toolTimeout = options.ToolTimeoutSeconds.Value;
            if (toolTimeout < ChatRequestOptionLimits.MinTimeoutSeconds || toolTimeout > ChatRequestOptionLimits.MaxTimeoutSeconds) {
                error = $"toolTimeoutSeconds must be between {ChatRequestOptionLimits.MinTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.";
                return false;
            }
        }

        if (options.Temperature.HasValue) {
            var temperature = options.Temperature.Value;
            if (double.IsNaN(temperature) || double.IsInfinity(temperature) || temperature < 0d || temperature > 2d) {
                error = "temperature must be between 0 and 2.";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateSelectorOptionValues(string[]? values, string optionName, out string? error) {
        error = null;
        if (values is null) {
            return true;
        }

        if (values.Length > ChatRequestOptionLimits.MaxToolSelectors) {
            error = $"{optionName} must include at most {ChatRequestOptionLimits.MaxToolSelectors} entries.";
            return false;
        }

        for (var i = 0; i < values.Length; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length == 0) {
                continue;
            }

            if (value.Length > ChatRequestOptionLimits.MaxToolSelectorLength) {
                error = $"{optionName} entries must be at most {ChatRequestOptionLimits.MaxToolSelectorLength} characters.";
                return false;
            }
        }

        return true;
    }

    private static TokenUsageDto? MapUsage(TurnUsage? usage) {
        if (usage is null) {
            return null;
        }
        return new TokenUsageDto {
            PromptTokens = usage.InputTokens,
            CompletionTokens = usage.OutputTokens,
            TotalTokens = usage.TotalTokens,
            CachedPromptTokens = usage.CachedInputTokens,
            ReasoningTokens = usage.ReasoningTokens
        };
    }

    internal static string? ResolveEffectiveTurnModelForTelemetry(string? resolvedModel, string? requestedModel, string? runtimeDefaultModel) {
        var resolved = NormalizeModelTelemetryValue(resolvedModel);
        if (resolved is not null) {
            return resolved;
        }

        var requested = NormalizeModelTelemetryValue(requestedModel);
        if (requested is not null) {
            return requested;
        }

        return NormalizeModelTelemetryValue(runtimeDefaultModel);
    }

    private static string? NormalizeModelTelemetryValue(string? model) {
        var normalized = (model ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private string ResolveMetricsTransport() {
        return _options.OpenAITransport switch {
            OpenAITransportKind.Native => "native",
            OpenAITransportKind.AppServer => "appserver",
            OpenAITransportKind.CompatibleHttp => "compatible-http",
            OpenAITransportKind.CopilotCli => "copilot-cli",
            _ => "unknown"
        };
    }

    private string? ResolveMetricsEndpointHost() {
        var raw = (_options.OpenAIBaseUrl ?? string.Empty).Trim();
        if (raw.Length == 0 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri)) {
            return null;
        }

        var host = (uri.Host ?? string.Empty).Trim();
        return host.Length == 0 ? null : host;
    }

    private async Task HandleCancelChatAsync(StreamWriter writer, CancelChatRequest request, CancellationToken cancellationToken) {
        var chatRequestId = (request.ChatRequestId ?? string.Empty).Trim();
        if (chatRequestId.Length == 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "chatRequestId is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        ChatRun? active;
        var removedQueuedRun = false;
        lock (_chatRunLock) {
            _chatRunsByRequestId.TryGetValue(chatRequestId, out active);
            if (active is not null && !active.Started && !active.IsCompleted) {
                RemoveQueuedRunFromQueueNoLock(active);
                _chatRunsByRequestId.Remove(chatRequestId);
                removedQueuedRun = true;
            }
        }

        if (active is null || active.IsCompleted) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Chat request '{chatRequestId}' not found.",
                Code = "chat_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        active.Cancel();
        if (removedQueuedRun) {
            active.MarkCompleted();
        }
        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Cancel requested."
        }, cancellationToken).ConfigureAwait(false);
    }

    private void RemoveQueuedRunFromQueueNoLock(ChatRun target) {
        if (_queuedChats.Count == 0) {
            return;
        }

        var retained = new Queue<ChatRun>(_queuedChats.Count);
        while (_queuedChats.Count > 0) {
            var current = _queuedChats.Dequeue();
            if (!ReferenceEquals(current, target)) {
                retained.Enqueue(current);
            }
        }

        while (retained.Count > 0) {
            _queuedChats.Enqueue(retained.Dequeue());
        }
    }

    private async Task CancelActiveChatIfAnyAsync() {
        ChatRun[] runs;
        Task? pumpTask;
        lock (_chatRunLock) {
            runs = _chatRunsByRequestId.Values.ToArray();
            _chatRunsByRequestId.Clear();
            _queuedChats.Clear();
            _activeChat = null;
            pumpTask = _chatRunPumpTask;
        }

        for (var i = 0; i < runs.Length; i++) {
            runs[i].Cancel();
        }

        for (var i = 0; i < runs.Length; i++) {
            if (runs[i].Task is null) {
                continue;
            }

            try {
                await runs[i].Task!.ConfigureAwait(false);
            } catch {
                // Ignore.
            }
        }

        if (pumpTask is not null) {
            try {
                await pumpTask.ConfigureAwait(false);
            } catch {
                // Ignore.
            }
        }

        lock (_chatRunLock) {
            _chatRunPumpTask = null;
        }

        ClearActiveThreadId();
    }

    private static string[] ExtractRequiredArguments(string parametersJson) {
        if (string.IsNullOrWhiteSpace(parametersJson)) {
            return Array.Empty<string>();
        }

        try {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty("required", out var required) || required.ValueKind != System.Text.Json.JsonValueKind.Array) {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var item in required.EnumerateArray()) {
                if (item.ValueKind != System.Text.Json.JsonValueKind.String) {
                    continue;
                }
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }
                list.Add(value.Trim());
            }
            return list.ToArray();
        } catch {
            return Array.Empty<string>();
        }
    }

}
