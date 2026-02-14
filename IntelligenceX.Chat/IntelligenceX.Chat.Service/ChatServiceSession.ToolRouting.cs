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
        var removeCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        if (removeCount <= 0) {
            return;
        }

        var threadIdsToRemove = _lastWeightedToolNamesByThreadId.Keys
            .Select(threadId => {
                var ticks = _lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var value) && value > 0
                    ? value
                    : long.MinValue;
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

        return text;
    }

    private static bool ShouldSkipWeightedRouting(string userRequest) {
        if (string.IsNullOrWhiteSpace(userRequest)) {
            return true;
        }

        var normalized = userRequest.Trim().ToLowerInvariant();
        return normalized.Contains("what tools", StringComparison.Ordinal)
               || normalized.Contains("list tools", StringComparison.Ordinal)
               || normalized.Contains("available tools", StringComparison.Ordinal)
               || normalized.Contains("tool catalog", StringComparison.Ordinal)
               || normalized.Contains("which tool", StringComparison.Ordinal)
               || normalized.Contains("all tools", StringComparison.Ordinal)
               || normalized.Contains("anything you can", StringComparison.Ordinal);
    }

    private static bool LooksLikeContinuationFollowUp(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (ContinuationFollowUpRegex.IsMatch(normalized)) {
            return true;
        }

        return normalized.Equals("continue", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("same", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("again", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> TokenizeForToolRouting(string userRequest) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = Regex.Split((userRequest ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9_]+");
        for (var i = 0; i < parts.Length; i++) {
            var token = (parts[i] ?? string.Empty).Trim();
            if (token.Length < 3) {
                continue;
            }

            if (token is "the" or "and" or "with" or "from" or "that" or "this" or "for" or "you" or "your" or "have" or "show"
                or "give" or "list" or "check" or "please" or "about" or "into" or "just" or "today") {
                continue;
            }

            if (seen.Add(token)) {
                result.Add(token);
            }
        }

        return result;
    }

    private static string BuildToolSearchText(ToolDefinition definition) {
        var tags = definition.Tags.Count == 0 ? string.Empty : string.Join(' ', definition.Tags);
        return (definition.Name + " " + (definition.Description ?? string.Empty) + " " + tags).ToLowerInvariant();
    }

    private static bool IsPackMentioned(string userRequest, string packId) {
        if (string.IsNullOrWhiteSpace(userRequest) || string.IsNullOrWhiteSpace(packId)) {
            return false;
        }

        var normalized = userRequest.ToLowerInvariant();
        return packId switch {
            "ad" => normalized.Contains("active directory", StringComparison.Ordinal)
                    || normalized.Contains("domain", StringComparison.Ordinal)
                    || normalized.Contains("replication", StringComparison.Ordinal)
                    || normalized.Contains("ou ", StringComparison.Ordinal),
            "eventlog" => normalized.Contains("event log", StringComparison.Ordinal)
                          || normalized.Contains("evtx", StringComparison.Ordinal)
                          || normalized.Contains("lockout", StringComparison.Ordinal),
            "system" => normalized.Contains("system", StringComparison.Ordinal)
                        || normalized.Contains("host", StringComparison.Ordinal)
                        || normalized.Contains("computer", StringComparison.Ordinal),
            "fs" => normalized.Contains("file", StringComparison.Ordinal)
                    || normalized.Contains("folder", StringComparison.Ordinal)
                    || normalized.Contains("path", StringComparison.Ordinal),
            "powershell" => normalized.Contains("powershell", StringComparison.Ordinal)
                            || normalized.Contains("script", StringComparison.Ordinal),
            "email" => normalized.Contains("email", StringComparison.Ordinal)
                       || normalized.Contains("mail", StringComparison.Ordinal),
            "testimox" => normalized.Contains("testimox", StringComparison.Ordinal)
                          || normalized.Contains("rule", StringComparison.Ordinal),
            _ => false
        };
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
        int TokenHits,
        bool DirectNameMatch,
        bool PackMatch,
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
