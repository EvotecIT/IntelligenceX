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

    private static List<ToolRoutingInsight> BuildContinuationRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs) {
        var list = new List<ToolRoutingInsight>(selectedDefs.Count);
        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = selectedDefs[i].Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            list.Add(new ToolRoutingInsight(
                ToolName: name.Trim(),
                Confidence: "high",
                Score: 1d,
                Reason: "continuation follow-up reuse"));
        }

        return list;
    }

    private async Task EmitRoutingInsightsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolRoutingInsight> insights) {
        if (insights.Count == 0) {
            return;
        }

        for (var i = 0; i < insights.Count; i++) {
            var insight = insights[i];
            var payload = JsonSerializer.Serialize(new {
                confidence = insight.Confidence,
                score = insight.Score,
                reason = insight.Reason
            });
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "routing_tool",
                    toolName: insight.ToolName,
                    message: payload)
                .ConfigureAwait(false);
        }
    }

    private bool TryGetContinuationToolSubset(string threadId, string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        subset = Array.Empty<ToolDefinition>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !LooksLikeContinuationFollowUp(userRequest)) {
            return false;
        }

        string[]? previousNames;
        lock (_toolRoutingContextLock) {
            if (!_lastWeightedToolNamesByThreadId.TryGetValue(normalizedThreadId, out previousNames) || previousNames.Length == 0) {
                return false;
            }

            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContexts();
        }

        var preferred = new HashSet<string>(previousNames!, StringComparer.OrdinalIgnoreCase);
        var selected = new List<ToolDefinition>();
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (preferred.Contains(definition.Name)) {
                selected.Add(definition);
            }
        }

        if (selected.Count < 2) {
            return false;
        }

        subset = selected;
        return true;
    }

    private void RememberWeightedToolSubset(string threadId, IReadOnlyList<ToolDefinition> selectedDefinitions, int allToolCount) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            if (selectedDefinitions.Count == 0 || selectedDefinitions.Count >= allToolCount) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                return;
            }

            var names = new List<string>(selectedDefinitions.Count);
            for (var i = 0; i < selectedDefinitions.Count && i < 64; i++) {
                var name = (selectedDefinitions[i].Name ?? string.Empty).Trim();
                if (name.Length > 0) {
                    names.Add(name);
                }
            }

            _lastWeightedToolNamesByThreadId[normalizedThreadId] = names.Count == 0 ? Array.Empty<string>() : names.ToArray();
            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContexts();
        }
    }

    private void RememberUserIntent(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || LooksLikeContinuationFollowUp(normalized)) {
            return;
        }

        if (normalized.Length > 600) {
            normalized = normalized.Substring(0, 600);
        }

        lock (_toolRoutingContextLock) {
            _lastUserIntentByThreadId[normalizedThreadId] = normalized;
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContexts();
        }
    }

    private string ExpandContinuationUserRequest(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return userRequest;
        }

        var raw = userRequest ?? string.Empty;
        if (TryResolvePendingActionSelection(normalizedThreadId, raw, out var resolved)) {
            return resolved;
        }

        var normalized = raw.Trim();
        if (normalized.Length == 0 || !LooksLikeContinuationFollowUp(normalized)) {
            return raw;
        }

        string? intent;
        long intentTicks;
        lock (_toolRoutingContextLock) {
            if (!_lastUserIntentByThreadId.TryGetValue(normalizedThreadId, out intent) || string.IsNullOrWhiteSpace(intent)) {
                return raw;
            }

            intentTicks = _lastUserIntentSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (intentTicks > 0) {
            if (intentTicks < DateTime.MinValue.Ticks || intentTicks > DateTime.MaxValue.Ticks) {
                // Defensive: avoid exceptions from ticks->DateTime conversion if ticks are corrupted/out of range.
                return raw;
            }
            var age = DateTime.UtcNow - new DateTime(intentTicks, DateTimeKind.Utc);
            if (age > UserIntentContextMaxAge) {
                return raw;
            }
        }

        lock (_toolRoutingContextLock) {
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContexts();
        }

        var expanded = $"{intent!.Trim()}\nFollow-up: {normalized}";
        return expanded.Length <= 900 ? expanded : expanded.Substring(0, 900);
    }

    private double ReadToolRoutingAdjustment(string toolName) {
        lock (_toolRoutingStatsLock) {
            if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                return 0d;
            }

            var score = 0d;
            if (stats.Successes > 0) {
                score += Math.Min(2.4d, stats.Successes * 0.2d);
            }
            if (stats.Failures > 0) {
                score -= Math.Min(2.4d, stats.Failures * 0.28d);
            }
            if (stats.LastSuccessUtcTicks > 0) {
                var sinceSuccess = DateTime.UtcNow - new DateTime(stats.LastSuccessUtcTicks, DateTimeKind.Utc);
                if (sinceSuccess <= TimeSpan.FromMinutes(20)) {
                    score += 0.35d;
                }
            }

            return score;
        }
    }

    private void UpdateToolRoutingStats(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        if (calls.Count == 0 || outputs.Count == 0) {
            return;
        }

        var nameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            if (string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Name)) {
                continue;
            }

            nameByCallId[call.CallId.Trim()] = call.Name.Trim();
        }

        if (nameByCallId.Count == 0) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingStatsLock) {
            foreach (var output in outputs) {
                var normalizedOutputCallId = (output.CallId ?? string.Empty).Trim();
                if (normalizedOutputCallId.Length == 0 || !nameByCallId.TryGetValue(normalizedOutputCallId, out var toolName)) {
                    continue;
                }

                if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                    stats = new ToolRoutingStats();
                    _toolRoutingStats[toolName] = stats;
                }

                stats.Invocations++;
                stats.LastUsedUtcTicks = nowTicks;
                var success = output.Ok != false
                              && string.IsNullOrWhiteSpace(output.ErrorCode)
                              && string.IsNullOrWhiteSpace(output.Error);
                if (success) {
                    stats.Successes++;
                    stats.LastSuccessUtcTicks = nowTicks;
                } else {
                    stats.Failures++;
                }
            }
            TrimToolRoutingStatsNoLock();
        }
    }

    private void TrimWeightedRoutingContexts() {
        if (Monitor.IsEntered(_toolRoutingContextLock)) {
            TrimWeightedRoutingContextsNoLock();
            return;
        }

        lock (_toolRoutingContextLock) {
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private void TrimWeightedRoutingContextsNoLock() {
        // Weighted-tool-subset context and user-intent context share the same key space (threadId),
        // so trim all when either grows beyond its cap.
        var weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        var intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
        var pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
        var removeCount = Math.Max(weightedRemoveCount, Math.Max(intentRemoveCount, pendingRemoveCount));
        if (removeCount <= 0) {
            return;
        }

        // Defensive: if tick/value maps drift (missing/zero ticks), drop incomplete entries so they don't bias eviction.
        var removedInvalid = false;
        foreach (var threadId in _lastWeightedToolNamesByThreadId.Keys.ToArray()) {
            if (!_lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _lastWeightedToolNamesByThreadId.Remove(threadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _lastUserIntentByThreadId.Keys.ToArray()) {
            if (!_lastUserIntentSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _lastUserIntentByThreadId.Remove(threadId);
                _lastUserIntentSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _pendingActionsByThreadId.Keys.ToArray()) {
            if (!_pendingActionsSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _pendingActionsByThreadId.Remove(threadId);
                _pendingActionsSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        if (removedInvalid) {
            weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
            intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
            pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
            removeCount = Math.Max(weightedRemoveCount, Math.Max(intentRemoveCount, pendingRemoveCount));
            if (removeCount <= 0) {
                return;
            }
        }

        var seenThreadIds = new HashSet<string>(_lastWeightedToolNamesByThreadId.Keys, StringComparer.Ordinal);
        foreach (var threadId in _lastUserIntentByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _pendingActionsByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }

        var threadIdsToRemove = seenThreadIds
            .Select(threadId => {
                var ticks = 0L;
                if (_lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var weightedTicks) && weightedTicks > ticks) {
                    ticks = weightedTicks;
                }
                if (_lastUserIntentSeenUtcTicks.TryGetValue(threadId, out var intentTicks) && intentTicks > ticks) {
                    ticks = intentTicks;
                }
                if (_pendingActionsSeenUtcTicks.TryGetValue(threadId, out var actionTicks) && actionTicks > ticks) {
                    ticks = actionTicks;
                }
                return (ThreadId: threadId, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ThreadId)
            .ToArray();

        foreach (var threadId in threadIdsToRemove) {
            _lastWeightedToolNamesByThreadId.Remove(threadId);
            _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
            _lastUserIntentByThreadId.Remove(threadId);
            _lastUserIntentSeenUtcTicks.Remove(threadId);
            _pendingActionsByThreadId.Remove(threadId);
            _pendingActionsSeenUtcTicks.Remove(threadId);
        }
    }

    private void TrimToolRoutingStatsNoLock() {
        var removeCount = _toolRoutingStats.Count - MaxTrackedToolRoutingStats;
        if (removeCount <= 0) {
            return;
        }

        var toolNamesToRemove = _toolRoutingStats
            .Select(pair => {
                var stats = pair.Value;
                var ticks = stats.LastUsedUtcTicks > 0
                    ? stats.LastUsedUtcTicks
                    : (stats.LastSuccessUtcTicks > 0 ? stats.LastSuccessUtcTicks : long.MinValue);
                return (ToolName: pair.Key, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ToolName, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ToolName)
            .ToArray();

        foreach (var toolName in toolNamesToRemove) {
            _toolRoutingStats.Remove(toolName);
        }
    }

    internal void SetToolRoutingStatsForTesting(IReadOnlyDictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> statsByToolName) {
        ArgumentNullException.ThrowIfNull(statsByToolName);

        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
            foreach (var pair in statsByToolName) {
                var name = (pair.Key ?? string.Empty).Trim();
                if (name.Length == 0) {
                    continue;
                }

                _toolRoutingStats[name] = new ToolRoutingStats {
                    LastUsedUtcTicks = pair.Value.LastUsedUtcTicks,
                    LastSuccessUtcTicks = pair.Value.LastSuccessUtcTicks
                };
            }
        }
    }

    internal void SetWeightedRoutingContextsForTesting(IReadOnlyDictionary<string, string[]> namesByThreadId, IReadOnlyDictionary<string, long> seenTicksByThreadId) {
        ArgumentNullException.ThrowIfNull(namesByThreadId);
        ArgumentNullException.ThrowIfNull(seenTicksByThreadId);

        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();

            foreach (var pair in namesByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0) {
                    continue;
                }

                var names = pair.Value ?? Array.Empty<string>();
                var namesClone = new string[names.Length];
                if (names.Length > 0) {
                    Array.Copy(names, namesClone, names.Length);
                }

                _lastWeightedToolNamesByThreadId[threadId] = namesClone;
            }

            foreach (var pair in seenTicksByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0 || !_lastWeightedToolNamesByThreadId.ContainsKey(threadId)) {
                    continue;
                }

                _lastWeightedToolSubsetSeenUtcTicks[threadId] = pair.Value;
            }
        }
    }

    internal IReadOnlyCollection<string> GetTrackedToolRoutingStatNamesForTesting() {
        lock (_toolRoutingStatsLock) {
            return _toolRoutingStats.Keys.ToArray();
        }
    }

    internal IReadOnlyCollection<string> GetTrackedWeightedRoutingContextThreadIdsForTesting() {
        lock (_toolRoutingContextLock) {
            return _lastWeightedToolNamesByThreadId.Keys.ToArray();
        }
    }

    internal void TrimToolRoutingStatsForTesting() {
        lock (_toolRoutingStatsLock) {
            TrimToolRoutingStatsNoLock();
        }
    }

    internal void TrimWeightedRoutingContextsForTesting() {
        lock (_toolRoutingContextLock) {
            TrimWeightedRoutingContexts();
        }
    }

    private static int ResolveMaxCandidateToolsLimit(int? requestedLimit, int totalToolCount) {
        var candidate = requestedLimit.GetValueOrDefault(0);
        if (candidate <= 0) {
            candidate = Math.Clamp((int)Math.Ceiling(totalToolCount * 0.45d), 10, 28);
        }

        return Math.Clamp(candidate, 4, Math.Max(4, totalToolCount));
    }

    private static string ExtractPrimaryUserRequest(string requestText) {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        var match = UserRequestSectionRegex.Match(text);
        if (match.Success && match.Groups.Count > 1) {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        return NormalizeRoutingUserText(text);
    }

    private static string ExtractIntentUserText(string requestText) {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        var match = UserRequestSectionRegex.Match(text);
        if (match.Success && match.Groups.Count > 1) {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value)) {
                text = value.Trim();
            }
        }

        // Keep intent relatively faithful while still removing markdown delimiters.
        var withoutInlineDelimiters = StripInlineCode(text);
        var strippedFences = StripCodeFences(withoutInlineDelimiters);
        var collapsed = CollapseWhitespace(strippedFences);
        if (collapsed.Length > 0) {
            return collapsed;
        }

        // If stripping fences wiped out everything (e.g., an all-code message), keep a compact version of the
        // original content but remove fence markers so follow-ups can still anchor on *some* context.
        return CollapseWhitespace(withoutInlineDelimiters.Replace("```", " ", StringComparison.Ordinal));
    }

    private static string NormalizeRoutingUserText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Strip code fences and inline code so routing focuses on intent, not pasted snippets.
        normalized = StripCodeFences(normalized);
        normalized = StripInlineCode(normalized);
        normalized = CollapseWhitespace(normalized);
        // Never fall back to the original text here: it may contain the very content we intentionally stripped.
        return normalized;
    }

    private static string StripCodeFences(string text) {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOf("```", StringComparison.Ordinal) < 0) {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var idx = 0;
        while (idx < text.Length) {
            var start = text.IndexOf("```", idx, StringComparison.Ordinal);
            if (start < 0) {
                sb.Append(text, idx, text.Length - idx);
                break;
            }

            sb.Append(text, idx, start - idx);

            var end = text.IndexOf("```", start + 3, StringComparison.Ordinal);
            if (end < 0) {
                var tail = ExtractUnclosedFenceTail(text, fenceStartIndex: start + 3);
                if (!string.IsNullOrWhiteSpace(tail)) {
                    if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) {
                        sb.Append(' ');
                    }
                    sb.Append(tail);
                }
                break;
            }

            idx = end + 3;
        }

        return sb.ToString();
    }

    private static string ExtractUnclosedFenceTail(string text, int fenceStartIndex) {
        if (string.IsNullOrWhiteSpace(text) || fenceStartIndex < 0 || fenceStartIndex >= text.Length) {
            return string.Empty;
        }

        // If a user forgets the closing fence, they often keep typing a short instruction after the code.
        // Try to salvage the last non-empty line if it looks like natural language rather than a command.
        var idx = text.Length;
        while (idx > fenceStartIndex) {
            var lineStart = text.LastIndexOf('\n', idx - 1);
            if (lineStart < fenceStartIndex) {
                lineStart = fenceStartIndex - 1;
            }

            var rawLine = text.Substring(lineStart + 1, idx - (lineStart + 1)).Trim();
            if (rawLine.Length == 0) {
                idx = lineStart;
                continue;
            }

            var candidate = CollapseWhitespace(StripInlineCode(rawLine));
            if (candidate.Length == 0 || candidate.Length > 96) {
                return string.Empty;
            }
            if (LooksLikeCodeTail(candidate)) {
                return string.Empty;
            }
            return candidate;
        }

        return string.Empty;
    }

    private static bool LooksLikeCodeTail(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var hasUpper = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (ch is '{' or '}' or ';' or '(' or ')' or '[' or ']' or '$' or '=' or '|' or '<' or '>') {
                return true;
            }
            if (ch is '\r' or '\n' or '\t') {
                return true;
            }
            if (char.IsUpper(ch)) {
                hasUpper = true;
            }
        }

        // Common for cmdlets/functions: `Get-Thing` (hyphen + upper). Allow "forest-wide" (no upper).
        return hasUpper && text.Contains('-', StringComparison.Ordinal);
    }

    private static string StripInlineCode(string text) {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOf('`', StringComparison.Ordinal) < 0) {
            return text;
        }

        // Inline code often wraps important tokens (paths/hostnames). Keep the content and drop delimiters.
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (ch == '`') {
                // Replace with whitespace so we don't accidentally concatenate tokens.
                sb.Append(' ');
                continue;
            }
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string CollapseWhitespace(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var prevSpace = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsWhiteSpace(ch)) {
                if (!prevSpace) {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            prevSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

}
