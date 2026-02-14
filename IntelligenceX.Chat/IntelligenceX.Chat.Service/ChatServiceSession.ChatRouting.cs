using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        var userRequest = ExtractPrimaryUserRequest(request.Text);
        var userIntent = ExtractIntentUserText(request.Text);
        RememberUserIntent(threadId, userIntent);
        var routedUserRequest = ExpandContinuationUserRequest(threadId, userRequest);
        var usedContinuationSubset = false;
        if (weightedToolRouting && toolDefs.Count > 0) {
            if (!TryGetContinuationToolSubset(threadId, userRequest, toolDefs, out var continuationSubset)) {
                var routed = await SelectWeightedToolSubsetAsync(
                        client,
                        threadId,
                        toolDefs,
                        routedUserRequest,
                        maxCandidateTools,
                        cancellationToken)
                    .ConfigureAwait(false);
                toolDefs = routed.Definitions;
                routingInsights = routed.Insights;
            } else {
                toolDefs = continuationSubset;
                routingInsights = BuildContinuationRoutingInsights(toolDefs);
                usedContinuationSubset = true;
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
        var executionNudgeUsed = false;

        for (var round = 0; round < Math.Max(1, maxRounds); round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                if (!executionNudgeUsed
                    && ShouldAttemptToolExecutionNudge(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        usedContinuationSubset: usedContinuationSubset)) {
                    executionNudgeUsed = true;
                    var nudgePrompt = BuildToolExecutionNudgePrompt(routedUserRequest, text);
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: "thinking",
                            message: "Re-planning to execute available tools in this turn.")
                        .ConfigureAwait(false);
                    options.NewThread = false;
                    turn = await ChatWithToolSchemaRecoveryAsync(client, ChatInput.FromText(nudgePrompt), options, turnToken).ConfigureAwait(false);
                    continue;
                }

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

    private static bool ShouldAttemptToolExecutionNudge(string userRequest, string assistantDraft, bool toolsAvailable, int priorToolCalls,
        bool usedContinuationSubset) {
        if (!toolsAvailable || priorToolCalls > 0) {
            return false;
        }

        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > 2400) {
            return false;
        }

        // If the assistant explicitly told the user to "say" a quoted phrase, accept echoing that phrase even when
        // weighted continuation routing wasn't used (for example after a restart or when tool routing kept full tool lists).
        var echoedCallToAction = UserMatchesAssistantCallToAction(request, draft);
        if (!usedContinuationSubset && !echoedCallToAction) {
            return false;
        }

        if (!echoedCallToAction && !LooksLikeCompactFollowUp(request)) {
            return false;
        }

        var asksAnotherQuestion = draft.Contains('?', StringComparison.Ordinal);
        if (asksAnotherQuestion) {
            return true;
        }

        // Language-agnostic "acknowledgement-like" draft: short, no structured output, no numeric evidence.
        var hasStructuredOutput = draft.Contains('\n', StringComparison.Ordinal)
                                  || draft.Contains('|', StringComparison.Ordinal)
                                  || draft.Contains('{', StringComparison.Ordinal)
                                  || draft.Contains('[', StringComparison.Ordinal);
        if (hasStructuredOutput) {
            return false;
        }

        var hasNumericSignal = false;
        for (var i = 0; i < draft.Length; i++) {
            if (char.IsDigit(draft[i])) {
                hasNumericSignal = true;
                break;
            }
        }

        if (hasNumericSignal || draft.Length > 220) {
            return false;
        }

        // Avoid overriding already-good short completions (for example "You're welcome.").
        // Only retry tool execution when the assistant draft still appears tied to the user's follow-up.
        return AssistantDraftReferencesUserRequest(request, draft);
    }

    private static bool UserMatchesAssistantCallToAction(string userRequest, string assistantDraft) {
        var request = NormalizeCompactText(userRequest);
        if (request.Length == 0 || request.Length > 120) {
            return false;
        }

        var phrases = ExtractQuotedPhrases(assistantDraft);
        if (phrases.Count == 0) {
            return false;
        }

        for (var i = 0; i < phrases.Count; i++) {
            var phrase = NormalizeCompactText(phrases[i]);
            if (phrase.Length == 0 || phrase.Length > 96) {
                continue;
            }

            // Strong signal: exact echo.
            if (string.Equals(request, phrase, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // Common pattern: "yes - <phrase>" or "<phrase>?".
            if (ContainsPhraseWithBoundaries(request, phrase)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeCompactText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Strip inline-code wrappers (`run now`) without trying to parse markdown fully.
        if (normalized.Length >= 2 && normalized[0] == '`' && normalized[^1] == '`') {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }

        // Trim light punctuation wrappers so "run now?" and "\"run now\"" normalize.
        normalized = normalized.Trim().Trim('"', '\'', '.', '!', '?', ':', ';', ',', '(', ')', '[', ']', '{', '}');
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Collapse whitespace to stabilize matching across minor formatting differences.
        var sb = new StringBuilder(normalized.Length);
        var inSpace = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsWhiteSpace(ch)) {
                if (!inSpace) {
                    sb.Append(' ');
                    inSpace = true;
                }
                continue;
            }

            inSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static bool ContainsPhraseWithBoundaries(string haystack, string needle) {
        if (haystack.Length == 0 || needle.Length == 0 || needle.Length > haystack.Length) {
            return false;
        }

        var startIndex = 0;
        while (true) {
            var idx = haystack.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) {
                return false;
            }

            var beforeOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var afterIndex = idx + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (beforeOk && afterOk) {
                return true;
            }

            startIndex = idx + 1;
            if (startIndex >= haystack.Length) {
                return false;
            }
        }
    }

    private static List<string> ExtractQuotedPhrases(string text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return new List<string>();
        }

        var phrases = new List<string>();
        for (var i = 0; i < value.Length; i++) {
            var quote = value[i];
            if (quote != '"' && quote != '\'') {
                continue;
            }

            // Treat apostrophes inside words as apostrophes, not as quoting. This avoids accidentally pairing "don't"
            // with a later single-quote and extracting a huge bogus "phrase".
            if (quote == '\'' && i > 0 && i + 1 < value.Length && char.IsLetterOrDigit(value[i - 1]) && char.IsLetterOrDigit(value[i + 1])) {
                continue;
            }

            var end = value.IndexOf(quote, i + 1);
            if (end <= i + 1) {
                continue;
            }

            var inner = value.Substring(i + 1, end - i - 1).Trim();
            i = end;
            if (inner.Length == 0 || inner.Length > 96) {
                continue;
            }

            if (inner.Contains('\n', StringComparison.Ordinal)) {
                continue;
            }

            // Keep it lean: only short, "say this" kind of phrases (avoid quoting entire paragraphs).
            var tokens = CountLetterDigitTokens(inner, maxTokens: 12);
            if (tokens == 0 || tokens > 8) {
                continue;
            }

            phrases.Add(inner);
            if (phrases.Count >= 6) {
                break;
            }
        }

        return phrases;
    }

    private static bool LooksLikeCompactFollowUp(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.Contains('\n', StringComparison.Ordinal)) {
            return false;
        }

        if (normalized.Length > 80) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 12);
        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= 6 && normalized.Length <= 64) {
            return true;
        }

        return tokenCount <= 8 && normalized.Length <= 80 && normalized.Contains('?', StringComparison.Ordinal);
    }

    private static bool AssistantDraftReferencesUserRequest(string userRequest, string assistantDraft) {
        var request = (userRequest ?? string.Empty).Trim();
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (request.Length == 0 || draft.Length == 0) {
            return false;
        }

        // Direct substring match is the strongest signal.
        if (request.Length >= 3 && draft.IndexOf(request, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        // Fall back to token containment (language-agnostic): if any meaningful user token appears in the draft,
        // it is likely the assistant intended to act on that follow-up but failed to call tools.
        var inToken = false;
        var tokenStart = 0;
        var checkedTokens = 0;
        for (var i = 0; i <= request.Length; i++) {
            var ch = i < request.Length ? request[i] : '\0';
            var isTokenChar = i < request.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                if (!inToken) {
                    inToken = true;
                    tokenStart = i;
                }
                continue;
            }

            if (!inToken) {
                continue;
            }

            var token = request.Substring(tokenStart, i - tokenStart);
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var hasNonAscii = false;
            for (var t = 0; t < token.Length; t++) {
                if (token[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (token.Length < minLen) {
                continue;
            }

            checkedTokens++;
            if (draft.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            if (checkedTokens >= 12) {
                break;
            }
        }

        return false;
    }

    private static int CountLetterDigitTokens(string text, int maxTokens) {
        var tokenCount = 0;
        var inToken = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch)) {
                if (!inToken) {
                    tokenCount++;
                    if (tokenCount >= maxTokens) {
                        return tokenCount;
                    }
                    inToken = true;
                }
            } else {
                inToken = false;
            }
        }

        return tokenCount;
    }

    private static string BuildToolExecutionNudgePrompt(string userRequest, string assistantDraft) {
        var requestText = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
        var draftText = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
        return $$"""
            [Execution correction]
            The previous assistant draft did not execute tools.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Execute available tools now when they can satisfy this request.
            Do not ask for another confirmation unless a required input cannot be inferred or discovered.
            If tools truly cannot satisfy the request, explain the exact blocker and the minimal missing input.
            """;
    }

    private async Task<(IReadOnlyList<ToolDefinition> Definitions, List<ToolRoutingInsight> Insights)> SelectWeightedToolSubsetAsync(
        IntelligenceXClient client,
        string threadId,
        IReadOnlyList<ToolDefinition> definitions,
        string requestText,
        int? maxCandidateTools,
        CancellationToken cancellationToken) {
        if (definitions.Count <= 12) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (ShouldSkipWeightedRouting(userRequest)) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var planned = await TrySelectToolsViaModelPlannerAsync(client, threadId, userRequest, definitions, limit, cancellationToken).ConfigureAwait(false);
        if (planned.Count > 0) {
            var selected = EnsureMinimumToolSelection(userRequest, definitions, planned, limit);
            if (selected.Count > 0 && selected.Count < definitions.Count) {
                var plannerInsights = BuildModelRoutingInsights(selected, plannedCount: planned.Count);
                return (selected, plannerInsights);
            }
        }

        var fallback = SelectWeightedToolSubset(definitions, userRequest, maxCandidateTools, out var fallbackInsights);
        return (fallback, fallbackInsights);
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

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return definitions;
        }

        var routingTokens = TokenizeRoutingTokens(userRequest, maxTokens: 16);
        var routingTokenSupport = routingTokens.Length == 0 ? Array.Empty<int>() : new int[routingTokens.Length];
        string[]? toolSearchTexts = null;
        if (routingTokens.Length > 0) {
            toolSearchTexts = new string[definitions.Count];
            for (var i = 0; i < definitions.Count; i++) {
                toolSearchTexts[i] = BuildToolRoutingSearchText(definitions[i]);
            }

            for (var t = 0; t < routingTokens.Length; t++) {
                var token = routingTokens[t];
                if (token.Length == 0) {
                    continue;
                }

                var support = 0;
                for (var i = 0; i < toolSearchTexts.Length; i++) {
                    if (toolSearchTexts[i].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        support++;
                    }
                }

                routingTokenSupport[t] = support;
            }
        }

        // Tokens that show up in most tools are noise (ex: "get", "list"). Filter them out per-turn.
        var maxTokenSupport = Math.Max(1, (int)Math.Ceiling(definitions.Count * 0.55d));

        var scored = new List<ToolScore>(definitions.Count);
        var hasSignal = false;
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var score = 0d;
            var tokenHits = 0;
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            if (directNameMatch) {
                score += 6d;
            }

            if (routingTokens.Length > 0) {
                var searchText = toolSearchTexts?[i] ?? BuildToolRoutingSearchText(definition);
                for (var t = 0; t < routingTokens.Length; t++) {
                    if (routingTokenSupport[t] > maxTokenSupport) {
                        continue;
                    }

                    var token = routingTokens[t];
                    if (token.Length == 0) {
                        continue;
                    }

                    if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        tokenHits++;
                    }
                }

                if (tokenHits > 0) {
                    score += tokenHits * 1.25d;
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
                DirectNameMatch: directNameMatch,
                TokenHits: tokenHits,
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
            if (toolScore.TokenHits > 0) {
                reasons.Add("token match");
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

    private static string[] TokenizeRoutingTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>(Math.Min(12, maxTokens));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                if (!inToken) {
                    inToken = true;
                    tokenStart = i;
                }
                continue;
            }

            if (!inToken) {
                continue;
            }

            var token = normalized.Substring(tokenStart, i - tokenStart).Normalize(NormalizationForm.FormKC).Trim();
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var lower = token.ToLowerInvariant();
            var hasNonAscii = false;
            for (var t = 0; t < lower.Length; t++) {
                if (lower[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (lower.Length < minLen) {
                continue;
            }

            if (seen.Add(lower)) {
                tokens.Add(lower);
                if (tokens.Count >= maxTokens) {
                    break;
                }
            }
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static string BuildToolRoutingSearchText(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var sb = new StringBuilder(256);
        sb.Append(definition.Name);
        if (!string.IsNullOrWhiteSpace(definition.Description)) {
            sb.Append(' ').Append(definition.Description!.Trim());
        }

        if (definition.Tags.Count > 0) {
            for (var i = 0; i < definition.Tags.Count; i++) {
                var tag = (definition.Tags[i] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }
                sb.Append(' ').Append(tag);
            }
        }

        if (definition.Aliases.Count > 0) {
            for (var i = 0; i < definition.Aliases.Count; i++) {
                var alias = definition.Aliases[i];
                if (alias is null || string.IsNullOrWhiteSpace(alias.Name)) {
                    continue;
                }
                sb.Append(' ').Append(alias.Name.Trim());
            }
        }

        return sb.ToString();
    }

}
