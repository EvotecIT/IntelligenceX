using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI.Tools;

/// <summary>
/// Executes a chat request with tool calls resolved locally.
/// </summary>
public static class ToolRunner {
    /// <summary>
    /// Runs a chat request and executes tool calls until completion.
    /// </summary>
    /// <param name="client">OpenAI client instance.</param>
    /// <param name="input">Chat input.</param>
    /// <param name="options">Chat options (tools will be injected).</param>
    /// <param name="registry">Tool registry.</param>
    /// <param name="runnerOptions">Runner options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<ToolRunResult> RunAsync(IntelligenceXClient client, ChatInput input, ChatOptions options,
        ToolRegistry registry, ToolRunnerOptions? runnerOptions = null, CancellationToken cancellationToken = default) {
        if (client is null) {
            throw new ArgumentNullException(nameof(client));
        }
        if (input is null) {
            throw new ArgumentNullException(nameof(input));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }

        runnerOptions ??= new ToolRunnerOptions();
        var maxRounds = Math.Max(1, runnerOptions.MaxRounds);

        var originalTools = options.Tools;
        var originalChoice = options.ToolChoice;
        var originalPrevious = options.PreviousResponseId;
        var originalNewThread = options.NewThread;
        options.Tools = registry.GetDefinitions();
        options.ToolChoice ??= ToolChoice.Auto;

        var allCalls = new List<ToolCall>();
        var allOutputs = new List<ToolOutput>();
        string? previousResponseId = null;
        var nextInput = input;

        try {
            for (var round = 0; round < maxRounds; round++) {
                if (round > 0) {
                    options.NewThread = false;
                }
                options.PreviousResponseId = previousResponseId;
                var turn = await client.ChatAsync(nextInput, options, cancellationToken).ConfigureAwait(false);
                var calls = ToolCallParser.Extract(turn);
                if (calls.Count == 0) {
                    return new ToolRunResult(turn, allCalls, allOutputs);
                }
                allCalls.AddRange(calls);

                var outputs = await ExecuteToolsAsync(calls, registry, cancellationToken).ConfigureAwait(false);
                allOutputs.AddRange(outputs);

                previousResponseId = TryGetResponseId(turn);
                nextInput = new ChatInput();
                foreach (var output in outputs) {
                    nextInput.AddRaw(output.ToInputItem());
                }
            }

            throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
        } finally {
            options.Tools = originalTools;
            options.ToolChoice = originalChoice;
            options.PreviousResponseId = originalPrevious;
            options.NewThread = originalNewThread;
        }
    }

    private static async Task<IReadOnlyList<ToolOutput>> ExecuteToolsAsync(IReadOnlyList<ToolCall> calls, ToolRegistry registry,
        CancellationToken cancellationToken) {
        var outputs = new List<ToolOutput>(calls.Count);
        foreach (var call in calls) {
            if (!registry.TryGet(call.Name, out var tool)) {
                throw new InvalidOperationException($"Tool '{call.Name}' is not registered.");
            }
            var result = await tool.InvokeAsync(call.Arguments, cancellationToken).ConfigureAwait(false);
            outputs.Add(new ToolOutput(call.CallId, result ?? string.Empty));
        }
        return outputs;
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

/// <summary>
/// Tool runner configuration.
/// </summary>
public sealed class ToolRunnerOptions {
    /// <summary>
    /// Maximum number of tool-call rounds allowed.
    /// </summary>
    public int MaxRounds { get; set; } = 3;
}

/// <summary>
/// Result of a tool runner execution.
/// </summary>
public sealed class ToolRunResult {
    /// <summary>
    /// Initializes a new tool run result.
    /// </summary>
    public ToolRunResult(TurnInfo finalTurn, IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutput> outputs) {
        FinalTurn = finalTurn ?? throw new ArgumentNullException(nameof(finalTurn));
        ToolCalls = calls ?? Array.Empty<ToolCall>();
        ToolOutputs = outputs ?? Array.Empty<ToolOutput>();
    }

    /// <summary>
    /// Gets the final turn returned by the model.
    /// </summary>
    public TurnInfo FinalTurn { get; }
    /// <summary>
    /// Gets all tool calls executed during the run.
    /// </summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; }
    /// <summary>
    /// Gets all tool outputs returned to the model.
    /// </summary>
    public IReadOnlyList<ToolOutput> ToolOutputs { get; }
}
