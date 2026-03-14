using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
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

    private sealed partial class ReplSession {
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
                       || requirements.RequiredAnyTools.Count > 0
                       || requirements.ForbiddenToolInputValues.Count > 0);
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
            IReadOnlyList<ToolDefinition>? toolDefinitions,
            IReadOnlyList<string>? knownHostTargets = null) {
            return BuildNoToolExecutionRetryPrompt(
                userRequest,
                assistantDraft,
                retryAttempt,
                toolDefinitions,
                knownHostTargets,
                orchestrationCatalog: null);
        }

        private static string BuildNoToolExecutionRetryPrompt(
            string userRequest,
            string assistantDraft,
            int retryAttempt,
            IReadOnlyList<ToolDefinition>? toolDefinitions,
            IReadOnlyList<string>? knownHostTargets,
            ToolOrchestrationCatalog? orchestrationCatalog) {
            var request = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
            var draft = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
            _ = retryAttempt;
            var knownHostHint = BuildKnownHostTargetHint(knownHostTargets);
            var contractHintLines = BuildToolContractPromptHintLines(
                toolDefinitions: toolDefinitions,
                toolPatterns: null,
                includeRemoteHostFallbackHint: knownHostTargets is { Count: > 0 },
                orchestrationCatalog: orchestrationCatalog);
            var executionAvailabilityHintLines = BuildToolExecutionAvailabilityHintLines(
                toolDefinitions: toolDefinitions,
                toolPatterns: null,
                knownHostTargets: knownHostTargets);
            var combinedHintLines = new List<string>(contractHintLines.Count + executionAvailabilityHintLines.Count);
            combinedHintLines.AddRange(contractHintLines);
            combinedHintLines.AddRange(executionAvailabilityHintLines);
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
                {{FormatRetryPromptHintLines(combinedHintLines)}}
                If this is a continuation request over "remaining discovered DCs/hosts", execute multiple best-effort tool calls using distinct host/DC inputs from thread context.
                If discovery appears empty in this turn, still use previously seen DC/host targets from thread context rather than stopping at narration.
                {{knownHostHint}}
                Do not claim internal retry/exhaustion limits; this is an internal execution correction path.
                If tools still cannot satisfy this request after a best-effort tool attempt, state the exact blocker and the minimal missing input once.
                """;
        }

        private static string FormatRetryPromptHintLines(IReadOnlyList<string> lines) {
            if (lines is null || lines.Count == 0) {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < lines.Count; i++) {
                var line = (lines[i] ?? string.Empty).Trim();
                if (line.Length == 0) {
                    continue;
                }

                if (sb.Length > 0) {
                    sb.AppendLine();
                }

                sb.Append(line);
            }

            return sb.ToString();
        }

    }
}
