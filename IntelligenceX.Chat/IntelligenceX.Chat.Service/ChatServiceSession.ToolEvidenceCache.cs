using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string CachedToolEvidenceMarker = "ix:cached-tool-evidence:v1";

    private readonly record struct ThreadToolEvidenceEntry(
        string ToolName,
        string ArgumentsJson,
        string Output,
        string SummaryMarkdown,
        long SeenUtcTicks);

    private void RememberThreadToolEvidence(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || toolCalls.Count == 0 || toolOutputs.Count == 0) {
            return;
        }

        var callContractById = new Dictionary<string, (string ToolName, string ArgumentsJson, bool Mutating)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var call = toolCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0 || toolName.Length == 0) {
                continue;
            }

            var argsJson = NormalizeArgumentsJsonForReplayContract(call.ArgumentsJson);
            var isMutating = mutatingToolHintsByName.TryGetValue(toolName, out var mutating) && mutating;
            callContractById[callId] = (toolName, argsJson, isMutating);
        }

        if (callContractById.Count == 0) {
            return;
        }

        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature)) {
                bySignature = new Dictionary<string, ThreadToolEvidenceEntry>(StringComparer.Ordinal);
                _threadToolEvidenceByThreadId[normalizedThreadId] = bySignature;
            }

            var nowTicks = DateTime.UtcNow.Ticks;
            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                var callId = (output.CallId ?? string.Empty).Trim();
                if (callId.Length == 0 || !callContractById.TryGetValue(callId, out var contract)) {
                    continue;
                }

                if (contract.Mutating) {
                    continue;
                }

                var success = output.Ok != false
                              && string.IsNullOrWhiteSpace(output.ErrorCode)
                              && string.IsNullOrWhiteSpace(output.Error);
                if (!success) {
                    continue;
                }

                var payload = CompactToolEvidencePayload((output.Output ?? string.Empty).Trim());
                var summary = CompactToolEvidenceSummary((output.SummaryMarkdown ?? string.Empty).Trim());
                if (payload.Length == 0 && summary.Length == 0) {
                    continue;
                }

                var signature = BuildToolEvidenceSignature(contract.ToolName, contract.ArgumentsJson);
                if (signature.Length == 0) {
                    continue;
                }

                bySignature[signature] = new ThreadToolEvidenceEntry(
                    ToolName: contract.ToolName,
                    ArgumentsJson: contract.ArgumentsJson,
                    Output: payload,
                    SummaryMarkdown: summary,
                    SeenUtcTicks: nowTicks);
            }

            TrimThreadToolEvidenceEntriesNoLock(bySignature);
            TrimThreadToolEvidenceContextsNoLock(nowTicks);
        }
    }

    private bool TryBuildToolEvidenceFallbackText(string threadId, string userRequest, out string text) {
        text = string.Empty;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var requestTokens = TokenizeRoutingTokens(userRequest, maxTokens: 10);
        ThreadToolEvidenceEntry[] selected;
        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature) || bySignature.Count == 0) {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var expiredKeys = new List<string>();
            var candidates = new List<(ThreadToolEvidenceEntry Entry, double Score)>(bySignature.Count);
            foreach (var pair in bySignature) {
                var entry = pair.Value;
                if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc) || nowUtc - seenUtc > ThreadToolEvidenceContextMaxAge) {
                    expiredKeys.Add(pair.Key);
                    continue;
                }

                var score = entry.SeenUtcTicks / (double)TimeSpan.TicksPerSecond;
                if (requestTokens.Length > 0) {
                    score += ComputeToolEvidenceTokenScore(requestTokens, entry);
                }

                candidates.Add((entry, score));
            }

            for (var i = 0; i < expiredKeys.Count; i++) {
                bySignature.Remove(expiredKeys[i]);
            }

            if (bySignature.Count == 0) {
                _threadToolEvidenceByThreadId.Remove(normalizedThreadId);
            }

            if (candidates.Count == 0) {
                return false;
            }

            candidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));
            var takeCount = Math.Min(3, candidates.Count);
            selected = new ThreadToolEvidenceEntry[takeCount];
            for (var i = 0; i < takeCount; i++) {
                selected[i] = candidates[i].Entry;
            }

            TrimThreadToolEvidenceContextsNoLock(nowUtc.Ticks);
        }

        var sb = new StringBuilder(1024);
        sb.AppendLine("[Cached evidence fallback]");
        sb.AppendLine(CachedToolEvidenceMarker);
        sb.AppendLine("Live tool execution did not complete in this turn, so I reused recent read-only evidence from this session.");
        sb.AppendLine();
        sb.AppendLine("Recent evidence:");
        for (var i = 0; i < selected.Length; i++) {
            var entry = selected[i];
            var snippet = entry.SummaryMarkdown.Length > 0
                ? entry.SummaryMarkdown
                : BuildToolEvidenceSnippet(entry.Output);
            sb.Append("- ").Append(entry.ToolName).Append(": ").AppendLine(snippet);
        }

        sb.AppendLine();
        sb.Append("If you want a live refresh, ask me to rerun these checks now.");
        text = sb.ToString().Trim();
        return text.Length > 0;
    }

    private static string BuildToolEvidenceSnippet(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "(no summary available)";
        }

        var lineEnd = normalized.IndexOfAny(new[] { '\r', '\n' });
        if (lineEnd >= 0) {
            normalized = normalized.Substring(0, lineEnd).Trim();
        }

        if (normalized.Length > 180) {
            normalized = normalized.Substring(0, 180).TrimEnd() + "...";
        }

        return normalized.Length == 0 ? "(no summary available)" : normalized;
    }

    private static string CompactToolEvidencePayload(string output) {
        var normalized = (output ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        const int maxChars = 3200;
        if (normalized.Length <= maxChars) {
            return normalized;
        }

        return normalized.Substring(0, maxChars).TrimEnd() + "...";
    }

    private static string CompactToolEvidenceSummary(string summary) {
        var normalized = (summary ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        const int maxChars = 420;
        if (normalized.Length <= maxChars) {
            return normalized;
        }

        return normalized.Substring(0, maxChars).TrimEnd() + "...";
    }

    private static string BuildToolEvidenceSignature(string toolName, string argumentsJson) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return string.Empty;
        }

        var normalizedArgs = NormalizeArgumentsJsonForReplayContract(argumentsJson);
        return normalizedToolName.ToLowerInvariant() + "|" + normalizedArgs;
    }

    private static double ComputeToolEvidenceTokenScore(string[] requestTokens, ThreadToolEvidenceEntry entry) {
        if (requestTokens.Length == 0) {
            return 0d;
        }

        var searchText = (entry.ToolName + " " + entry.SummaryMarkdown + " " + entry.Output).Trim();
        if (searchText.Length == 0) {
            return 0d;
        }

        var tokenHits = 0;
        for (var i = 0; i < requestTokens.Length; i++) {
            var token = requestTokens[i];
            if (token.Length == 0) {
                continue;
            }

            if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                tokenHits++;
            }
        }

        return tokenHits * 9d;
    }

    private void TrimThreadToolEvidenceEntriesNoLock(Dictionary<string, ThreadToolEvidenceEntry> bySignature) {
        if (bySignature.Count <= MaxToolEvidenceEntriesPerThread) {
            return;
        }

        var removeCount = bySignature.Count - MaxToolEvidenceEntriesPerThread;
        var signaturesToRemove = new List<string>(removeCount);
        foreach (var pair in bySignature) {
            signaturesToRemove.Add(pair.Key);
        }

        signaturesToRemove.Sort((left, right) => {
            var leftTicks = bySignature.TryGetValue(left, out var leftEntry) ? leftEntry.SeenUtcTicks : long.MinValue;
            var rightTicks = bySignature.TryGetValue(right, out var rightEntry) ? rightEntry.SeenUtcTicks : long.MinValue;
            var ticksCompare = leftTicks.CompareTo(rightTicks);
            if (ticksCompare != 0) {
                return ticksCompare;
            }

            return StringComparer.Ordinal.Compare(left, right);
        });

        for (var i = 0; i < removeCount && i < signaturesToRemove.Count; i++) {
            bySignature.Remove(signaturesToRemove[i]);
        }
    }

    private void TrimThreadToolEvidenceContextsNoLock(long nowTicks) {
        var nowUtc = new DateTime(nowTicks, DateTimeKind.Utc);
        var emptyThreadIds = new List<string>();
        foreach (var pair in _threadToolEvidenceByThreadId) {
            var bySignature = pair.Value;
            var expiredKeys = new List<string>();
            foreach (var signaturePair in bySignature) {
                var entry = signaturePair.Value;
                if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)
                    || nowUtc - seenUtc > ThreadToolEvidenceContextMaxAge) {
                    expiredKeys.Add(signaturePair.Key);
                }
            }

            for (var i = 0; i < expiredKeys.Count; i++) {
                bySignature.Remove(expiredKeys[i]);
            }

            if (bySignature.Count == 0) {
                emptyThreadIds.Add(pair.Key);
                continue;
            }

            TrimThreadToolEvidenceEntriesNoLock(bySignature);
        }

        for (var i = 0; i < emptyThreadIds.Count; i++) {
            _threadToolEvidenceByThreadId.Remove(emptyThreadIds[i]);
        }

        var removeContexts = _threadToolEvidenceByThreadId.Count - MaxTrackedThreadToolEvidenceContexts;
        if (removeContexts <= 0) {
            return;
        }

        var threadOrder = new List<(string ThreadId, long LatestSeenTicks)>(_threadToolEvidenceByThreadId.Count);
        foreach (var pair in _threadToolEvidenceByThreadId) {
            var latestTicks = long.MinValue;
            foreach (var entry in pair.Value.Values) {
                if (entry.SeenUtcTicks > latestTicks) {
                    latestTicks = entry.SeenUtcTicks;
                }
            }

            threadOrder.Add((pair.Key, latestTicks));
        }

        threadOrder.Sort(static (left, right) => {
            var ticksCompare = left.LatestSeenTicks.CompareTo(right.LatestSeenTicks);
            if (ticksCompare != 0) {
                return ticksCompare;
            }

            return StringComparer.Ordinal.Compare(left.ThreadId, right.ThreadId);
        });

        for (var i = 0; i < removeContexts && i < threadOrder.Count; i++) {
            _threadToolEvidenceByThreadId.Remove(threadOrder[i].ThreadId);
        }
    }

    private void ClearThreadToolEvidence() {
        lock (_threadToolEvidenceLock) {
            _threadToolEvidenceByThreadId.Clear();
        }
    }

    internal bool TryBuildToolEvidenceFallbackTextForTesting(string threadId, string userRequest, out string text) {
        return TryBuildToolEvidenceFallbackText(threadId, userRequest, out text);
    }

    internal void RememberThreadToolEvidenceForTesting(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        RememberThreadToolEvidence(threadId, toolCalls, toolOutputs, mutatingToolHintsByName);
    }
}
