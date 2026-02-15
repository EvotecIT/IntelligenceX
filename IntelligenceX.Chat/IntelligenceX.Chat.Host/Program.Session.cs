using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

    private sealed class ReplSession {
        private readonly IntelligenceXClient _client;
        private readonly ToolRegistry _registry;
        private readonly ReplOptions _options;
        private readonly string? _instructions;
        private readonly Action<string>? _status;
        private string? _previousResponseId;

        public ReplSession(IntelligenceXClient client, ToolRegistry registry, ReplOptions options, string? instructions, Action<string>? status) {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions;
            _status = status;
        }

        public void ResetThread() {
            _previousResponseId = null;
        }

        public async Task<ReplTurnResult> AskAsync(string text, CancellationToken cancellationToken) {
            var withMetrics = await AskWithMetricsAsync(text, cancellationToken).ConfigureAwait(false);
            return withMetrics.Result;
        }

        public async Task<ReplTurnMetricsResult> AskWithMetricsAsync(string text, CancellationToken cancellationToken) {
            var startedAtUtc = DateTime.UtcNow;
            var firstDeltaUtcTicks = 0L;

            void OnDelta(object? _, string __) {
                if (firstDeltaUtcTicks != 0) {
                    return;
                }
                _ = Interlocked.CompareExchange(ref firstDeltaUtcTicks, DateTime.UtcNow.Ticks, 0);
            }

            _client.DeltaReceived += OnDelta;
            try {
                var result = await AskCoreAsync(text, cancellationToken).ConfigureAwait(false);
                var completedAtUtc = DateTime.UtcNow;
                DateTime? firstDeltaAtUtc = null;
                long? ttftMs = null;
                if (firstDeltaUtcTicks != 0) {
                    firstDeltaAtUtc = new DateTime(firstDeltaUtcTicks, DateTimeKind.Utc);
                    ttftMs = (long)Math.Max(0, TimeSpan.FromTicks(firstDeltaUtcTicks - startedAtUtc.Ticks).TotalMilliseconds);
                }

                var metrics = new ReplTurnMetrics(
                    startedAtUtc,
                    firstDeltaAtUtc,
                    completedAtUtc,
                    durationMs: (long)Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds),
                    ttftMs: ttftMs,
                    usage: result.Usage,
                    toolCallsCount: result.ToolCalls.Count,
                    toolRounds: result.ToolRounds);
                return new ReplTurnMetricsResult(result, metrics);
            } finally {
                _client.DeltaReceived -= OnDelta;
            }
        }

        private async Task<ReplTurnResult> AskCoreAsync(string text, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(text)) {
                return new ReplTurnResult(string.Empty, Array.Empty<ToolCall>(), Array.Empty<ToolOutput>());
            }

            using var turnCts = CreateTimeoutCts(cancellationToken, _options.TurnTimeoutSeconds);
            var turnToken = turnCts?.Token ?? cancellationToken;

            var calls = new List<ToolCall>();
            var outputs = new List<ToolOutput>();
            var toolRounds = 0;

            var input = ChatInput.FromText(text);
            var toolDefs = _registry.GetDefinitions();
            var chatOptions = new ChatOptions {
                Model = _options.Model,
                Instructions = _instructions,
                ReasoningEffort = _options.ReasoningEffort,
                ReasoningSummary = _options.ReasoningSummary,
                TextVerbosity = _options.TextVerbosity,
                Temperature = _options.Temperature,
                ParallelToolCalls = _options.ParallelToolCalls,
                Tools = toolDefs.Count == 0 ? null : toolDefs,
                ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto,
                PreviousResponseId = _previousResponseId,
                NewThread = string.IsNullOrWhiteSpace(_previousResponseId)
            };

            if (_options.LiveProgress) {
                _status?.Invoke("thinking...");
            }
            TurnInfo turn = await ChatWithToolSchemaRecoveryAsync(input, chatOptions, turnToken).ConfigureAwait(false);

            for (var round = 0; round < Math.Max(1, _options.MaxToolRounds); round++) {
                var extracted = ToolCallParser.Extract(turn);
                if (extracted.Count == 0) {
                    var finalText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                    _previousResponseId = TryGetResponseId(turn) ?? _previousResponseId;
                    return new ReplTurnResult(finalText, calls, outputs, turn.Usage, toolRounds);
                }

                toolRounds++;
                calls.AddRange(extracted);
                if (_options.LiveProgress) {
                    foreach (var call in extracted) {
                        var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                        var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                        _status?.Invoke($"tool: {GetToolDisplayName(call.Name)}{id} args={args}");
                    }
                }
                var executed = await ExecuteToolsAsync(extracted, turnToken).ConfigureAwait(false);
                outputs.AddRange(executed);

                var next = new ChatInput();
                foreach (var output in executed) {
                    next.AddToolOutput(output.CallId, output.Output);
                }

                chatOptions.NewThread = false;
                chatOptions.PreviousResponseId = TryGetResponseId(turn);
                if (_options.LiveProgress) {
                    _status?.Invoke("thinking...");
                }
                turn = await ChatWithToolSchemaRecoveryAsync(next, chatOptions, turnToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Tool runner exceeded max rounds ({_options.MaxToolRounds}).");
        }

        private async Task<IReadOnlyList<ToolOutput>> ExecuteToolsAsync(IReadOnlyList<ToolCall> calls, CancellationToken cancellationToken) {
            if (!_options.ParallelToolCalls || calls.Count <= 1) {
                var outputs = new List<ToolOutput>(calls.Count);
                foreach (var call in calls) {
                    outputs.Add(await ExecuteToolAsync(call, cancellationToken).ConfigureAwait(false));
                }
                return outputs;
            }

            var tasks = new Task<ToolOutput>[calls.Count];
            for (var i = 0; i < calls.Count; i++) {
                tasks[i] = ExecuteToolAsync(calls[i], cancellationToken);
            }
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task<ToolOutput> ExecuteToolAsync(ToolCall call, CancellationToken cancellationToken) {
            if (!_registry.TryGet(call.Name, out var tool)) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_not_registered",
                    error: $"Tool '{call.Name}' is not registered.",
                    hints: new[] { "Run /tools to list available tools.", "Check that the correct packs are enabled." },
                    isTransient: false));
            }
            if (_options.LiveProgress) {
                var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                _status?.Invoke($"running: {GetToolDisplayName(call.Name)}{id}");
            }
            using var toolCts = CreateTimeoutCts(cancellationToken, _options.ToolTimeoutSeconds);
            var toolToken = toolCts?.Token ?? cancellationToken;
            try {
                var result = await tool.InvokeAsync(call.Arguments, toolToken).ConfigureAwait(false);
                return new ToolOutput(call.CallId, result ?? string.Empty);
            } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_timeout",
                    error: $"Tool '{call.Name}' timed out after {_options.ToolTimeoutSeconds}s.",
                    hints: new[] { "Increase --tool-timeout-seconds, or narrow the query (OU scoping, tighter filters)." },
                    isTransient: true));
            } catch (Exception ex) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_exception",
                    error: $"{ex.GetType().Name}: {ex.Message}",
                    hints: new[] { "Try again. If it keeps failing, re-run with --echo-tool-outputs to capture details." },
                    isTransient: false));
            }
        }

        private static string? TryGetResponseId(TurnInfo turn) {
            if (turn is null) {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(turn.ResponseId)) {
                return turn.ResponseId;
            }
            var responseId = turn.Raw.GetString("responseId");
            if (!string.IsNullOrWhiteSpace(responseId)) {
                return responseId;
            }
            var response = turn.Raw.GetObject("response");
            return response?.GetString("id");
        }

        private async Task<TurnInfo> ChatWithToolSchemaRecoveryAsync(ChatInput input, ChatOptions options, CancellationToken cancellationToken) {
            try {
                return await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ShouldRetryWithoutTools(ex, options)) {
                options.Tools = null;
                options.ToolChoice = null;
                return await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool ShouldRetryWithoutTools(Exception ex, ChatOptions options) {
            if (options.Tools is not { Count: > 0 }) {
                return false;
            }

            var message = ex.Message ?? string.Empty;
            if (message.Length == 0) {
                return false;
            }

            var missingToolName = message.IndexOf("missing required parameter", StringComparison.OrdinalIgnoreCase) >= 0
                                  && message.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0
                                  && message.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
            if (missingToolName) {
                return true;
            }

            return message.IndexOf("cannot truncate prompt with n_keep", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("n_ctx", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("context window", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("maximum context length", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("prompt too long", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private sealed class ReplTurnResult {
        public ReplTurnResult(string text, IReadOnlyList<ToolCall> toolCalls, IReadOnlyList<ToolOutput> toolOutputs,
            TurnUsage? usage = null, int toolRounds = 0) {
            Text = text ?? string.Empty;
            ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
            ToolOutputs = toolOutputs ?? Array.Empty<ToolOutput>();
            Usage = usage;
            ToolRounds = Math.Max(0, toolRounds);
        }

        public string Text { get; }
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        public IReadOnlyList<ToolOutput> ToolOutputs { get; }
        public TurnUsage? Usage { get; }
        public int ToolRounds { get; }
    }

    private sealed class ReplTurnMetrics {
        public ReplTurnMetrics(DateTime startedAtUtc, DateTime? firstDeltaAtUtc, DateTime completedAtUtc, long durationMs, long? ttftMs,
            TurnUsage? usage, int toolCallsCount, int toolRounds) {
            StartedAtUtc = startedAtUtc;
            FirstDeltaAtUtc = firstDeltaAtUtc;
            CompletedAtUtc = completedAtUtc;
            DurationMs = Math.Max(0, durationMs);
            TtftMs = ttftMs.HasValue ? Math.Max(0, ttftMs.Value) : null;
            Usage = usage;
            ToolCallsCount = Math.Max(0, toolCallsCount);
            ToolRounds = Math.Max(0, toolRounds);
        }

        public DateTime StartedAtUtc { get; }
        public DateTime? FirstDeltaAtUtc { get; }
        public DateTime CompletedAtUtc { get; }
        public long DurationMs { get; }
        public long? TtftMs { get; }
        public TurnUsage? Usage { get; }
        public int ToolCallsCount { get; }
        public int ToolRounds { get; }
    }

    private sealed class ReplTurnMetricsResult {
        public ReplTurnMetricsResult(ReplTurnResult result, ReplTurnMetrics metrics) {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        public ReplTurnResult Result { get; }
        public ReplTurnMetrics Metrics { get; }
    }

}
