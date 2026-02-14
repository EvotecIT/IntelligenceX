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
            TrimWeightedRoutingContextsNoLock();
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
            TrimWeightedRoutingContextsNoLock();
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
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private string ExpandContinuationUserRequest(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return userRequest;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || !LooksLikeContinuationFollowUp(normalized)) {
            return normalized;
        }

        string? intent;
        long intentTicks;
        lock (_toolRoutingContextLock) {
            if (!_lastUserIntentByThreadId.TryGetValue(normalizedThreadId, out intent) || string.IsNullOrWhiteSpace(intent)) {
                return normalized;
            }

            intentTicks = _lastUserIntentSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (intentTicks > 0) {
            if (intentTicks > DateTime.MaxValue.Ticks) {
                // Defensive: avoid exceptions from ticks->DateTime conversion if ticks are corrupted/out of range.
                return normalized;
            }
            var age = DateTime.UtcNow - new DateTime(intentTicks, DateTimeKind.Utc);
            if (age > UserIntentContextMaxAge) {
                return normalized;
            }
        }

        lock (_toolRoutingContextLock) {
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
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

    private void TrimWeightedRoutingContextsNoLock() {
        // Weighted-tool-subset context and user-intent context share the same key space (threadId),
        // so trim both when either grows beyond its cap.
        var weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        var intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
        var removeCount = Math.Max(weightedRemoveCount, intentRemoveCount);
        if (removeCount <= 0) {
            return;
        }

        // Defensive: keep timestamp maps in sync with their value maps so missing ticks can't skew eviction ordering.
        var nowTicks = DateTime.UtcNow.Ticks;
        foreach (var threadId in _lastWeightedToolNamesByThreadId.Keys) {
            if (!_lastWeightedToolSubsetSeenUtcTicks.ContainsKey(threadId)) {
                _lastWeightedToolSubsetSeenUtcTicks[threadId] = nowTicks;
            }
        }
        foreach (var threadId in _lastUserIntentByThreadId.Keys) {
            if (!_lastUserIntentSeenUtcTicks.ContainsKey(threadId)) {
                _lastUserIntentSeenUtcTicks[threadId] = nowTicks;
            }
        }

        var seenThreadIds = new HashSet<string>(_lastWeightedToolNamesByThreadId.Keys, StringComparer.Ordinal);
        foreach (var threadId in _lastUserIntentByThreadId.Keys) {
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
            TrimWeightedRoutingContextsNoLock();
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
                break;
            }

            idx = end + 3;
        }

        return sb.ToString();
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

    private async Task<IReadOnlyList<ToolDefinition>> TrySelectToolsViaModelPlannerAsync(IntelligenceXClient client, string activeThreadId, string userRequest,
        IReadOnlyList<ToolDefinition> definitions, int limit, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(activeThreadId)
            || string.IsNullOrWhiteSpace(userRequest)
            || definitions.Count == 0
            || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        try {
            var plannerPrompt = BuildModelPlannerPrompt(userRequest, definitions, limit);
            if (plannerPrompt.Length == 0) {
                return Array.Empty<ToolDefinition>();
            }

            var plannerOptions = new ChatOptions {
                Model = _options.Model,
                Tools = null,
                ToolChoice = ToolChoice.None,
                ParallelToolCalls = false,
                Temperature = 0,
                ReasoningEffort = ReasoningEffort.Minimal,
                ReasoningSummary = ReasoningSummary.Off,
                TextVerbosity = TextVerbosity.Low,
                Instructions = """
                    You are a semantic tool router.
                    Select the most relevant tools for the user request from the provided catalog.
                    Return strict JSON only with this shape:
                    {"tool_names":["tool_a","tool_b"]}
                    Rules:
                    - Use only tool names present in the provided list.
                    - Prefer precision over recall.
                    - Return at most the requested max count.
                    - Do not add commentary or markdown.
                    """
            };

            _ = await client.StartNewThreadAsync(
                    model: plannerOptions.Model,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var turn = await client.ChatAsync(ChatInput.FromText(plannerPrompt), plannerOptions, cancellationToken).ConfigureAwait(false);
            var plannerText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
            return ParsePlannerSelectedDefinitions(plannerText, definitions, limit);
        } catch {
            return Array.Empty<ToolDefinition>();
        } finally {
            try {
                await client.UseThreadAsync(activeThreadId, cancellationToken).ConfigureAwait(false);
            } catch {
                // Best-effort restore of active conversation thread.
            }
        }
    }

    private IReadOnlyList<ToolDefinition> EnsureMinimumToolSelection(string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        IReadOnlyList<ToolDefinition> initialSelected, int limit) {
        if (allDefinitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var selected = new List<ToolDefinition>(Math.Min(limit, allDefinitions.Count));
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < initialSelected.Count && selected.Count < limit; i++) {
            var definition = initialSelected[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name) || !selectedNames.Add(definition.Name)) {
                continue;
            }
            selected.Add(definition);
        }

        var minSelection = Math.Min(allDefinitions.Count, Math.Max(8, Math.Min(limit, 12)));
        if (selected.Count >= minSelection) {
            return selected;
        }

        var rankedFallback = new List<(ToolDefinition Definition, double Score)>(allDefinitions.Count);
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name) || selectedNames.Contains(definition.Name)) {
                continue;
            }

            var score = 0d;
            if (!string.IsNullOrWhiteSpace(userRequest)
                && userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0) {
                score += 6d;
            }

            score += ReadToolRoutingAdjustment(definition.Name);
            rankedFallback.Add((definition, score));
        }

        rankedFallback.Sort(static (left, right) => {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(left.Definition.Name, right.Definition.Name);
        });

        for (var i = 0; i < rankedFallback.Count && selected.Count < minSelection; i++) {
            var definition = rankedFallback[i].Definition;
            if (selectedNames.Add(definition.Name)) {
                selected.Add(definition);
            }
        }

        return selected.Count == 0 ? allDefinitions : selected;
    }

    private static List<ToolRoutingInsight> BuildModelRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs, int plannedCount) {
        var list = new List<ToolRoutingInsight>(Math.Min(12, selectedDefs.Count));
        if (selectedDefs.Count == 0) {
            return list;
        }

        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = (selectedDefs[i].Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            var fromPlanner = i < plannedCount;
            var confidence = i < 3 ? "high" : i < 8 ? "medium" : "low";
            var reason = fromPlanner
                ? "semantic planner selection"
                : "semantic planner backfill with routing history";
            var score = Math.Max(0.2d, 1d - (i * 0.06d));
            list.Add(new ToolRoutingInsight(
                ToolName: name,
                Confidence: confidence,
                Score: Math.Round(score, 3),
                Reason: reason));
        }

        return list;
    }

    private static string BuildModelPlannerPrompt(string userRequest, IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (definitions.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(capacity: Math.Min(64_000, 4000 + (definitions.Count * 120)));
        sb.AppendLine("Select tools for the following user request.");
        sb.AppendLine("User request:");
        sb.AppendLine(userRequest.Trim());
        sb.AppendLine();
        sb.AppendLine($"Return at most {Math.Max(1, limit)} tool names.");
        sb.AppendLine("Available tools:");

        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var name = definition.Name.Trim();
            var description = (definition.Description ?? string.Empty).Trim();
            if (description.Length > 220) {
                description = description[..220].TrimEnd();
            }
            sb.Append(i + 1).Append(". ").Append(name);
            if (description.Length > 0) {
                sb.Append(" :: ").Append(description);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IReadOnlyList<ToolDefinition> ParsePlannerSelectedDefinitions(string plannerText, IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (string.IsNullOrWhiteSpace(plannerText) || definitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var byName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var name = definition.Name.Trim();
            if (!byName.ContainsKey(name)) {
                byName.Add(name, definition);
            }
        }

        var selected = new List<ToolDefinition>(Math.Min(limit, byName.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = ExtractPlannerToolNames(plannerText);
        for (var i = 0; i < names.Count && selected.Count < limit; i++) {
            var requestedName = names[i];
            if (string.IsNullOrWhiteSpace(requestedName)) {
                continue;
            }

            var normalized = requestedName.Trim();
            if (!byName.TryGetValue(normalized, out var definition) || !seen.Add(definition.Name)) {
                continue;
            }

            selected.Add(definition);
        }

        return selected;
    }

    private static List<string> ExtractPlannerToolNames(string plannerText) {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = BuildPlannerJsonCandidates(plannerText);
        for (var i = 0; i < candidates.Count; i++) {
            if (!TryExtractPlannerNamesFromJson(candidates[i], names, seen)) {
                continue;
            }

            if (names.Count > 0) {
                return names;
            }
        }

        // Fallback for non-JSON planner replies: collect tool-like identifiers.
        var matches = Regex.Matches(plannerText, @"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < matches.Count; i++) {
            var value = (matches[i].Value ?? string.Empty).Trim();
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }
            names.Add(value);
        }

        return names;
    }

    private static List<string> BuildPlannerJsonCandidates(string plannerText) {
        var list = new List<string>();
        var normalized = (plannerText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return list;
        }

        list.Add(normalized);

        var fencedMatches = Regex.Matches(
            normalized,
            "```(?:json)?\\s*(?<json>[\\s\\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < fencedMatches.Count; i++) {
            var captured = fencedMatches[i].Groups["json"].Value.Trim();
            if (captured.Length > 0) {
                list.Add(captured);
            }
        }

        AppendJsonEnvelopeCandidate(normalized, '{', '}', list);
        AppendJsonEnvelopeCandidate(normalized, '[', ']', list);
        return list;
    }

    private static void AppendJsonEnvelopeCandidate(string text, char startChar, char endChar, ICollection<string> target) {
        var start = text.IndexOf(startChar);
        var end = text.LastIndexOf(endChar);
        if (start < 0 || end <= start) {
            return;
        }

        var candidate = text.Substring(start, (end - start) + 1).Trim();
        if (candidate.Length > 1) {
            target.Add(candidate);
        }
    }

    private static bool TryExtractPlannerNamesFromJson(string candidate, ICollection<string> names, ISet<string> seen) {
        if (string.IsNullOrWhiteSpace(candidate)) {
            return false;
        }

        JsonValue? parsed;
        try {
            parsed = JsonLite.Parse(candidate);
        } catch {
            return false;
        }

        if (parsed is null) {
            return false;
        }

        var extracted = 0;
        var rootObj = parsed.AsObject();
        if (rootObj is not null) {
            extracted += AppendPlannerNamesFromObject(rootObj, names, seen);
        } else {
            var rootArray = parsed.AsArray();
            if (rootArray is not null) {
                extracted += AppendPlannerNamesFromArray(rootArray, names, seen);
            }
        }

        return extracted > 0;
    }

    private static int AppendPlannerNamesFromObject(JsonObject obj, ICollection<string> names, ISet<string> seen) {
        var added = 0;
        added += AppendPlannerNamesFromArray(obj.GetArray("tool_names"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("tools"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("selected"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("recommended"), names, seen);

        var resultObj = obj.GetObject("result");
        if (resultObj is not null) {
            added += AppendPlannerNamesFromObject(resultObj, names, seen);
        }

        return added;
    }

    private static int AppendPlannerNamesFromArray(JsonArray? array, ICollection<string> names, ISet<string> seen) {
        if (array is null || array.Count == 0) {
            return 0;
        }

        var added = 0;
        for (var i = 0; i < array.Count; i++) {
            var item = array[i];
            var asName = item.AsString();
            if (TryAddPlannerName(asName, names, seen)) {
                added++;
                continue;
            }

            var asObj = item.AsObject();
            if (asObj is null) {
                continue;
            }

            if (TryAddPlannerName(asObj.GetString("name"), names, seen)) {
                added++;
            }
            if (TryAddPlannerName(asObj.GetString("tool"), names, seen)) {
                added++;
            }
            if (TryAddPlannerName(asObj.GetString("tool_name"), names, seen)) {
                added++;
            }
        }

        return added;
    }

    private static bool TryAddPlannerName(string? value, ICollection<string> names, ISet<string> seen) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || !seen.Add(normalized)) {
            return false;
        }

        names.Add(normalized);
        return true;
    }

    private static bool ShouldSkipWeightedRouting(string userRequest) {
        return string.IsNullOrWhiteSpace(userRequest);
    }

    private static bool LooksLikeContinuationFollowUp(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.Contains('\n', StringComparison.Ordinal) || normalized.Length > 96) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= 6 && normalized.Length <= 64) {
            return true;
        }

        return tokenCount <= 8
               && normalized.Length <= 96
               && normalized.Contains('?', StringComparison.Ordinal);
    }

    private sealed class ToolRoutingStats {
        public int Invocations { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public long LastUsedUtcTicks { get; set; }
        public long LastSuccessUtcTicks { get; set; }
    }

    private readonly record struct ToolScore(
        ToolDefinition Definition,
        double Score,
        bool DirectNameMatch,
        int TokenHits,
        double Adjustment);

    private readonly record struct ToolRoutingInsight(
        string ToolName,
        string Confidence,
        double Score,
        string Reason);

    private readonly record struct ToolRetryProfile(
        int MaxAttempts,
        int DelayBaseMs,
        bool RetryOnTimeout,
        bool RetryOnTransport);

}
