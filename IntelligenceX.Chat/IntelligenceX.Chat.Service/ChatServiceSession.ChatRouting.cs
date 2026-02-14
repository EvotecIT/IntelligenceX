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
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private async Task<ChatResultMessage> RunChatOnCurrentThreadAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, string threadId,
        CancellationToken cancellationToken) {
        var toolCalls = new List<ToolCallDto>();
        var toolOutputs = new List<ToolOutputDto>();

        IReadOnlyList<ToolDefinition> toolDefs = _registry.GetDefinitions();
        if (request.Options?.DisabledTools is { Length: > 0 } disabledTools && toolDefs.Count > 0) {
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < disabledTools.Length; i++) {
                if (!string.IsNullOrWhiteSpace(disabledTools[i])) {
                    disabled.Add(disabledTools[i].Trim());
                }
            }

            if (disabled.Count > 0) {
                var filtered = new List<ToolDefinition>(toolDefs.Count);
                for (var i = 0; i < toolDefs.Count; i++) {
                    if (!disabled.Contains(toolDefs[i].Name)) {
                        filtered.Add(toolDefs[i]);
                    }
                }
                toolDefs = filtered;
            }
        }
        toolDefs = SanitizeToolDefinitions(toolDefs);
        var originalToolCount = toolDefs.Count;
        var routingInsights = new List<ToolRoutingInsight>();
        var weightedToolRouting = request.Options?.WeightedToolRouting ?? true;
        var maxCandidateTools = request.Options?.MaxCandidateTools;
        if (weightedToolRouting && toolDefs.Count > 0) {
            var userRequest = ExtractPrimaryUserRequest(request.Text);
            if (!TryGetContinuationToolSubset(threadId, userRequest, toolDefs, out var continuationSubset)) {
                toolDefs = SelectWeightedToolSubset(toolDefs, userRequest, maxCandidateTools, out routingInsights);
            } else {
                toolDefs = continuationSubset;
                routingInsights = BuildContinuationRoutingInsights(toolDefs);
            }
            RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
        }

        var parallelTools = request.Options?.ParallelTools ?? _options.ParallelTools;
        var maxRounds = request.Options?.MaxToolRounds ?? _options.MaxToolRounds;
        var turnTimeoutSeconds = request.Options?.TurnTimeoutSeconds ?? _options.TurnTimeoutSeconds;
        var toolTimeoutSeconds = request.Options?.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        using var turnCts = CreateTimeoutCts(cancellationToken, turnTimeoutSeconds);
        var turnToken = turnCts?.Token ?? cancellationToken;

        var options = new ChatOptions {
            Model = request.Options?.Model ?? _options.Model,
            ParallelToolCalls = parallelTools,
            Tools = toolDefs.Count == 0 ? null : toolDefs,
            ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto
        };

        if (weightedToolRouting && originalToolCount > 0 && toolDefs.Count > 0 && toolDefs.Count < originalToolCount) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: "routing",
                    message: $"Tool routing selected {toolDefs.Count} of {originalToolCount} tools for this turn.")
                .ConfigureAwait(false);
            await EmitRoutingInsightsAsync(writer, request.RequestId, threadId, routingInsights).ConfigureAwait(false);
        }

        await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "thinking").ConfigureAwait(false);
        TurnInfo turn = await ChatWithToolSchemaRecoveryAsync(client, ChatInput.FromText(request.Text), options, turnToken).ConfigureAwait(false);

        for (var round = 0; round < Math.Max(1, maxRounds); round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                if (_options.Redact) {
                    text = RedactText(text);
                }
                return new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = text,
                    Tools = toolCalls.Count == 0 && toolOutputs.Count == 0
                        ? null
                        : new ToolRunDto { Calls = toolCalls.ToArray(), Outputs = toolOutputs.ToArray() }
                };
            }

            foreach (var call in extracted) {
                await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "tool_call", toolName: call.Name, toolCallId: call.CallId)
                    .ConfigureAwait(false);
                toolCalls.Add(new ToolCallDto {
                    CallId = call.CallId,
                    Name = call.Name,
                    ArgumentsJson = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments)
                });
            }

            var executed = await ExecuteToolsAsync(writer, request.RequestId, threadId, extracted, parallelTools, toolTimeoutSeconds, turnToken)
                .ConfigureAwait(false);
            UpdateToolRoutingStats(extracted, executed);
            foreach (var output in executed) {
                toolOutputs.Add(new ToolOutputDto {
                    CallId = output.CallId,
                    Output = output.Output,
                    Ok = output.Ok,
                    ErrorCode = output.ErrorCode,
                    Error = output.Error,
                    Hints = output.Hints,
                    IsTransient = output.IsTransient,
                    SummaryMarkdown = output.SummaryMarkdown,
                    MetaJson = output.MetaJson,
                    RenderJson = output.RenderJson,
                    FailureJson = output.FailureJson
                });
            }

            var next = new ChatInput();
            foreach (var output in executed) {
                next.AddToolOutput(output.CallId, output.Output);
            }
            options.NewThread = false;
            await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "thinking").ConfigureAwait(false);
            turn = await ChatWithToolSchemaRecoveryAsync(client, next, options, turnToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
    }

    private static IReadOnlyList<ToolDefinition> SanitizeToolDefinitions(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var sanitized = new List<ToolDefinition>(definitions.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var normalizedName = (definition.Name ?? string.Empty).Trim();
            if (normalizedName.Length == 0 || !seen.Add(normalizedName)) {
                continue;
            }

            sanitized.Add(definition);
        }

        return sanitized.Count == 0 ? Array.Empty<ToolDefinition>() : sanitized;
    }

    private IReadOnlyList<ToolDefinition> SelectWeightedToolSubset(IReadOnlyList<ToolDefinition> definitions, string requestText, int? maxCandidateTools,
        out List<ToolRoutingInsight> insights) {
        insights = new List<ToolRoutingInsight>();
        if (definitions.Count <= 12) {
            return definitions;
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (ShouldSkipWeightedRouting(userRequest)) {
            return definitions;
        }

        var tokens = TokenizeForToolRouting(userRequest);
        if (tokens.Count == 0) {
            return definitions;
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return definitions;
        }

        var scored = new List<ToolScore>(definitions.Count);
        var hasSignal = false;
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var searchText = BuildToolSearchText(definition);
            var score = 0d;
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            if (directNameMatch) {
                score += 6d;
            }

            var packId = InferPackIdFromToolName(definition.Name, definition.Tags);
            var packMatch = IsPackMentioned(userRequest, packId);
            if (packMatch) {
                score += 2.5d;
            }

            var tokenHits = 0;
            for (var t = 0; t < tokens.Count; t++) {
                var token = tokens[t];
                if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    score += 0.9d;
                    tokenHits++;
                }

                if (definition.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    score += 0.9d;
                }
            }

            var adjustment = ReadToolRoutingAdjustment(definition.Name);
            score += adjustment;
            if (score > 0.01d) {
                hasSignal = true;
            }

            scored.Add(new ToolScore(
                Definition: definition,
                Score: score,
                TokenHits: tokenHits,
                DirectNameMatch: directNameMatch,
                PackMatch: packMatch,
                Adjustment: adjustment));
        }

        if (!hasSignal) {
            return definitions;
        }

        scored.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
        });

        if (scored[0].Score < 1d) {
            return definitions;
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedDefs = new List<ToolDefinition>(Math.Min(limit, definitions.Count));
        for (var i = 0; i < scored.Count && selectedDefs.Count < limit; i++) {
            var definition = scored[i].Definition;
            if (!selected.Add(definition.Name)) {
                continue;
            }
            selectedDefs.Add(definition);
        }

        if (selectedDefs.Count == 0) {
            return definitions;
        }

        var minSelection = Math.Min(definitions.Count, Math.Max(8, Math.Min(limit, 12)));
        if (selectedDefs.Count < minSelection) {
            for (var i = selectedDefs.Count; i < scored.Count && selectedDefs.Count < minSelection; i++) {
                var definition = scored[i].Definition;
                if (!selected.Add(definition.Name)) {
                    continue;
                }
                selectedDefs.Add(definition);
            }
        }

        if (selectedDefs.Count >= definitions.Count) {
            return definitions;
        }

        insights = BuildRoutingInsights(scored, selectedDefs);
        return selectedDefs;
    }

    private static List<ToolRoutingInsight> BuildRoutingInsights(IReadOnlyList<ToolScore> scored, IReadOnlyList<ToolDefinition> selectedDefs) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        for (var i = 0; i < scored.Count; i++) {
            var toolScore = scored[i];
            if (!selectedNames.Contains(toolScore.Definition.Name)) {
                continue;
            }

            var confidenceValue = Math.Clamp(toolScore.Score / maxScore, 0d, 1d);
            var confidence = confidenceValue >= 0.72d ? "high" : confidenceValue >= 0.45d ? "medium" : "low";
            var reasons = new List<string>();
            if (toolScore.DirectNameMatch) {
                reasons.Add("direct name match");
            }
            if (toolScore.PackMatch) {
                reasons.Add("pack intent match");
            }
            if (toolScore.TokenHits > 0) {
                reasons.Add($"{toolScore.TokenHits} token matches");
            }
            if (toolScore.Adjustment > 0.2d) {
                reasons.Add("recent tool success");
            } else if (toolScore.Adjustment < -0.2d) {
                reasons.Add("recent tool failures");
            }

            if (reasons.Count == 0) {
                reasons.Add("general relevance");
            }

            insights.Add(new ToolRoutingInsight(
                ToolName: toolScore.Definition.Name,
                Confidence: confidence,
                Score: Math.Round(toolScore.Score, 3),
                Reason: string.Join(", ", reasons)));
        }

        insights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (insights.Count > 12) {
            insights.RemoveRange(12, insights.Count - 12);
        }

        return insights;
    }

}
