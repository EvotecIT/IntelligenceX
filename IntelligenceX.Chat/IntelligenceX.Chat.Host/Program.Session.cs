using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
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
        private const int MaxNoToolExecutionRetries = 3;
        private const string ScenarioExecutionContractMarker = "[Scenario execution contract]";
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
                    toolRounds: result.ToolRounds,
                    noToolExecutionRetries: result.NoToolExecutionRetries);
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
            var noToolExecutionRetryCount = 0;

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

            var maxRounds = Math.Clamp(_options.MaxToolRounds, ChatRequestOptionLimits.MinToolRounds, MaxToolRoundsLimit);
            for (var round = 0; round < maxRounds; round++) {
                var extracted = ToolCallParser.Extract(turn);
                if (extracted.Count == 0) {
                    var finalText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;

                    var shouldRetryNoToolExecution = noToolExecutionRetryCount < MaxNoToolExecutionRetries
                                                     && calls.Count == 0
                                                     && outputs.Count == 0
                                                     && toolDefs.Count > 0
                                                     && ShouldRetryNoToolExecution(text, finalText);
                    var shouldRetryScenarioContractRepair = noToolExecutionRetryCount < MaxNoToolExecutionRetries
                                                            && calls.Count > 0
                                                            && toolDefs.Count > 0
                                                            && ShouldRetryScenarioContractRepair(text, calls);
                    if (shouldRetryNoToolExecution || shouldRetryScenarioContractRepair) {
                        noToolExecutionRetryCount++;
                        var hasScenarioContract = TryParseScenarioExecutionContractRequirements(text, out _);
                        var useScenarioRepairPrompt = shouldRetryScenarioContractRepair
                                                      || (shouldRetryNoToolExecution && hasScenarioContract);
                        var retryPrompt = useScenarioRepairPrompt
                            ? BuildScenarioContractRepairRetryPrompt(text, finalText, calls, noToolExecutionRetryCount)
                            : BuildNoToolExecutionRetryPrompt(text, finalText, noToolExecutionRetryCount);
                        chatOptions.NewThread = false;
                        chatOptions.PreviousResponseId = TryGetResponseId(turn);
                        if (_options.LiveProgress) {
                            _status?.Invoke(useScenarioRepairPrompt
                                ? "repairing partial scenario tool execution..."
                                : "re-planning tool execution for this turn...");
                        }
                        turn = await ChatWithToolSchemaRecoveryAsync(ChatInput.FromText(retryPrompt), chatOptions, turnToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    _previousResponseId = TryGetResponseId(turn) ?? _previousResponseId;
                    return new ReplTurnResult(finalText, calls, outputs, turn.Usage, toolRounds, noToolExecutionRetryCount);
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

            throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
        }

        private static bool LooksLikeExecutionIntentPlaceholderDraft(string userRequest, string assistantDraft) {
            var requestText = CollapseWhitespace((userRequest ?? string.Empty).Trim());
            var normalizedDraft = CollapseWhitespace((assistantDraft ?? string.Empty).Trim());

            var requestTokens = ExtractMeaningfulTokens(requestText, maxTokens: 24);
            var draftTokens = ExtractMeaningfulTokens(normalizedDraft, maxTokens: 48);
            if (requestTokens.Count < 4 || draftTokens.Count < 4) {
                return false;
            }

            if (normalizedDraft.Length < 24 || normalizedDraft.Length > 560) {
                return false;
            }

            if (normalizedDraft.Contains('?', StringComparison.Ordinal)
                || normalizedDraft.Contains('？', StringComparison.Ordinal)
                || normalizedDraft.Contains('¿', StringComparison.Ordinal)
                || normalizedDraft.Contains('؟', StringComparison.Ordinal)) {
                return false;
            }

            if (normalizedDraft.Contains('|', StringComparison.Ordinal)
                || normalizedDraft.Contains('{', StringComparison.Ordinal)
                || normalizedDraft.Contains('}', StringComparison.Ordinal)
                || normalizedDraft.Contains('[', StringComparison.Ordinal)
                || normalizedDraft.Contains(']', StringComparison.Ordinal)
                || normalizedDraft.Contains('<', StringComparison.Ordinal)
                || normalizedDraft.Contains('>', StringComparison.Ordinal)
                || normalizedDraft.Contains('=', StringComparison.Ordinal)) {
                return false;
            }

            var requestUnique = new HashSet<string>(requestTokens, StringComparer.OrdinalIgnoreCase);
            var draftUnique = new HashSet<string>(draftTokens, StringComparer.OrdinalIgnoreCase);
            var sharedCount = 0;
            foreach (var token in requestUnique) {
                if (draftUnique.Contains(token)) {
                    sharedCount++;
                }
            }

            if (sharedCount < 3) {
                return false;
            }

            var overlapRatio = requestUnique.Count == 0 ? 0d : (double)sharedCount / requestUnique.Count;
            if (overlapRatio < 0.35d) {
                return false;
            }

            var longDigitRunCount = 0;
            var currentDigitRun = 0;
            for (var i = 0; i < normalizedDraft.Length; i++) {
                if (char.IsDigit(normalizedDraft[i])) {
                    currentDigitRun++;
                    continue;
                }

                if (currentDigitRun >= 4) {
                    longDigitRunCount++;
                }
                currentDigitRun = 0;
            }

            if (currentDigitRun >= 4) {
                longDigitRunCount++;
            }

            return longDigitRunCount == 0;
        }

        private static string CollapseWhitespace(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            var inSpace = false;
            for (var i = 0; i < value.Length; i++) {
                var ch = value[i];
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

        private static bool ShouldRetryNoToolExecution(string userRequest, string assistantDraft) {
            var draft = (assistantDraft ?? string.Empty).Trim();
            if (draft.Length == 0) {
                return !string.IsNullOrWhiteSpace(userRequest);
            }

            if ((userRequest ?? string.Empty).IndexOf(ScenarioExecutionContractMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            var request = userRequest ?? string.Empty;
            if (LooksLikeExecutionIntentPlaceholderDraft(request, draft)) {
                return true;
            }

            if (LooksLikeBlockerPrefaceWithoutExecution(request, draft)) {
                return true;
            }

            return LooksLikeLinkedFollowUpQuestionWithoutExecution(request, draft);
        }

        private static bool LooksLikeBlockerPrefaceWithoutExecution(string userRequest, string assistantDraft) {
            var request = CollapseWhitespace((userRequest ?? string.Empty).Trim());
            var draft = CollapseWhitespace((assistantDraft ?? string.Empty).Trim());
            if (request.Length < 18 || draft.Length < 24 || draft.Length > 1800) {
                return false;
            }

            if (ContainsQuestionSignal(draft)) {
                return false;
            }

            // Keep this heuristic language-neutral for routing, but still guard a known blocker-preface
            // phrase family that regressed in live host runs.
            if (draft.IndexOf("i can do that, but", StringComparison.OrdinalIgnoreCase) < 0
                && draft.IndexOf("i can do that but", StringComparison.OrdinalIgnoreCase) < 0) {
                return false;
            }

            var requestTokens = ExtractMeaningfulTokens(request, maxTokens: 32);
            var draftTokens = ExtractMeaningfulTokens(draft, maxTokens: 64);
            if (requestTokens.Count < 4 || draftTokens.Count < 4) {
                return false;
            }

            var requestUnique = new HashSet<string>(requestTokens, StringComparer.OrdinalIgnoreCase);
            var draftUnique = new HashSet<string>(draftTokens, StringComparer.OrdinalIgnoreCase);
            var sharedCount = 0;
            foreach (var token in requestUnique) {
                if (draftUnique.Contains(token)) {
                    sharedCount++;
                }
            }

            if (sharedCount < 1) {
                return false;
            }

            var overlapRatio = requestUnique.Count == 0 ? 0d : (double)sharedCount / requestUnique.Count;
            return overlapRatio >= 0.1d;
        }

        private static bool ContainsQuestionSignal(string text) {
            var value = text ?? string.Empty;
            return value.IndexOf('?', StringComparison.Ordinal) >= 0
                   || value.IndexOf('？', StringComparison.Ordinal) >= 0
                   || value.IndexOf('¿', StringComparison.Ordinal) >= 0
                   || value.IndexOf('؟', StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeLinkedFollowUpQuestionWithoutExecution(string userRequest, string assistantDraft) {
            var request = CollapseWhitespace((userRequest ?? string.Empty).Trim());
            var draft = CollapseWhitespace((assistantDraft ?? string.Empty).Trim());

            if (request.Length < 18 || draft.Length < 24 || draft.Length > 1800) {
                return false;
            }

            if (!ContainsQuestionSignal(draft)) {
                return false;
            }

            if (draft.Contains('|', StringComparison.Ordinal)
                || draft.Contains('{', StringComparison.Ordinal)
                || draft.Contains('}', StringComparison.Ordinal)
                || draft.Contains('[', StringComparison.Ordinal)
                || draft.Contains(']', StringComparison.Ordinal)
                || draft.Contains('<', StringComparison.Ordinal)
                || draft.Contains('>', StringComparison.Ordinal)
                || draft.Contains('=', StringComparison.Ordinal)) {
                return false;
            }

            var requestTokens = ExtractMeaningfulTokens(request, maxTokens: 32);
            var draftTokens = ExtractMeaningfulTokens(draft, maxTokens: 48);
            if (requestTokens.Count < 4 || draftTokens.Count < 4) {
                return false;
            }

            var requestUnique = new HashSet<string>(requestTokens, StringComparer.OrdinalIgnoreCase);
            var draftUnique = new HashSet<string>(draftTokens, StringComparer.OrdinalIgnoreCase);
            var sharedCount = 0;
            foreach (var token in requestUnique) {
                if (draftUnique.Contains(token)) {
                    sharedCount++;
                }
            }

            if (sharedCount < 1) {
                return false;
            }

            var overlapRatio = requestUnique.Count == 0 ? 0d : (double)sharedCount / requestUnique.Count;
            return overlapRatio >= 0.1d;
        }

        private static List<string> ExtractMeaningfulTokens(string text, int maxTokens) {
            var value = (text ?? string.Empty).Trim();
            var tokens = new List<string>();
            if (value.Length == 0 || maxTokens <= 0) {
                return tokens;
            }

            var inToken = false;
            var tokenStart = 0;
            for (var i = 0; i <= value.Length; i++) {
                var ch = i < value.Length ? value[i] : '\0';
                var isTokenChar = i < value.Length && char.IsLetterOrDigit(ch);
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

                var token = value.Substring(tokenStart, i - tokenStart);
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

                tokens.Add(token);
                if (tokens.Count >= maxTokens) {
                    break;
                }
            }

            return tokens;
        }

        private static string BuildNoToolExecutionRetryPrompt(string userRequest, string assistantDraft, int retryAttempt) {
            var request = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
            var draft = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
            _ = retryAttempt;
            return $$"""
                [Execution correction]
                The previous assistant draft implied execution (or returned empty output) but no tool calls were emitted.

                User request:
                {{request}}

                Previous assistant draft:
                {{draft}}

                If tools can satisfy this request, execute at least one relevant tool call now in this turn.
                For read-only requests, infer missing inputs from prior tool outputs where possible and do not ask for confirmation before the first tool call.
                For AD "authoritative latest lastLogon" requests, query lastLogon per discovered DC and report the max value with source DC.
                When AD datetime fields are FILETIME ticks, convert and report exact UTC ISO timestamps using strict ISO-8601 with T and trailing Z (for example 2026-02-24T17:20:10.5177390Z) and include the exact uppercase token UTC at least once.
                Include at least one timestamp matching regex \d{4}-\d{2}-\d{2}T\d{2}:\d{2} when timestamps are requested.
                If no matching evidence is found, still include queried time-window boundaries as strict ISO-8601 UTC timestamps (T + Z).
                Do not use blocker-preface phrasing like "I can do that, but"; execute best-effort tools first, then report results or exact blockers.
                For optional projection arguments (columns/sort_by), use only supported fields; if uncertain, omit projection arguments.
                For eventlog_named_events_query, use names from eventlog_named_events_catalog; if uncertain, prefer eventlog_live_query with explicit event_ids.
                If Event Log source input is missing, default machine_name to the first discovered/source DC from prior turns.
                If this is a continuation request over "remaining discovered DCs/hosts", execute multiple best-effort tool calls using distinct host/DC inputs from thread context.
                If discovery appears empty in this turn, still use previously seen DC/host targets from thread context rather than stopping at narration.
                Do not claim internal retry/exhaustion limits; this is an internal execution correction path.
                If tools still cannot satisfy this request after a best-effort tool attempt, state the exact blocker and the minimal missing input once.
                """;
        }

        private static bool ShouldRetryScenarioContractRepair(string userRequest, IReadOnlyList<ToolCall> calls) {
            if (!TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) || requirements is null) {
                return false;
            }

            if (requirements.MinToolCalls > 0 && calls.Count < requirements.MinToolCalls) {
                return true;
            }

            if (requirements.RequiredTools.Count > 0) {
                foreach (var requiredPattern in requirements.RequiredTools) {
                    if (!ToolCallSetContainsPattern(calls, requiredPattern)) {
                        return true;
                    }
                }
            }

            if (requirements.RequiredAnyTools.Count > 0) {
                var matchedAnyRequired = false;
                foreach (var requiredPattern in requirements.RequiredAnyTools) {
                    if (!ToolCallSetContainsPattern(calls, requiredPattern)) {
                        continue;
                    }

                    matchedAnyRequired = true;
                    break;
                }

                if (!matchedAnyRequired) {
                    return true;
                }
            }

            if (requirements.MinDistinctToolInputValues.Count == 0) {
                return false;
            }

            foreach (var requirement in requirements.MinDistinctToolInputValues) {
                var minDistinct = Math.Max(0, requirement.Value);
                if (minDistinct == 0) {
                    continue;
                }

                var observedValues = CollectDistinctToolInputValuesByKey(calls, requirement.Key);
                if (observedValues.Count < minDistinct) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseScenarioExecutionContractRequirements(string userRequest, out ScenarioExecutionContractRequirements? requirements) {
            requirements = null;
            var request = userRequest ?? string.Empty;
            if (request.IndexOf(ScenarioExecutionContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
                return false;
            }

            var minToolCalls = 0;
            var minToolCallsMatch = Regex.Match(
                request,
                @"Minimum tool calls in this turn:\s*(?<count>\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (minToolCallsMatch.Success
                && int.TryParse(minToolCallsMatch.Groups["count"].Value, out var parsedMinToolCalls)
                && parsedMinToolCalls > 0) {
                minToolCalls = parsedMinToolCalls;
            }

            var minDistinctToolInputValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var requiredTools = ParseScenarioContractToolPatterns(
                request,
                @"Required tool calls \(all\):\s*(?<patterns>[^\r\n]+)");
            var requiredAnyTools = ParseScenarioContractToolPatterns(
                request,
                @"Required tool calls \(at least one\):\s*(?<patterns>[^\r\n]+)");
            var distinctMatch = Regex.Match(
                request,
                @"Distinct tool input value requirements:\s*(?<requirements>[^\r\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (distinctMatch.Success) {
                var rawRequirements = (distinctMatch.Groups["requirements"].Value ?? string.Empty).Trim();
                if (rawRequirements.EndsWith(".", StringComparison.Ordinal)) {
                    rawRequirements = rawRequirements.Substring(0, rawRequirements.Length - 1).TrimEnd();
                }

                if (rawRequirements.Length > 0) {
                    var segments = rawRequirements.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var segment in segments) {
                        var pair = segment.Split(">=", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (pair.Length != 2) {
                            continue;
                        }

                        var key = (pair[0] ?? string.Empty).Trim();
                        if (key.Length == 0) {
                            continue;
                        }

                        if (!int.TryParse(pair[1], out var parsedMinDistinct) || parsedMinDistinct < 0) {
                            continue;
                        }

                        minDistinctToolInputValues[key] = parsedMinDistinct;
                    }
                }
            }

            if (minToolCalls <= 0
                && minDistinctToolInputValues.Count == 0
                && requiredTools.Count == 0
                && requiredAnyTools.Count == 0) {
                return false;
            }

            requirements = new ScenarioExecutionContractRequirements(
                minToolCalls,
                minDistinctToolInputValues,
                requiredTools,
                requiredAnyTools);
            return true;
        }

        private static IReadOnlyList<string> ParseScenarioContractToolPatterns(string request, string pattern) {
            var match = Regex.Match(request ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) {
                return Array.Empty<string>();
            }

            var rawPatterns = (match.Groups["patterns"].Value ?? string.Empty).Trim();
            if (rawPatterns.EndsWith(".", StringComparison.Ordinal)) {
                rawPatterns = rawPatterns.Substring(0, rawPatterns.Length - 1).TrimEnd();
            }

            if (rawPatterns.Length == 0) {
                return Array.Empty<string>();
            }

            var parsed = rawPatterns
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parsed.Length == 0 ? Array.Empty<string>() : parsed;
        }

        private static bool ToolCallSetContainsPattern(IReadOnlyList<ToolCall> calls, string pattern) {
            var candidatePattern = (pattern ?? string.Empty).Trim();
            if (candidatePattern.Length == 0) {
                return false;
            }

            for (var i = 0; i < calls.Count; i++) {
                var toolName = (calls[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                if (PatternMatchesToolName(candidatePattern, toolName)) {
                    return true;
                }
            }

            return false;
        }

        private static bool PatternMatchesToolName(string pattern, string toolName) {
            var expected = (pattern ?? string.Empty).Trim();
            var actual = (toolName ?? string.Empty).Trim();
            if (expected.Length == 0 || actual.Length == 0) {
                return false;
            }

            if (string.Equals(expected, "*", StringComparison.Ordinal)) {
                return true;
            }

            var wildcardIndex = expected.IndexOf('*');
            if (wildcardIndex < 0) {
                return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
            }

            var regexPattern = "^" + Regex.Escape(expected).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(actual, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string BuildScenarioContractRepairRetryPrompt(
            string userRequest,
            string assistantDraft,
            IReadOnlyList<ToolCall> calls,
            int retryAttempt) {
            var request = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
            var draft = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
            _ = retryAttempt;
            var safeCalls = calls ?? Array.Empty<ToolCall>();
            var observedToolCalls = Math.Max(0, safeCalls.Count);

            var contractSummary = "unable to parse scenario requirements.";
            var qualifyingPatternHint = string.Empty;
            if (TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) && requirements is not null) {
                var minCalls = Math.Max(0, requirements.MinToolCalls);
                var distinctParts = new List<string>();
                foreach (var requirement in requirements.MinDistinctToolInputValues) {
                    var observedValues = CollectDistinctToolInputValuesByKey(safeCalls, requirement.Key);
                    distinctParts.Add(requirement.Key + "=" + observedValues.Count + "/" + Math.Max(0, requirement.Value));
                }

                var requiredAllCount = requirements.RequiredTools.Count;
                var requiredAllMatched = requirements.RequiredTools.Count(pattern => ToolCallSetContainsPattern(safeCalls, pattern));
                var requiredAnyCount = requirements.RequiredAnyTools.Count;
                var requiredAnyMatched = requirements.RequiredAnyTools.Any(pattern => ToolCallSetContainsPattern(safeCalls, pattern)) ? 1 : 0;

                var distinctSummary = distinctParts.Count == 0
                    ? "none"
                    : string.Join(", ", distinctParts);
                contractSummary = "min_tool_calls=" + minCalls + ", observed_tool_calls=" + observedToolCalls
                                  + ", required_all=" + requiredAllMatched + "/" + requiredAllCount
                                  + ", required_any=" + requiredAnyMatched + "/" + requiredAnyCount
                                  + ", distinct_inputs={" + distinctSummary + "}";

                var qualifyingPatterns = new List<string>();
                qualifyingPatterns.AddRange(requirements.RequiredTools);
                qualifyingPatterns.AddRange(requirements.RequiredAnyTools);
                if (qualifyingPatterns.Count > 0) {
                    qualifyingPatternHint = string.Join(", ", qualifyingPatterns.Distinct(StringComparer.OrdinalIgnoreCase));
                }
            }

            return $$"""
                [Execution correction]
                The previous assistant draft ended with partial tool execution that does not satisfy the scenario execution contract.
                Observed tool-call count so far in this turn: {{observedToolCalls}}.
                Contract progress: {{contractSummary}}.
                {{(qualifyingPatternHint.Length == 0 ? string.Empty : "Qualifying tool patterns for this turn: " + qualifyingPatternHint + ".")}}

                User request:
                {{request}}

                Previous assistant draft:
                {{draft}}

                Execute additional qualifying tool calls now in this same turn to satisfy the missing contract requirements.
                Do not ask follow-up questions before issuing additional tool calls.
                Emit at least one qualifying tool call before any narrative prose in this retry.
                If required coverage is >1 (for example min_tool_calls>=2 or machine_name>=2), issue multiple tool calls in this retry.
                Do not repeat identical tool-call signatures unless there is no alternative; prioritize missing distinct input coverage.
                Infer missing read-only inputs from prior tool outputs where possible.
                For continuation requests over remaining discovered DCs/hosts, execute calls across at least two distinct host/DC inputs.
                If current discovery returns zero hosts, use previously seen DC/host targets from this thread as fallback and proceed with best-effort execution.
                Do not claim internal retry/exhaustion limits; this is an internal execution correction path.
                If tools still cannot satisfy the missing contract requirements after best effort, state the exact blocker once.
                """;
        }

        private async Task<IReadOnlyList<ToolOutput>> ExecuteToolsAsync(IReadOnlyList<ToolCall> calls, CancellationToken cancellationToken) {
            var runInParallel = ShouldRunParallelToolExecution(calls, out var mutatingToolNames);
            if (_options.LiveProgress
                && _options.ParallelToolCalls
                && calls.Count > 1
                && !runInParallel
                && mutatingToolNames.Length > 0) {
                var listed = string.Join(", ", mutatingToolNames.Take(3));
                var suffix = mutatingToolNames.Length > 3 ? ", ..." : string.Empty;
                _status?.Invoke(
                    $"parallel safety: running sequentially because write-capable tools were requested ({listed}{suffix}). " +
                    "Use --allow-mutating-parallel-tools to override.");
            }

            if (!runInParallel || calls.Count <= 1) {
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

        private bool ShouldRunParallelToolExecution(IReadOnlyList<ToolCall> calls, out string[] mutatingToolNames) {
            mutatingToolNames = Array.Empty<string>();
            if (!_options.ParallelToolCalls || calls.Count <= 1) {
                return false;
            }

            if (_options.AllowMutatingParallelToolCalls) {
                return true;
            }

            var mutating = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < calls.Count; i++) {
                var toolName = (calls[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                if (!_registry.TryGetDefinition(toolName, out var definition)) {
                    continue;
                }

                if (definition.WriteGovernance?.IsWriteCapable != true) {
                    continue;
                }

                if (seen.Add(toolName)) {
                    mutating.Add(toolName);
                }
            }

            if (mutating.Count == 0) {
                return true;
            }

            mutatingToolNames = mutating.ToArray();
            return false;
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
            return ToolSchemaRecoveryClassifier.ShouldRetryWithoutTools(ex, options);
        }

        private sealed class ScenarioExecutionContractRequirements {
            public ScenarioExecutionContractRequirements(
                int minToolCalls,
                IReadOnlyDictionary<string, int> minDistinctToolInputValues,
                IReadOnlyList<string> requiredTools,
                IReadOnlyList<string> requiredAnyTools) {
                MinToolCalls = Math.Max(0, minToolCalls);
                MinDistinctToolInputValues = minDistinctToolInputValues
                                             ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                RequiredTools = requiredTools ?? Array.Empty<string>();
                RequiredAnyTools = requiredAnyTools ?? Array.Empty<string>();
            }

            public int MinToolCalls { get; }
            public IReadOnlyDictionary<string, int> MinDistinctToolInputValues { get; }
            public IReadOnlyList<string> RequiredTools { get; }
            public IReadOnlyList<string> RequiredAnyTools { get; }
        }
    }

    private sealed class ReplTurnResult {
        public ReplTurnResult(string text, IReadOnlyList<ToolCall> toolCalls, IReadOnlyList<ToolOutput> toolOutputs,
            TurnUsage? usage = null, int toolRounds = 0, int noToolExecutionRetries = 0) {
            Text = text ?? string.Empty;
            ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
            ToolOutputs = toolOutputs ?? Array.Empty<ToolOutput>();
            Usage = usage;
            ToolRounds = Math.Max(0, toolRounds);
            NoToolExecutionRetries = Math.Max(0, noToolExecutionRetries);
        }

        public string Text { get; }
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        public IReadOnlyList<ToolOutput> ToolOutputs { get; }
        public TurnUsage? Usage { get; }
        public int ToolRounds { get; }
        public int NoToolExecutionRetries { get; }
    }

    private sealed class ReplTurnMetrics {
        public ReplTurnMetrics(DateTime startedAtUtc, DateTime? firstDeltaAtUtc, DateTime completedAtUtc, long durationMs, long? ttftMs,
            TurnUsage? usage, int toolCallsCount, int toolRounds, int noToolExecutionRetries) {
            StartedAtUtc = startedAtUtc;
            FirstDeltaAtUtc = firstDeltaAtUtc;
            CompletedAtUtc = completedAtUtc;
            DurationMs = Math.Max(0, durationMs);
            TtftMs = ttftMs.HasValue ? Math.Max(0, ttftMs.Value) : null;
            Usage = usage;
            ToolCallsCount = Math.Max(0, toolCallsCount);
            ToolRounds = Math.Max(0, toolRounds);
            NoToolExecutionRetries = Math.Max(0, noToolExecutionRetries);
        }

        public DateTime StartedAtUtc { get; }
        public DateTime? FirstDeltaAtUtc { get; }
        public DateTime CompletedAtUtc { get; }
        public long DurationMs { get; }
        public long? TtftMs { get; }
        public TurnUsage? Usage { get; }
        public int ToolCallsCount { get; }
        public int ToolRounds { get; }
        public int NoToolExecutionRetries { get; }
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
