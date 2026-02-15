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
            if (string.IsNullOrWhiteSpace(text)) {
                return new ReplTurnResult(string.Empty, Array.Empty<ToolCall>(), Array.Empty<ToolOutput>());
            }

            using var turnCts = CreateTimeoutCts(cancellationToken, _options.TurnTimeoutSeconds);
            var turnToken = turnCts?.Token ?? cancellationToken;

            var calls = new List<ToolCall>();
            var outputs = new List<ToolOutput>();

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
            TurnInfo turn = await _client.ChatAsync(input, chatOptions, turnToken).ConfigureAwait(false);

            for (var round = 0; round < Math.Max(1, _options.MaxToolRounds); round++) {
                var extracted = ToolCallParser.Extract(turn);
                if (extracted.Count == 0) {
                    var finalText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                    _previousResponseId = TryGetResponseId(turn) ?? _previousResponseId;
                    return new ReplTurnResult(finalText, calls, outputs);
                }

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
                turn = await _client.ChatAsync(next, chatOptions, turnToken).ConfigureAwait(false);
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
    }

    private sealed class ReplTurnResult {
        public ReplTurnResult(string text, IReadOnlyList<ToolCall> toolCalls, IReadOnlyList<ToolOutput> toolOutputs) {
            Text = text ?? string.Empty;
            ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
            ToolOutputs = toolOutputs ?? Array.Empty<ToolOutput>();
        }

        public string Text { get; }
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        public IReadOnlyList<ToolOutput> ToolOutputs { get; }
    }

}
