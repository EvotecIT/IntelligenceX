using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
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
        private const int ScenarioForcedToolChoiceRetryThreshold = 2;
        private const int MaxModelPhaseAttempts = 2;
        private const int ModelPhaseRetryBaseDelayMs = 350;
        private const int MaxRecentHostTargets = 96;
        private const int MaxRetryPromptHostTargets = 16;
        private const int MaxAutoFilledToolTargets = 4;
        private const int HostTargetSpecificityFqdnBonus = 4;
        private const int HostTargetSpecificityShortNameBonus = 1;
        private const int HostTargetSpecificityIpLiteralPenalty = 2;
        private const int HostTargetSpecificityLocalhostPenalty = 3;
        private const int MinReplicationProbeTimeoutMs = 10000;
        private const string AdDiscoveryRootDseFailureErrorCode = "not_configured";
        private const string ScenarioExecutionContractMarker = "[Scenario execution contract]";
        private const string ScenarioExecutionContractDirectiveMarker = "ix:scenario-execution:v1";
        private readonly IntelligenceXClient _client;
        private readonly ToolRegistry _registry;
        private readonly ReplOptions _options;
        private readonly string? _instructions;
        private readonly Action<string>? _status;
        private readonly List<string> _recentHostTargets = new();
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
            _recentHostTargets.Clear();
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
                        var retryKnownHostTargets = GetRecentHostTargetsSnapshot();
                        var forcedToolName = useScenarioRepairPrompt
                            ? ResolveScenarioRepairForcedToolName(
                                userRequest: text,
                                calls: calls,
                                toolDefinitions: toolDefs,
                                retryAttempt: noToolExecutionRetryCount)
                            : null;
                        var retryPrompt = useScenarioRepairPrompt
                            ? BuildScenarioContractRepairRetryPrompt(
                                userRequest: text,
                                assistantDraft: finalText,
                                calls: calls,
                                retryAttempt: noToolExecutionRetryCount,
                                knownHostTargets: retryKnownHostTargets,
                                forcedToolName: forcedToolName)
                            : BuildNoToolExecutionRetryPrompt(
                                userRequest: text,
                                assistantDraft: finalText,
                                retryAttempt: noToolExecutionRetryCount,
                                knownHostTargets: retryKnownHostTargets);
                        chatOptions.NewThread = false;
                        chatOptions.PreviousResponseId = TryGetResponseId(turn);
                        chatOptions.ToolChoice = !string.IsNullOrWhiteSpace(forcedToolName)
                            ? ToolChoice.Custom(forcedToolName!)
                            : ToolChoice.Auto;
                        if (_options.LiveProgress) {
                            _status?.Invoke(useScenarioRepairPrompt
                                ? "repairing partial scenario tool execution..."
                                : "re-planning tool execution for this turn...");
                        }
                        turn = await ChatWithToolSchemaRecoveryAsync(ChatInput.FromText(retryPrompt), chatOptions, turnToken)
                            .ConfigureAwait(false);
                        chatOptions.ToolChoice = ToolChoice.Auto;
                        continue;
                    }

                    _previousResponseId = TryGetResponseId(turn) ?? _previousResponseId;
                    return new ReplTurnResult(finalText, calls, outputs, turn.Usage, toolRounds, noToolExecutionRetryCount);
                }

                var effectiveExtracted = ApplyScenarioDistinctHostCoverageFallbacks(
                    userRequest: text,
                    calls: extracted,
                    toolDefinitions: toolDefs,
                    knownHostTargets: GetRecentHostTargetsSnapshot());
                if (_options.LiveProgress && !ReferenceEquals(effectiveExtracted, extracted)) {
                    _status?.Invoke("input-repair: enforced distinct host/DC coverage for scenario execution contract.");
                }

                toolRounds++;
                calls.AddRange(effectiveExtracted);
                RememberRecentHostTargets(effectiveExtracted);
                if (_options.LiveProgress) {
                    foreach (var call in effectiveExtracted) {
                        var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                        var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                        _status?.Invoke($"tool: {GetToolDisplayName(call.Name)}{id} args={args}");
                    }
                }
                var executed = await ExecuteToolsAsync(effectiveExtracted, turnToken).ConfigureAwait(false);
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
                return !string.IsNullOrWhiteSpace(userRequest)
                       && !IsScenarioNoToolExecutionContract(userRequest);
            }

            if (IsScenarioNoToolExecutionContract(userRequest)) {
                return false;
            }

            if (IsScenarioToolExecutionContract(userRequest)) {
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

        private static bool IsScenarioToolExecutionContract(string userRequest) {
            var request = userRequest ?? string.Empty;
            if (request.IndexOf(ScenarioExecutionContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
                return false;
            }

            if (TryParseScenarioExecutionContractBoolDirective(request, "requires_no_tool_execution", out var requiresNoToolExecution)
                && requiresNoToolExecution) {
                return false;
            }

            if (TryParseScenarioExecutionContractBoolDirective(request, "requires_tool_execution", out var requiresToolExecution)
                && requiresToolExecution) {
                return true;
            }

            if (request.IndexOf("requires tool execution before the final response", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            return TryParseScenarioExecutionContractRequirements(request, out var requirements)
                   && requirements is not null
                   && (requirements.MinToolCalls > 0
                       || requirements.MinDistinctToolInputValues.Count > 0
                       || requirements.RequiredTools.Count > 0
                       || requirements.RequiredAnyTools.Count > 0);
        }

        private static bool IsScenarioNoToolExecutionContract(string userRequest) {
            var request = userRequest ?? string.Empty;
            if (request.IndexOf(ScenarioExecutionContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
                return false;
            }

            if (TryParseScenarioExecutionContractBoolDirective(request, "requires_no_tool_execution", out var requiresNoToolExecution)) {
                return requiresNoToolExecution;
            }

            return request.IndexOf("requires a response without tool execution", StringComparison.OrdinalIgnoreCase) >= 0;
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

            if (!LooksLikeOperationalRequestShape(request)) {
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
            if (overlapRatio < 0.1d) {
                return false;
            }

            if (LooksLikeEvidenceDenseDraft(draft)) {
                return false;
            }

            var draftNovelTokenCount = Math.Max(0, draftUnique.Count - sharedCount);
            var noveltyRatio = draftUnique.Count == 0 ? 0d : (double)draftNovelTokenCount / draftUnique.Count;
            return noveltyRatio >= 0.45d;
        }

        private static bool ContainsQuestionSignal(string text) {
            var value = text ?? string.Empty;
            return value.IndexOf('?', StringComparison.Ordinal) >= 0
                   || value.IndexOf('？', StringComparison.Ordinal) >= 0
                   || value.IndexOf('¿', StringComparison.Ordinal) >= 0
                   || value.IndexOf('؟', StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeOperationalRequestShape(string text) {
            var value = text ?? string.Empty;
            if (value.Length == 0) {
                return false;
            }

            if (Regex.IsMatch(value, @"\b[\p{L}\p{N}][\p{L}\p{N}\-]*\.[\p{L}\p{N}\.\-]+\b", RegexOptions.CultureInvariant)) {
                return true;
            }

            if (Regex.IsMatch(value, @"\b\d{3,}\b", RegexOptions.CultureInvariant)) {
                return true;
            }

            if (value.IndexOf('_', StringComparison.Ordinal) >= 0
                || value.IndexOf('/', StringComparison.Ordinal) >= 0
                || value.IndexOf('\\', StringComparison.Ordinal) >= 0) {
                return true;
            }

            var uppercaseTokenCount = 0;
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
                if (token.Length < 2) {
                    continue;
                }

                var uppercase = 0;
                for (var t = 0; t < token.Length; t++) {
                    if (char.IsUpper(token[t])) {
                        uppercase++;
                    }
                }

                if (uppercase >= 2 || uppercase == token.Length) {
                    uppercaseTokenCount++;
                    if (uppercaseTokenCount >= 2) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool LooksLikeEvidenceDenseDraft(string text) {
            var value = text ?? string.Empty;
            if (value.Length == 0) {
                return false;
            }

            if (Regex.IsMatch(value, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}", RegexOptions.CultureInvariant)) {
                return true;
            }

            var longDigitRuns = 0;
            var currentDigitRun = 0;
            for (var i = 0; i < value.Length; i++) {
                if (char.IsDigit(value[i])) {
                    currentDigitRun++;
                    continue;
                }

                if (currentDigitRun >= 4) {
                    longDigitRuns++;
                }
                currentDigitRun = 0;
            }

            if (currentDigitRun >= 4) {
                longDigitRuns++;
            }

            return longDigitRuns >= 2;
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

        private static string BuildNoToolExecutionRetryPrompt(
            string userRequest,
            string assistantDraft,
            int retryAttempt,
            IReadOnlyList<string>? knownHostTargets = null) {
            var request = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
            var draft = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
            _ = retryAttempt;
            var knownHostHint = BuildKnownHostTargetHint(knownHostTargets);
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
                {{knownHostHint}}
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
            if (TryParseScenarioExecutionContractIntDirective(request, "min_tool_calls", out var parsedStructuredMinToolCalls)
                && parsedStructuredMinToolCalls > 0) {
                minToolCalls = parsedStructuredMinToolCalls;
            } else {
                var minToolCallsMatch = Regex.Match(
                    request,
                    @"Minimum tool calls in this turn:\s*(?<count>\d+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (minToolCallsMatch.Success
                    && int.TryParse(minToolCallsMatch.Groups["count"].Value, out var parsedMinToolCalls)
                    && parsedMinToolCalls > 0) {
                    minToolCalls = parsedMinToolCalls;
                }
            }

            var minDistinctToolInputValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<string> requiredTools;
            if (!TryParseScenarioExecutionContractToolPatternsDirective(request, "required_tools_all", out requiredTools)) {
                requiredTools = ParseScenarioContractToolPatterns(
                    request,
                    @"Required tool calls \(all\):\s*(?<patterns>[^\r\n]+)");
            }

            IReadOnlyList<string> requiredAnyTools;
            if (!TryParseScenarioExecutionContractToolPatternsDirective(request, "required_tools_any", out requiredAnyTools)) {
                requiredAnyTools = ParseScenarioContractToolPatterns(
                    request,
                    @"Required tool calls \(at least one\):\s*(?<patterns>[^\r\n]+)");
            }

            if (!TryParseScenarioExecutionContractDistinctInputDirective(request, "distinct_tool_inputs", minDistinctToolInputValues)) {
                var distinctMatch = Regex.Match(
                    request,
                    @"Distinct tool input value requirements:\s*(?<requirements>[^\r\n]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (distinctMatch.Success) {
                    ParseScenarioDistinctInputRequirements(distinctMatch.Groups["requirements"].Value, minDistinctToolInputValues, stripTrailingPeriod: true);
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

        private static bool TryParseScenarioExecutionContractBoolDirective(string userRequest, string key, out bool value) {
            value = false;
            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            var normalized = (rawValue ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                return false;
            }

            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "1", StringComparison.Ordinal)) {
                value = true;
                return true;
            }

            if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "0", StringComparison.Ordinal)) {
                value = false;
                return true;
            }

            return false;
        }

        private static bool TryParseScenarioExecutionContractIntDirective(string userRequest, string key, out int value) {
            value = 0;
            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            return int.TryParse((rawValue ?? string.Empty).Trim(), out value);
        }

        private static bool TryParseScenarioExecutionContractToolPatternsDirective(string userRequest, string key, out IReadOnlyList<string> patterns) {
            patterns = Array.Empty<string>();
            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            patterns = ParseScenarioContractCsvPatterns(rawValue);
            return true;
        }

        private static bool TryParseScenarioExecutionContractDistinctInputDirective(
            string userRequest,
            string key,
            Dictionary<string, int> destination) {
            if (destination is null) {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!TryReadScenarioExecutionContractDirectiveValue(userRequest, key, out var rawValue)) {
                return false;
            }

            ParseScenarioDistinctInputRequirements(rawValue, destination, stripTrailingPeriod: false);
            return true;
        }

        private static bool TryReadScenarioExecutionContractDirectiveValue(string userRequest, string key, out string value) {
            value = string.Empty;
            var request = userRequest ?? string.Empty;
            if (request.Length == 0 || string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            var markerIndex = request.IndexOf(ScenarioExecutionContractDirectiveMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) {
                return false;
            }

            var tail = request[(markerIndex + ScenarioExecutionContractDirectiveMarker.Length)..];
            var lines = tail.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                var line = (lines[i] ?? string.Empty).Trim();
                if (line.Length == 0) {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {
                    break;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0) {
                    continue;
                }

                var candidateKey = line[..separator].Trim();
                if (!string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                value = line[(separator + 1)..].Trim();
                return true;
            }

            return false;
        }

        private static IReadOnlyList<string> ParseScenarioContractCsvPatterns(string? rawPatterns) {
            var raw = (rawPatterns ?? string.Empty).Trim();
            if (raw.Length == 0 || string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) {
                return Array.Empty<string>();
            }

            var parsed = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parsed.Length == 0 ? Array.Empty<string>() : parsed;
        }

        private static void ParseScenarioDistinctInputRequirements(
            string? rawRequirements,
            Dictionary<string, int> destination,
            bool stripTrailingPeriod) {
            if (destination is null) {
                throw new ArgumentNullException(nameof(destination));
            }

            var raw = (rawRequirements ?? string.Empty).Trim();
            if (stripTrailingPeriod && raw.EndsWith(".", StringComparison.Ordinal)) {
                raw = raw.Substring(0, raw.Length - 1).TrimEnd();
            }

            if (raw.Length == 0 || string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            var segments = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

                destination[key] = parsedMinDistinct;
            }
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

            return ParseScenarioContractCsvPatterns(rawPatterns);
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

            var hasWildcard = expected.IndexOf('*') >= 0 || expected.IndexOf('?') >= 0;
            if (!hasWildcard) {
                return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
            }

            var regexPattern = "^"
                               + Regex.Escape(expected)
                                   .Replace("\\*", ".*", StringComparison.Ordinal)
                                   .Replace("\\?", ".", StringComparison.Ordinal)
                               + "$";
            return Regex.IsMatch(actual, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string BuildKnownHostTargetHint(IReadOnlyList<string>? knownHostTargets) {
            if (knownHostTargets is null || knownHostTargets.Count == 0) {
                return string.Empty;
            }

            var values = new List<string>(Math.Min(MaxRetryPromptHostTargets, knownHostTargets.Count));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < knownHostTargets.Count && values.Count < MaxRetryPromptHostTargets; i++) {
                var candidate = NormalizeHostTargetCandidate(knownHostTargets[i]);
                if (candidate.Length == 0 || !seen.Add(candidate)) {
                    continue;
                }

                values.Add(candidate);
            }

            if (values.Count == 0) {
                return string.Empty;
            }

            return "Known host/DC targets from prior tool inputs in this thread: "
                   + string.Join(", ", values)
                   + ".";
        }

        private static string BuildForcedToolHint(string? forcedToolName) {
            var toolName = (forcedToolName ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                return string.Empty;
            }

            return "Use tool '" + toolName + "' first in this retry before any narrative text.";
        }

        private static string? ResolveScenarioRepairForcedToolName(
            string userRequest,
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            int retryAttempt) {
            if (retryAttempt < ScenarioForcedToolChoiceRetryThreshold || toolDefinitions.Count == 0) {
                return null;
            }

            if (!TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) || requirements is null) {
                return null;
            }

            var patterns = new List<string>();
            if (requirements.RequiredTools.Count > 0) {
                foreach (var pattern in requirements.RequiredTools) {
                    if (ToolCallSetContainsPattern(calls, pattern)) {
                        continue;
                    }

                    patterns.Add(pattern);
                }
            }

            if (requirements.RequiredAnyTools.Count > 0) {
                var anyMatched = requirements.RequiredAnyTools.Any(pattern => ToolCallSetContainsPattern(calls, pattern));
                if (!anyMatched) {
                    patterns.AddRange(requirements.RequiredAnyTools);
                }
            }

            if (patterns.Count == 0) {
                return null;
            }

            var requiresHostTargetInputs = RequirementsNeedHostTargetCoverage(requirements);
            foreach (var pattern in patterns.Distinct(StringComparer.OrdinalIgnoreCase)) {
                var preferred = FindMatchingForcedToolName(
                    pattern: pattern,
                    toolDefinitions: toolDefinitions,
                    requireHostTargetInputs: requiresHostTargetInputs);
                if (!string.IsNullOrWhiteSpace(preferred)) {
                    return preferred;
                }
            }

            return null;
        }

        private static bool RequirementsNeedHostTargetCoverage(ScenarioExecutionContractRequirements requirements) {
            if (requirements.MinDistinctToolInputValues.Count == 0) {
                return false;
            }

            foreach (var key in requirements.MinDistinctToolInputValues.Keys) {
                var aliases = GetScenarioInputKeyAliases(key);
                if (aliases.Any(alias => string.Equals(alias, "machine_name", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "domain_controller", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "host", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "server", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "target", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(alias, "computer_name", StringComparison.OrdinalIgnoreCase))) {
                    return true;
                }
            }

            return false;
        }

        private static string? FindMatchingForcedToolName(
            string pattern,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            bool requireHostTargetInputs) {
            for (var i = 0; i < toolDefinitions.Count; i++) {
                var definition = toolDefinitions[i];
                var toolName = (definition.Name ?? string.Empty).Trim();
                if (toolName.Length == 0 || !PatternMatchesToolName(pattern, toolName)) {
                    continue;
                }

                if (requireHostTargetInputs && !ToolDefinitionSupportsHostTargetInputs(definition)) {
                    continue;
                }

                return toolName;
            }

            if (!requireHostTargetInputs) {
                return null;
            }

            // Host-target requirement is a preference for contract recovery; fallback to any matching
            // tool to avoid deadlocking when schema metadata is incomplete.
            for (var i = 0; i < toolDefinitions.Count; i++) {
                var toolName = (toolDefinitions[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                if (PatternMatchesToolName(pattern, toolName)) {
                    return toolName;
                }
            }

            return null;
        }

        private static bool ToolDefinitionSupportsHostTargetInputs(ToolDefinition definition) {
            var properties = definition.Parameters?.GetObject("properties");
            if (properties is null) {
                return false;
            }

            var candidateKeys = GetScenarioInputKeyAliases("machine_name");
            if (candidateKeys.Count == 0) {
                return false;
            }

            for (var keyIndex = 0; keyIndex < candidateKeys.Count; keyIndex++) {
                var key = candidateKeys[keyIndex];
                if (properties.GetObject(key) is not null) {
                    return true;
                }

                foreach (var pair in properties) {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildScenarioContractRepairRetryPrompt(
            string userRequest,
            string assistantDraft,
            IReadOnlyList<ToolCall> calls,
            int retryAttempt,
            IReadOnlyList<string>? knownHostTargets = null,
            string? forcedToolName = null) {
            var request = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
            var draft = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
            _ = retryAttempt;
            var safeCalls = calls ?? Array.Empty<ToolCall>();
            var observedToolCalls = Math.Max(0, safeCalls.Count);
            var knownHostHint = BuildKnownHostTargetHint(knownHostTargets);
            var forcedToolHint = BuildForcedToolHint(forcedToolName);

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
                {{forcedToolHint}}
                If required coverage is >1 (for example min_tool_calls>=2 or machine_name>=2), issue multiple tool calls in this retry.
                Do not repeat identical tool-call signatures unless there is no alternative; prioritize missing distinct input coverage.
                Infer missing read-only inputs from prior tool outputs where possible.
                For continuation requests over remaining discovered DCs/hosts, execute calls across at least two distinct host/DC inputs.
                If current discovery returns zero hosts, use previously seen DC/host targets from this thread as fallback and proceed with best-effort execution.
                {{knownHostHint}}
                Do not claim internal retry/exhaustion limits; this is an internal execution correction path.
                If tools still cannot satisfy the missing contract requirements after best effort, state the exact blocker once.
                """;
        }

        private void RememberRecentHostTargets(IReadOnlyList<ToolCall> calls) {
            if (calls.Count == 0) {
                return;
            }

            var candidateKeys = GetScenarioInputKeyAliases("machine_name");
            if (candidateKeys.Count == 0) {
                return;
            }

            for (var i = 0; i < calls.Count; i++) {
                var args = calls[i].Arguments;
                if (args is null) {
                    continue;
                }

                for (var keyIndex = 0; keyIndex < candidateKeys.Count; keyIndex++) {
                    var candidateKey = candidateKeys[keyIndex];
                    if (!TryReadToolInputValuesByKey(args, candidateKey, out var values) || values.Count == 0) {
                        continue;
                    }

                    for (var valueIndex = 0; valueIndex < values.Count; valueIndex++) {
                        var normalized = NormalizeHostTargetCandidate(values[valueIndex]);
                        if (normalized.Length == 0) {
                            continue;
                        }

                        _recentHostTargets.RemoveAll(existing =>
                            string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
                        _recentHostTargets.Add(normalized);
                        if (_recentHostTargets.Count <= MaxRecentHostTargets) {
                            continue;
                        }

                        _recentHostTargets.RemoveAt(0);
                    }
                }
            }
        }

        private string[] GetRecentHostTargetsSnapshot() {
            if (_recentHostTargets.Count == 0) {
                return Array.Empty<string>();
            }

            var start = Math.Max(0, _recentHostTargets.Count - MaxRetryPromptHostTargets);
            return _recentHostTargets
                .Skip(start)
                .Take(MaxRetryPromptHostTargets)
                .ToArray();
        }

        private static string NormalizeHostTargetCandidate(string value) {
            var candidate = (value ?? string.Empty).Trim();
            if (candidate.Length < 2 || candidate.Length > 128) {
                return string.Empty;
            }

            if (candidate.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
                return string.Empty;
            }

            return candidate;
        }

        private static List<string> OrderHostTargetCandidatesBySpecificity(IReadOnlyList<string> candidates) {
            if (candidates.Count <= 1) {
                return candidates as List<string> ?? candidates.ToList();
            }

            return candidates
                .Select(static (value, index) => new {
                    Value = value,
                    Index = index,
                    Score = ComputeHostTargetSpecificity(value)
                })
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Index)
                .Select(static candidate => candidate.Value)
                .ToList();
        }

        private static int ComputeHostTargetSpecificity(string value) {
            var candidate = NormalizeHostTargetCandidate(value);
            if (candidate.Length == 0) {
                return int.MinValue;
            }

            var score = candidate.Contains('.')
                ? HostTargetSpecificityFqdnBonus
                : HostTargetSpecificityShortNameBonus;
            if (Uri.CheckHostName(candidate) is UriHostNameType.IPv4 or UriHostNameType.IPv6) {
                score -= HostTargetSpecificityIpLiteralPenalty;
            }

            if (candidate.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) {
                score -= HostTargetSpecificityLocalhostPenalty;
            }

            return score;
        }

        private async Task<IReadOnlyList<ToolOutput>> ExecuteToolsAsync(IReadOnlyList<ToolCall> calls, CancellationToken cancellationToken) {
            var knownHostTargets = GetRecentHostTargetsSnapshot();
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

            var nonReusableIndices = GetNonReusableReadOnlyToolCallIndices(calls);
            var canonicalIndices = BuildReadOnlyCallCanonicalIndices(calls, nonReusableIndices, out var dedupedReadOnlyCalls);
            if (_options.LiveProgress && dedupedReadOnlyCalls > 0) {
                _status?.Invoke($"input-repair: deduplicated {dedupedReadOnlyCalls} identical read-only tool call signatures in this turn.");
            }

            var uniqueCanonicalIndices = GetUniqueCanonicalIndices(canonicalIndices);
            if (!runInParallel || calls.Count <= 1) {
                var canonicalOutputs = new Dictionary<int, ToolOutput>(uniqueCanonicalIndices.Length);
                for (var index = 0; index < uniqueCanonicalIndices.Length; index++) {
                    var canonicalIndex = uniqueCanonicalIndices[index];
                    canonicalOutputs[canonicalIndex] = await ExecuteToolAsync(calls[canonicalIndex], cancellationToken, knownHostTargets).ConfigureAwait(false);
                }

                return RehydrateToolOutputsFromCanonical(calls, canonicalIndices, canonicalOutputs);
            }

            var tasks = new Task<ToolOutput>[uniqueCanonicalIndices.Length];
            for (var index = 0; index < uniqueCanonicalIndices.Length; index++) {
                var canonicalIndex = uniqueCanonicalIndices[index];
                tasks[index] = ExecuteToolAsync(calls[canonicalIndex], cancellationToken, knownHostTargets);
            }

            var executed = await Task.WhenAll(tasks).ConfigureAwait(false);
            var outputsByCanonical = new Dictionary<int, ToolOutput>(uniqueCanonicalIndices.Length);
            for (var index = 0; index < uniqueCanonicalIndices.Length; index++) {
                outputsByCanonical[uniqueCanonicalIndices[index]] = executed[index];
            }

            return RehydrateToolOutputsFromCanonical(calls, canonicalIndices, outputsByCanonical);
        }

        private ISet<int> GetNonReusableReadOnlyToolCallIndices(IReadOnlyList<ToolCall> calls) {
            var nonReusable = new HashSet<int>();
            for (var i = 0; i < calls.Count; i++) {
                var toolName = (calls[i].Name ?? string.Empty).Trim();
                if (toolName.Length == 0 || !_registry.TryGetDefinition(toolName, out var definition)) {
                    nonReusable.Add(i);
                    continue;
                }

                if (definition.WriteGovernance?.IsWriteCapable == true) {
                    nonReusable.Add(i);
                }
            }

            return nonReusable;
        }

        private static int[] BuildReadOnlyCallCanonicalIndices(
            IReadOnlyList<ToolCall> calls,
            ISet<int> nonReusableIndices,
            out int dedupedCount) {
            dedupedCount = 0;
            var canonicalIndices = new int[calls.Count];
            var bySignature = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < calls.Count; i++) {
                canonicalIndices[i] = i;
                if (nonReusableIndices.Contains(i)
                    || !TryBuildToolCallExecutionSignature(calls[i], out var signature)) {
                    continue;
                }

                if (bySignature.TryGetValue(signature, out var existingCanonicalIndex)) {
                    canonicalIndices[i] = existingCanonicalIndex;
                    dedupedCount++;
                    continue;
                }

                bySignature[signature] = i;
            }

            return canonicalIndices;
        }

        private static int[] GetUniqueCanonicalIndices(IReadOnlyList<int> canonicalIndices) {
            var unique = new List<int>(canonicalIndices.Count);
            var seen = new HashSet<int>();
            for (var i = 0; i < canonicalIndices.Count; i++) {
                var canonical = canonicalIndices[i];
                if (seen.Add(canonical)) {
                    unique.Add(canonical);
                }
            }

            return unique.ToArray();
        }

        private static IReadOnlyList<ToolOutput> RehydrateToolOutputsFromCanonical(
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<int> canonicalIndices,
            IReadOnlyDictionary<int, ToolOutput> outputsByCanonical) {
            var outputs = new List<ToolOutput>(calls.Count);
            for (var i = 0; i < calls.Count; i++) {
                var canonicalIndex = canonicalIndices[i];
                if (!outputsByCanonical.TryGetValue(canonicalIndex, out var canonicalOutput)) {
                    outputs.Add(new ToolOutput(calls[i].CallId, ToolOutputEnvelope.Error(
                        errorCode: "tool_output_missing",
                        error: $"Missing canonical tool output for call '{calls[i].CallId}'.",
                        hints: new[] { "Re-run the turn; this indicates an internal orchestration mismatch." },
                        isTransient: true)));
                    continue;
                }

                if (canonicalIndex == i && string.Equals(canonicalOutput.CallId, calls[i].CallId, StringComparison.Ordinal)) {
                    outputs.Add(canonicalOutput);
                    continue;
                }

                outputs.Add(new ToolOutput(calls[i].CallId, canonicalOutput.Output));
            }

            return outputs;
        }

        private static bool TryBuildToolCallExecutionSignature(ToolCall call, out string signature) {
            signature = string.Empty;
            var toolName = (call.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                return false;
            }

            var normalizedToolName = toolName.ToLowerInvariant();
            if (call.Arguments is not null) {
                signature = normalizedToolName + "|" + JsonLite.Serialize(JsonValue.From(call.Arguments));
                return true;
            }

            var normalizedInput = (call.Input ?? string.Empty).Trim();
            signature = normalizedToolName + "|" + normalizedInput;
            return true;
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

        private async Task<ToolOutput> ExecuteToolAsync(
            ToolCall call,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? knownHostTargets = null) {
            if (!_registry.TryGet(call.Name, out var tool)) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_not_registered",
                    error: $"Tool '{call.Name}' is not registered.",
                    hints: new[] { "Run /tools to list available tools.", "Check that the correct packs are enabled." },
                    isTransient: false));
            }

            _registry.TryGetDefinition(call.Name, out var definition);
            var effectiveCall = ApplyKnownHostTargetFallbacks(call, definition, knownHostTargets);
            if (_options.LiveProgress && !ReferenceEquals(effectiveCall, call)) {
                _status?.Invoke($"input-repair: auto-filled host targets for {GetToolDisplayName(call.Name)} from thread context.");
            }

            if (_options.LiveProgress) {
                var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                _status?.Invoke($"running: {GetToolDisplayName(call.Name)}{id}");
            }
            using var toolCts = CreateTimeoutCts(cancellationToken, _options.ToolTimeoutSeconds);
            var toolToken = toolCts?.Token ?? cancellationToken;
            try {
                var result = await tool.InvokeAsync(effectiveCall.Arguments, toolToken).ConfigureAwait(false);
                var repairedCall = ApplyAdDiscoveryRootDseFallback(effectiveCall, result);
                if (!ReferenceEquals(repairedCall, effectiveCall)) {
                    if (_options.LiveProgress) {
                        _status?.Invoke("input-repair: retrying AD discovery without pinned domain_controller after RootDSE failure.");
                    }

                    var repairedResult = await tool.InvokeAsync(repairedCall.Arguments, toolToken).ConfigureAwait(false);
                    if (TryReadToolOutputOk(repairedResult, out var repairedOk) && repairedOk) {
                        return new ToolOutput(repairedCall.CallId, repairedResult ?? string.Empty);
                    }
                }

                var replicationRepairedCall = ApplyAdReplicationProbeFallback(effectiveCall, result, knownHostTargets);
                if (!ReferenceEquals(replicationRepairedCall, effectiveCall)) {
                    if (_options.LiveProgress) {
                        _status?.Invoke("input-repair: retrying replication probe with expanded timeout and normalized DC target.");
                    }

                    var repairedResult = await tool.InvokeAsync(replicationRepairedCall.Arguments, toolToken).ConfigureAwait(false);
                    if (TryReadToolOutputOk(repairedResult, out var repairedOk) && repairedOk) {
                        return new ToolOutput(replicationRepairedCall.CallId, repairedResult ?? string.Empty);
                    }

                    return new ToolOutput(replicationRepairedCall.CallId, repairedResult ?? string.Empty);
                }

                return new ToolOutput(effectiveCall.CallId, result ?? string.Empty);
            } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                return new ToolOutput(effectiveCall.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_timeout",
                    error: $"Tool '{call.Name}' timed out after {_options.ToolTimeoutSeconds}s.",
                    hints: new[] { "Increase --tool-timeout-seconds, or narrow the query (OU scoping, tighter filters)." },
                    isTransient: true));
            } catch (Exception ex) {
                return new ToolOutput(effectiveCall.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_exception",
                    error: $"{ex.GetType().Name}: {ex.Message}",
                    hints: new[] { "Try again. If it keeps failing, re-run with --echo-tool-outputs to capture details." },
                    isTransient: false));
            }
        }

        private static ToolCall ApplyAdDiscoveryRootDseFallback(ToolCall call, string toolOutput) {
            if (call.Arguments is null || !IsAdDiscoveryToolName(call.Name)) {
                return call;
            }

            if (!TryReadToolInputValuesByKey(call.Arguments, "domain_controller", out var configuredDomainControllers)
                || configuredDomainControllers.Count == 0) {
                return call;
            }

            var pinnedDomainController = configuredDomainControllers
                .Select(NormalizeHostTargetCandidate)
                .FirstOrDefault(static value => value.Length > 0);
            if (string.IsNullOrWhiteSpace(pinnedDomainController)
                || !LooksLikePinnedDomainControllerRootDseFailure(toolOutput, pinnedDomainController)) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            var replacedDomainController = false;
            foreach (var pair in call.Arguments) {
                if (!string.Equals(pair.Key, "domain_controller", StringComparison.OrdinalIgnoreCase)) {
                    rewrittenArguments.Add(pair.Key, pair.Value);
                    continue;
                }

                if (replacedDomainController) {
                    continue;
                }

                rewrittenArguments.Add(pair.Key, string.Empty);
                replacedDomainController = true;
            }

            if (!replacedDomainController) {
                rewrittenArguments.Add("domain_controller", string.Empty);
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static ToolCall ApplyAdReplicationProbeFallback(
            ToolCall call,
            string toolOutput,
            IReadOnlyList<string>? knownHostTargets) {
            if (call.Arguments is null
                || !string.Equals(call.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)
                || !IsReplicationProbeCall(call.Arguments)
                || !TryReadToolOutputFailure(toolOutput, out var errorCode, out var errorMessage)) {
                return call;
            }

            var looksLikeTimeout = string.Equals(errorCode, "timeout", StringComparison.OrdinalIgnoreCase)
                                   || errorMessage.Contains("Replication query timed out", StringComparison.OrdinalIgnoreCase);
            var looksLikeNoData = errorMessage.Contains("No replication data returned", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeTimeout && !looksLikeNoData) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            foreach (var pair in call.Arguments) {
                rewrittenArguments.Add(pair.Key, pair.Value ?? JsonValue.Null);
            }

            var changed = false;
            if (looksLikeTimeout) {
                var configuredTimeout = rewrittenArguments.GetInt64("timeout_ms") ?? 0;
                if (configuredTimeout <= 0 || configuredTimeout < MinReplicationProbeTimeoutMs) {
                    rewrittenArguments.Add("timeout_ms", MinReplicationProbeTimeoutMs);
                    changed = true;
                }
            }

            if (TryPromoteReplicationProbeHostInputsToFqdn(rewrittenArguments, knownHostTargets)) {
                changed = true;
            }

            if (!changed) {
                return call;
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static bool IsReplicationProbeCall(JsonObject arguments) {
            var probeKind = arguments.GetString("probe_kind") ?? string.Empty;
            return string.Equals(probeKind, "replication", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryPromoteReplicationProbeHostInputsToFqdn(
            JsonObject arguments,
            IReadOnlyList<string>? knownHostTargets) {
            if (knownHostTargets is null || knownHostTargets.Count == 0) {
                return false;
            }

            var knownFqdns = OrderHostTargetCandidatesBySpecificity(knownHostTargets)
                .Select(NormalizeHostTargetCandidate)
                .Where(static value => value.Contains('.'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (knownFqdns.Length == 0) {
                return false;
            }

            var changed = false;
            if (TryPromoteStringHostArgument(arguments, "domain_controller", knownFqdns)) {
                changed = true;
            }

            if (TryPromoteStringArrayHostArgument(arguments, "targets", knownFqdns)) {
                changed = true;
            }

            if (TryPromoteStringArrayHostArgument(arguments, "include_domain_controllers", knownFqdns)) {
                changed = true;
            }

            return changed;
        }

        private static bool TryPromoteStringHostArgument(JsonObject arguments, string key, IReadOnlyList<string> knownFqdns) {
            var current = arguments.GetString(key) ?? string.Empty;
            if (!TryResolveKnownHostFqdn(current, knownFqdns, out var resolved)) {
                return false;
            }

            arguments.Add(key, resolved);
            return true;
        }

        private static bool TryPromoteStringArrayHostArgument(JsonObject arguments, string key, IReadOnlyList<string> knownFqdns) {
            if (arguments.GetArray(key) is not JsonArray values || values.Count == 0) {
                return false;
            }

            var patched = new JsonArray();
            var changed = false;
            for (var i = 0; i < values.Count; i++) {
                var original = values[i]?.AsString() ?? string.Empty;
                if (TryResolveKnownHostFqdn(original, knownFqdns, out var resolved)) {
                    patched.Add(resolved);
                    changed = true;
                    continue;
                }

                patched.Add(original);
            }

            if (!changed) {
                return false;
            }

            arguments.Add(key, patched);
            return true;
        }

        private static bool TryResolveKnownHostFqdn(string value, IReadOnlyList<string> knownFqdns, out string resolved) {
            resolved = string.Empty;
            var normalized = NormalizeHostTargetCandidate(value);
            if (normalized.Length == 0) {
                return false;
            }

            for (var i = 0; i < knownFqdns.Count; i++) {
                var candidate = knownFqdns[i];
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase)) {
                    resolved = candidate;
                    return true;
                }
            }

            for (var i = 0; i < knownFqdns.Count; i++) {
                var candidate = knownFqdns[i];
                if (candidate.StartsWith(normalized + ".", StringComparison.OrdinalIgnoreCase)) {
                    resolved = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadToolOutputFailure(string output, out string errorCode, out string errorMessage) {
            errorCode = string.Empty;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(output)) {
                return false;
            }

            var envelope = JsonLite.Parse(output)?.AsObject();
            if (envelope is null || !TryReadToolOutputOk(output, out var ok) || ok) {
                return false;
            }

            errorCode = envelope.GetString("error_code") ?? string.Empty;
            errorMessage = envelope.GetString("error") ?? string.Empty;
            var failure = envelope.GetObject("failure");
            if (errorCode.Length == 0) {
                errorCode = failure?.GetString("code") ?? string.Empty;
            }
            if (errorMessage.Length == 0) {
                errorMessage = failure?.GetString("message") ?? string.Empty;
            }

            return errorCode.Length > 0 || errorMessage.Length > 0;
        }

        private static bool IsAdDiscoveryToolName(string toolName) {
            var normalized = (toolName ?? string.Empty).Trim();
            return string.Equals(normalized, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "ad_forest_discover", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePinnedDomainControllerRootDseFailure(string toolOutput, string pinnedDomainController) {
            if (string.IsNullOrWhiteSpace(toolOutput) || string.IsNullOrWhiteSpace(pinnedDomainController)) {
                return false;
            }

            JsonObject? envelope;
            try {
                envelope = JsonLite.Parse(toolOutput)?.AsObject();
            } catch {
                return false;
            }

            if (envelope is null) {
                return false;
            }

            bool ok;
            try {
                ok = envelope.GetBoolean("ok", defaultValue: false);
            } catch {
                ok = false;
            }

            if (ok) {
                return false;
            }

            var errorCode = envelope.GetString("error_code") ?? string.Empty;
            var errorMessage = envelope.GetString("error") ?? string.Empty;
            var failure = envelope.GetObject("failure");
            if (errorCode.Length == 0) {
                errorCode = failure?.GetString("code") ?? string.Empty;
            }
            if (errorMessage.Length == 0) {
                errorMessage = failure?.GetString("message") ?? string.Empty;
            }

            if (!string.Equals(errorCode, AdDiscoveryRootDseFailureErrorCode, StringComparison.OrdinalIgnoreCase)
                || errorMessage.Length == 0) {
                return false;
            }

            return errorMessage.Contains("RootDSE", StringComparison.OrdinalIgnoreCase)
                   && errorMessage.Contains(pinnedDomainController, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<ToolCall> ApplyScenarioDistinctHostCoverageFallbacks(
            string userRequest,
            IReadOnlyList<ToolCall> calls,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            IReadOnlyList<string>? knownHostTargets) {
            if (calls.Count == 0 || toolDefinitions.Count == 0 || knownHostTargets is null || knownHostTargets.Count == 0) {
                return calls;
            }

            if (!TryParseScenarioExecutionContractRequirements(userRequest, out var requirements) || requirements is null) {
                return calls;
            }

            var requiredDistinctHostCoverage = GetRequiredDistinctHostCoverage(requirements);
            if (requiredDistinctHostCoverage <= 1) {
                return calls;
            }

            var observedDistinctTargets = CollectDistinctToolInputValuesByKey(calls, "machine_name");
            if (observedDistinctTargets.Count >= requiredDistinctHostCoverage) {
                return calls;
            }

            var fallbackTargets = new List<string>();
            var seenFallbackTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < knownHostTargets.Count; i++) {
                var normalized = NormalizeHostTargetCandidate(knownHostTargets[i]);
                if (normalized.Length == 0
                    || observedDistinctTargets.Contains(normalized)
                    || !seenFallbackTargets.Add(normalized)) {
                    continue;
                }

                fallbackTargets.Add(normalized);
            }

            if (fallbackTargets.Count == 0) {
                return calls;
            }

            fallbackTargets = OrderHostTargetCandidatesBySpecificity(fallbackTargets);
            var patchedCalls = calls.ToArray();
            var patchedAny = false;
            for (var i = 0; i < patchedCalls.Length
                            && observedDistinctTargets.Count < requiredDistinctHostCoverage
                            && fallbackTargets.Count > 0; i++) {
                var originalCall = patchedCalls[i];
                if (originalCall.Arguments is null) {
                    continue;
                }

                ToolDefinition? definition = null;
                for (var definitionIndex = 0; definitionIndex < toolDefinitions.Count; definitionIndex++) {
                    var candidateName = (toolDefinitions[definitionIndex].Name ?? string.Empty).Trim();
                    if (!string.Equals(candidateName, originalCall.Name, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    definition = toolDefinitions[definitionIndex];
                    break;
                }

                var fallbackTarget = fallbackTargets[0];
                var patchedCall = ApplyHostTargetOverride(originalCall, definition, fallbackTarget);
                if (ReferenceEquals(patchedCall, originalCall)) {
                    continue;
                }

                patchedCalls[i] = patchedCall;
                patchedAny = true;
                observedDistinctTargets.Add(fallbackTarget);
                fallbackTargets.RemoveAt(0);
            }

            return patchedAny ? patchedCalls : calls;
        }

        private static int GetRequiredDistinctHostCoverage(ScenarioExecutionContractRequirements requirements) {
            if (requirements is null || requirements.MinDistinctToolInputValues.Count == 0) {
                return 0;
            }

            var requiredDistinctHostCoverage = 0;
            foreach (var requirement in requirements.MinDistinctToolInputValues) {
                var requiredDistinct = Math.Max(0, requirement.Value);
                if (requiredDistinct <= 1) {
                    continue;
                }

                var aliases = GetScenarioInputKeyAliases(requirement.Key);
                if (!aliases.Any(IsHostTargetAlias)) {
                    continue;
                }

                requiredDistinctHostCoverage = Math.Max(requiredDistinctHostCoverage, requiredDistinct);
            }

            return requiredDistinctHostCoverage;
        }

        private static bool IsHostTargetAlias(string key) {
            return string.Equals(key, "machine_name", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "domain_controller", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "host", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "server", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "target", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "targets", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "servers", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(key, "computer_name", StringComparison.OrdinalIgnoreCase);
        }

        private static ToolCall ApplyHostTargetOverride(ToolCall call, ToolDefinition? definition, string hostTarget) {
            if (call.Arguments is null) {
                return call;
            }

            var normalizedTarget = NormalizeHostTargetCandidate(hostTarget);
            if (normalizedTarget.Length == 0) {
                return call;
            }

            if (!TryPickHostTargetInputKey(call, definition, out var targetKey, out var keyIsArray)) {
                return call;
            }

            var rewrittenArguments = new JsonObject(StringComparer.Ordinal);
            var replaced = false;
            foreach (var pair in call.Arguments) {
                if (!IsHostTargetAlias(pair.Key)) {
                    rewrittenArguments.Add(pair.Key, pair.Value ?? JsonValue.Null);
                    continue;
                }

                // Keep host-target aliases internally consistent within the same call. If any fallback
                // host is applied, every present host/DC alias should resolve to that same host target.
                AddHostTargetValue(
                    rewrittenArguments,
                    pair.Key,
                    normalizedTarget,
                    asArray: pair.Value?.AsArray() is not null);
                replaced = true;
            }

            if (!replaced) {
                AddHostTargetValue(rewrittenArguments, targetKey, normalizedTarget, keyIsArray);
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(rewrittenArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, rewrittenArguments, call.Raw);
        }

        private static bool TryPickHostTargetInputKey(
            ToolCall call,
            ToolDefinition? definition,
            out string key,
            out bool keyIsArray) {
            key = string.Empty;
            keyIsArray = false;
            if (call.Arguments is null) {
                return false;
            }

            var preferredKeys = new[] {
                "machine_name",
                "domain_controller",
                "host",
                "server",
                "computer_name",
                "target",
                "targets",
                "servers"
            };

            for (var preferredKeyIndex = 0; preferredKeyIndex < preferredKeys.Length; preferredKeyIndex++) {
                var preferredKey = preferredKeys[preferredKeyIndex];
                foreach (var pair in call.Arguments) {
                    if (!string.Equals(pair.Key, preferredKey, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    key = pair.Key;
                    keyIsArray = pair.Value?.AsArray() is not null;
                    return true;
                }
            }

            if (definition is null) {
                return false;
            }

            for (var preferredKeyIndex = 0; preferredKeyIndex < preferredKeys.Length; preferredKeyIndex++) {
                var preferredKey = preferredKeys[preferredKeyIndex];
                if (!ToolDefinitionHasInputProperty(definition, preferredKey)) {
                    continue;
                }

                key = preferredKey;
                keyIsArray = string.Equals(preferredKey, "targets", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(preferredKey, "servers", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            return false;
        }

        private static void AddHostTargetValue(JsonObject arguments, string key, string value, bool asArray) {
            if (!asArray) {
                arguments.Add(key, value);
                return;
            }

            var array = new JsonArray();
            array.Add(value);
            arguments.Add(key, array);
        }

        private static ToolCall ApplyKnownHostTargetFallbacks(
            ToolCall call,
            ToolDefinition? definition,
            IReadOnlyList<string>? knownHostTargets) {
            if (definition is null || knownHostTargets is null || knownHostTargets.Count == 0 || call.Arguments is null) {
                return call;
            }

            if (!ToolDefinitionSupportsHostTargetInputs(definition)) {
                return call;
            }

            var candidateInputKeys = GetScenarioInputKeyAliases("machine_name");
            for (var keyIndex = 0; keyIndex < candidateInputKeys.Count; keyIndex++) {
                if (!TryReadToolInputValuesByKey(call.Arguments, candidateInputKeys[keyIndex], out var values) || values.Count == 0) {
                    continue;
                }

                return call;
            }

            var supportsTarget = ToolDefinitionHasInputProperty(definition, "target");
            var supportsTargets = ToolDefinitionHasInputProperty(definition, "targets");
            if (!supportsTarget && !supportsTargets) {
                return call;
            }

            var normalizedTargets = new List<string>(Math.Min(MaxAutoFilledToolTargets, knownHostTargets.Count));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < knownHostTargets.Count && normalizedTargets.Count < MaxAutoFilledToolTargets; i++) {
                var normalized = NormalizeHostTargetCandidate(knownHostTargets[i]);
                if (normalized.Length == 0 || !seen.Add(normalized)) {
                    continue;
                }

                normalizedTargets.Add(normalized);
            }

            if (normalizedTargets.Count == 0) {
                return call;
            }

            normalizedTargets = OrderHostTargetCandidatesBySpecificity(normalizedTargets);
            var patchedArguments = new JsonObject(StringComparer.Ordinal);
            foreach (var pair in call.Arguments) {
                patchedArguments.Add(pair.Key, pair.Value);
            }

            if (supportsTarget) {
                patchedArguments.Add("target", normalizedTargets[0]);
            }

            if (supportsTargets) {
                var targetsArray = new JsonArray();
                for (var i = 0; i < normalizedTargets.Count; i++) {
                    targetsArray.Add(normalizedTargets[i]);
                }

                patchedArguments.Add("targets", targetsArray);
            }

            var patchedInput = JsonLite.Serialize(JsonValue.From(patchedArguments));
            return new ToolCall(call.CallId, call.Name, patchedInput, patchedArguments, call.Raw);
        }

        private static bool ToolDefinitionHasInputProperty(ToolDefinition definition, string key) {
            if (definition is null || string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            var properties = definition.Parameters?.GetObject("properties");
            if (properties is null) {
                return false;
            }

            foreach (var pair in properties) {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
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
            for (var attempt = 0; attempt < MaxModelPhaseAttempts; attempt++) {
                var attemptOptions = options.Clone();
                try {
                    return await ChatWithToolSchemaRecoverySingleAttemptAsync(input, attemptOptions, cancellationToken).ConfigureAwait(false);
                } catch (Exception ex) when (ShouldRetryModelPhaseAttempt(ex, attempt, MaxModelPhaseAttempts, cancellationToken)) {
                    if (_options.LiveProgress) {
                        _status?.Invoke("transient model error; retrying...");
                    }

                    var delayMs = ModelPhaseRetryBaseDelayMs * (attempt + 1);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Model phase retry loop exhausted without returning a result.");
        }

        private async Task<TurnInfo> ChatWithToolSchemaRecoverySingleAttemptAsync(ChatInput input, ChatOptions options,
            CancellationToken cancellationToken) {
            try {
                return await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ShouldRetryWithoutTools(ex, options)) {
                options.Tools = null;
                options.ToolChoice = null;
                return await _client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool ShouldRetryModelPhaseAttempt(Exception ex, int attempt, int maxAttempts, CancellationToken cancellationToken) {
            if (attempt + 1 >= maxAttempts) {
                return false;
            }

            if (cancellationToken.IsCancellationRequested || ex is OperationCanceledException) {
                return false;
            }

            if (ex is OpenAIAuthenticationRequiredException) {
                return false;
            }

            if (LooksLikeToolOutputPairingReferenceGap(ex)) {
                return true;
            }

            return HasRetryableTransportFailureInChain(ex);
        }

        private static bool HasRetryableTransportFailureInChain(Exception ex) {
            var depth = 0;
            for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
                if (current is TimeoutException || current is IOException || current is HttpRequestException) {
                    return true;
                }

                var statusCode = TryGetStatusCodeFromExceptionData(current);
                if (statusCode is >= 500) {
                    return true;
                }

                var message = (current.Message ?? string.Empty).Trim();
                if (message.Length == 0) {
                    continue;
                }

                if (message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("unexpected end of stream", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("server disconnected", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("server had an error processing your request", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("bad gateway", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("gateway timeout", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static int? TryGetStatusCodeFromExceptionData(Exception ex) {
            if (ex?.Data is null) {
                return null;
            }

            var raw = ex.Data["openai:status_code"];
            return raw switch {
                int intCode => intCode,
                long longCode => (int)longCode,
                short shortCode => shortCode,
                byte byteCode => byteCode,
                string text when int.TryParse(text, out var parsed) => parsed,
                _ => null
            };
        }

        private static bool LooksLikeToolOutputPairingReferenceGap(Exception ex) {
            var depth = 0;
            for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
                var message = (current.Message ?? string.Empty).Trim();
                if (message.Length == 0) {
                    continue;
                }

                if (message.Contains("No tool call found for custom tool call output", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("custom tool call output with call_id", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("No tool output found for function call", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("No tool output found for custom tool call", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("No tool call found for function call output", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
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
