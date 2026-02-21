using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private async Task TryWriteDeltaAsync(StreamWriter writer, string requestId, string threadId, string delta) {
        try {
            await WriteAsync(writer, new ChatDeltaMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Text = delta
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort streaming; ignore pipe failures.
        }
    }

    private async Task TryWriteStatusAsync(StreamWriter writer, string requestId, string threadId, string status, string? toolName = null,
        string? toolCallId = null, long? durationMs = null, string? message = null) {
        try {
            await WriteAsync(writer, new ChatStatusMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Status = status,
                ToolName = toolName,
                ToolCallId = toolCallId,
                DurationMs = durationMs,
                Message = message
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort; ignore pipe failures.
        }
    }

    private async Task HandleListToolsAsync(StreamWriter writer, string requestId, CancellationToken cancellationToken) {
        var defs = _registry.GetDefinitions();
        var tools = new ToolDefinitionDto[defs.Count];
        for (var i = 0; i < defs.Count; i++) {
            var parametersJson = defs[i].Parameters is null ? "{}" : JsonLite.Serialize(defs[i].Parameters);
            var required = ExtractRequiredArguments(parametersJson);
            var parameters = ExtractToolParameters(parametersJson, required);
            var packId = string.Empty;
            if (_toolPackIdsByToolName.TryGetValue(defs[i].Name, out var registeredPackId)) {
                packId = NormalizePackId(registeredPackId);
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

            tools[i] = new ToolDefinitionDto {
                Name = defs[i].Name,
                Description = defs[i].Description ?? string.Empty,
                DisplayName = ResolveToolDisplayName(defs[i]),
                Category = InferToolCategory(packId, ResolveToolCategory(defs[i])),
                Tags = defs[i].Tags.Count == 0 ? null : defs[i].Tags.ToArray(),
                PackId = packId.Length == 0 ? null : packId,
                PackName = string.IsNullOrWhiteSpace(packName) ? null : packName,
                PackDescription = string.IsNullOrWhiteSpace(packDescription) ? null : packDescription,
                PackSourceKind = packSourceKind,
                ParametersJson = parametersJson,
                RequiredArguments = required,
                Parameters = parameters
            };
        }
        await WriteAsync(writer, new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = requestId,
            Tools = tools
        }, cancellationToken).ConfigureAwait(false);
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
        var output = await ExecuteToolAsync(call, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        await WriteAsync(writer, new InvokeToolResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            ToolName = toolName,
            Output = output
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> HandleChatRequestAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, string? activeThreadId,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Text)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Text cannot be empty.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }
        if (!TryValidateChatRequestOptions(request.Options, out var optionsValidationError)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = optionsValidationError ?? "Invalid chat options.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }

        ChatRun? existingRun;
        lock (_chatRunLock) {
            existingRun = _activeChat;
            if (existingRun is not null && !existingRun.IsCompleted) {
                existingRun = _activeChat;
            } else {
                existingRun = null;
            }
        }

        if (existingRun is not null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"A chat request is already running (requestId={existingRun.ChatRequestId}).",
                Code = "chat_in_progress"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }

        try {
            activeThreadId = await EnsureThreadAsync(client, request.ThreadId, activeThreadId, request.Options?.Model, cancellationToken)
                .ConfigureAwait(false);
        } catch (OpenAIAuthenticationRequiredException) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Not authenticated. Run ChatGPT login in a client that can persist ~/.intelligencex/auth.json, then reconnect.",
                Code = "not_authenticated"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Chat failed: {ex.Message}",
                Code = "chat_failed"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }

        var run = new ChatRun(request.RequestId, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            ThreadId = activeThreadId
        };
        lock (_chatRunLock) {
            _activeChat = run;
        }

        run.Task = Task.Run(async () => {
            IDisposable? deltaSubscription = null;
            var startedAtUtc = DateTime.UtcNow;
            long firstDeltaUtcTicks = 0;
            TokenUsageDto? usageDto = null;
            var toolCallsCount = 0;
            var toolRounds = 0;
            var projectionFallbackCount = 0;
            IReadOnlyList<ToolErrorMetricDto>? toolErrors = null;
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
            try {
                if (bufferDraftDeltasForSmartReview) {
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadIdForDelta,
                            status: "phase_review",
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

                var result = await RunChatOnCurrentThreadAsync(client, writer, request, threadIdForDelta, run.Cts.Token).ConfigureAwait(false);
                usageDto = MapUsage(result.Usage);
                toolCallsCount = result.ToolCallsCount;
                toolRounds = result.ToolRounds;
                projectionFallbackCount = result.ProjectionFallbackCount;
                toolErrors = result.ToolErrors;
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
            } catch (OperationCanceledException) when (run.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                outcome = "canceled";
                outcomeCode = "chat_canceled";
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Chat canceled by client.",
                    Code = "chat_canceled"
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
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
                        Usage = usageDto,
                        ToolCallsCount = toolCallsCount,
                        ToolRounds = toolRounds,
                        ProjectionFallbackCount = projectionFallbackCount,
                        ToolErrors = toolErrors is { Count: > 0 } ? toolErrors : null,
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
                run.MarkCompleted();
                lock (_chatRunLock) {
                    if (ReferenceEquals(_activeChat, run)) {
                        _activeChat = null;
                    }
                }
            }
        }, CancellationToken.None);

        return activeThreadId;
    }

    private static bool TryValidateChatRequestOptions(ChatRequestOptions? options, out string? error) {
        error = null;
        if (options is null) {
            return true;
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
        if (string.IsNullOrWhiteSpace(request.ChatRequestId)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "chatRequestId is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        ChatRun? active;
        lock (_chatRunLock) {
            active = _activeChat;
        }

        if (active is null || active.IsCompleted
            || !string.Equals(active.ChatRequestId, request.ChatRequestId, StringComparison.Ordinal)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Active chat request '{request.ChatRequestId}' not found.",
                Code = "chat_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        active.Cancel();
        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Cancel requested."
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task CancelActiveChatIfAnyAsync() {
        ChatRun? active;
        lock (_chatRunLock) {
            active = _activeChat;
            _activeChat = null;
        }

        if (active is null) {
            return;
        }

        active.Cancel();
        if (active.Task is not null) {
            try {
                await active.Task.ConfigureAwait(false);
            } catch {
                // Ignore.
            }
        }
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
