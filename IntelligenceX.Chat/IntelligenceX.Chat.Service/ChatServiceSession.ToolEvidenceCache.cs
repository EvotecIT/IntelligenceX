using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.Abstractions.Protocol;
using JsonValueKind = System.Text.Json.JsonValueKind;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string CachedToolEvidenceMarker = "ix:cached-tool-evidence:v1";
    private static readonly Regex ExplicitRequestedToolNameRegex = new(
        @"\b[a-z][a-z0-9]*(?:(?:\\?[_-])[a-z0-9]+)+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly record struct ThreadToolEvidenceEntry(
        string ToolName,
        string ArgumentsJson,
        string Output,
        string SummaryMarkdown,
        long SeenUtcTicks);

    private readonly record struct ToolEvidenceCandidate(
        ThreadToolEvidenceEntry Entry,
        double Score,
        int TokenHits,
        int StrongTokenHits);

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

        ThreadToolEvidenceEntry[]? snapshotEntries = null;
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
            if (_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var latestBySignature) && latestBySignature.Count > 0) {
                snapshotEntries = SnapshotThreadToolEvidenceEntries(latestBySignature);
            }
        }

        if (snapshotEntries is not null) {
            PersistThreadToolEvidenceSnapshot(normalizedThreadId, snapshotEntries);
        } else {
            PersistThreadToolEvidenceSnapshot(normalizedThreadId, Array.Empty<ThreadToolEvidenceEntry>());
        }
    }

    private bool TryBuildToolEvidenceFallbackText(string threadId, string userRequest, out string text) {
        text = string.Empty;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        if (ShouldBypassCachedToolEvidenceFallback(normalizedThreadId, userRequest)) {
            return false;
        }

        TryHydrateThreadToolEvidenceFromSnapshot(normalizedThreadId);

        var requestTokens = TokenizeRoutingTokens(userRequest, maxTokens: 10);
        var requestedToolNames = ExtractExplicitRequestedToolNames(userRequest);
        var requestedFamily = ResolveRequestedToolEvidenceFamily(normalizedThreadId, userRequest);
        var hasRequestedFamily = requestedFamily.Length > 0;
        var allowFamilyOnlyFallbackWithoutTokenMatch = hasRequestedFamily
                                                       && requestedToolNames.Length == 0
                                                       && requestTokens.Length <= 2
                                                       && !ContainsQuestionSignal(userRequest)
                                                       && !LooksLikeLiveRefreshFollowUp(userRequest)
                                                       && !LooksLikeExplicitLiveRefreshToolRequest(userRequest);
        ThreadToolEvidenceEntry[] selected;
        ThreadToolEvidenceEntry[]? updatedSnapshotEntries = null;
        var shouldClearSnapshot = false;
        var hasCandidates = true;
        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature) || bySignature.Count == 0) {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var expiredKeys = new List<string>();
            var candidates = new List<ToolEvidenceCandidate>(bySignature.Count);
            foreach (var pair in bySignature) {
                var entry = pair.Value;
                if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc) || nowUtc - seenUtc > ThreadToolEvidenceContextMaxAge) {
                    expiredKeys.Add(pair.Key);
                    continue;
                }

                var entryFamily = ResolveDomainIntentFamily(entry.ToolName);
                if (hasRequestedFamily
                    && entryFamily.Length > 0
                    && !string.Equals(entryFamily, requestedFamily, StringComparison.Ordinal)) {
                    continue;
                }

                var (tokenHits, strongTokenHits) = CountToolEvidenceTokenHits(requestTokens, entry);
                var score = entry.SeenUtcTicks / (double)TimeSpan.TicksPerSecond;
                score += ComputeToolEvidenceTokenScore(tokenHits, strongTokenHits);
                candidates.Add(new ToolEvidenceCandidate(entry, score, tokenHits, strongTokenHits));
            }

            for (var i = 0; i < expiredKeys.Count; i++) {
                bySignature.Remove(expiredKeys[i]);
            }

            if (bySignature.Count == 0) {
                _threadToolEvidenceByThreadId.Remove(normalizedThreadId);
                shouldClearSnapshot = true;
            }

            if (candidates.Count == 0) {
                hasCandidates = false;
                selected = Array.Empty<ThreadToolEvidenceEntry>();
            } else {
                if (requestTokens.Length > 0) {
                    var hasStrongTokenMatchedCandidate = false;
                    var hasTokenMatchedCandidate = false;
                    for (var i = 0; i < candidates.Count; i++) {
                        if (candidates[i].StrongTokenHits > 0) {
                            hasStrongTokenMatchedCandidate = true;
                            break;
                        }
                        if (candidates[i].TokenHits > 0) {
                            hasTokenMatchedCandidate = true;
                        }
                    }

                    if (hasStrongTokenMatchedCandidate) {
                        candidates.RemoveAll(static candidate => candidate.StrongTokenHits <= 0);
                    } else if (hasTokenMatchedCandidate) {
                        candidates.RemoveAll(static candidate => candidate.TokenHits <= 0);
                    } else if (!allowFamilyOnlyFallbackWithoutTokenMatch) {
                        // Same-family evidence is still too broad when none of the request's
                        // routing tokens match the cached payload. In that case prefer no
                        // fallback over replaying a nearby-but-unrelated AD artifact.
                        hasCandidates = false;
                    }
                }
            }

            if (hasCandidates
                && candidates.Count > 0
                && requestedToolNames.Length > 0) {
                candidates.RemoveAll(candidate => !MatchesExplicitRequestedToolName(candidate.Entry.ToolName, requestedToolNames));
                if (candidates.Count == 0) {
                    hasCandidates = false;
                }
            }

            if (!hasCandidates || candidates.Count == 0) {
                selected = Array.Empty<ThreadToolEvidenceEntry>();
            } else {
                candidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));
                var takeCount = Math.Min(3, candidates.Count);
                selected = new ThreadToolEvidenceEntry[takeCount];
                for (var i = 0; i < takeCount; i++) {
                    selected[i] = candidates[i].Entry;
                }
            }

            TrimThreadToolEvidenceContextsNoLock(nowUtc.Ticks);
            if (!shouldClearSnapshot && expiredKeys.Count > 0) {
                updatedSnapshotEntries = SnapshotThreadToolEvidenceEntries(bySignature);
            }
        }

        if (shouldClearSnapshot) {
            PersistThreadToolEvidenceSnapshot(normalizedThreadId, Array.Empty<ThreadToolEvidenceEntry>());
        } else if (updatedSnapshotEntries is not null) {
            PersistThreadToolEvidenceSnapshot(normalizedThreadId, updatedSnapshotEntries);
        }

        if (!hasCandidates) {
            return false;
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
            sb.AppendLine();
            sb.Append("#### ").AppendLine(entry.ToolName);
            AppendMarkdownBlock(sb, snippet);
        }

        sb.AppendLine();
        sb.Append("If you want a live refresh, ask me to rerun these checks now.");
        text = sb.ToString().Trim();
        return text.Length > 0;
    }

    private bool ShouldBypassCachedToolEvidenceFallback(string threadId, string userRequest) {
        return HasFreshThreadToolEvidence(threadId)
               && (LooksLikeLiveRefreshFollowUp(userRequest)
                   || LooksLikeExplicitLiveRefreshToolRequest(userRequest));
    }

    private bool HasFreshThreadToolEvidence(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        TryHydrateThreadToolEvidenceFromSnapshot(normalizedThreadId);

        var nowUtc = DateTime.UtcNow;
        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature) || bySignature.Count == 0) {
                return false;
            }

            foreach (var pair in bySignature) {
                if (TryGetUtcDateTimeFromTicks(pair.Value.SeenUtcTicks, out var seenUtc)
                    && nowUtc - seenUtc <= ThreadToolEvidenceContextMaxAge) {
                    return true;
                }
            }
        }

        return false;
    }

    private string ResolveRequestedToolEvidenceFamily(string threadId, string userRequest) {
        if (ShouldTreatAsPassiveCompactFollowUp(threadId, userRequest)) {
            return string.Empty;
        }

        var availableDefinitions = _registry.GetDefinitions();
        if (TryResolveDomainIntentFamilyFromUserSignals(userRequest, availableDefinitions, out var inferredFamily)) {
            return inferredFamily;
        }

        return TryGetCurrentDomainIntentFamily(threadId, out var rememberedFamily)
            ? rememberedFamily
            : string.Empty;
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

    private static void AppendMarkdownBlock(StringBuilder sb, string markdown) {
        var normalized = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0) {
            sb.AppendLine("(no summary available)");
            return;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++) {
            sb.AppendLine(lines[i]);
        }
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

    private static string[] ExtractExplicitRequestedToolNames(string userRequest) {
        var request = NormalizeExplicitToolReferenceInput(userRequest);
        if (request.Length == 0) {
            return Array.Empty<string>();
        }

        MatchCollection matches;
        try {
            matches = ExplicitRequestedToolNameRegex.Matches(request);
        } catch (RegexMatchTimeoutException) {
            return Array.Empty<string>();
        }
        if (matches.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(Math.Min(8, matches.Count));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < matches.Count && names.Count < 8; i++) {
            var value = (matches[i].Value ?? string.Empty).Trim();
            if (value.Length == 0) {
                continue;
            }

            value = value
                .Replace("\\_", "_", StringComparison.Ordinal)
                .Replace("\\-", "-", StringComparison.Ordinal);
            var normalized = NormalizeCompactToken(value.AsSpan());
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            names.Add(normalized);
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static string NormalizeExplicitToolReferenceInput(string userRequest) {
        var raw = (userRequest ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return string.Empty;
        }

        var normalized = raw.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.Format) {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool MatchesExplicitRequestedToolName(string toolName, IReadOnlyList<string> requestedToolNames) {
        var normalizedToolName = NormalizeCompactToken((toolName ?? string.Empty).AsSpan());
        if (normalizedToolName.Length == 0 || requestedToolNames.Count == 0) {
            return false;
        }

        for (var i = 0; i < requestedToolNames.Count; i++) {
            var requested = (requestedToolNames[i] ?? string.Empty).Trim();
            if (requested.Length == 0) {
                continue;
            }

            if (string.Equals(normalizedToolName, requested, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static double ComputeToolEvidenceTokenScore(int tokenHits, int strongTokenHits) {
        if (tokenHits <= 0 && strongTokenHits <= 0) {
            return 0d;
        }

        return (tokenHits * 9d) + (strongTokenHits * 14d);
    }

    private static bool IsStrongToolEvidenceToken(string token) {
        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.IndexOf('_') >= 0 || normalized.IndexOf('-') >= 0) {
            return true;
        }

        if (normalized.Length >= 7) {
            return true;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsDigit(normalized[i])) {
                return true;
            }
        }

        return false;
    }

    private static (int TokenHits, int StrongTokenHits) CountToolEvidenceTokenHits(string[] requestTokens, ThreadToolEvidenceEntry entry) {
        if (requestTokens.Length == 0) {
            return (0, 0);
        }

        var searchText = (entry.ToolName + " " + entry.SummaryMarkdown + " " + entry.Output).Trim();
        if (searchText.Length == 0) {
            return (0, 0);
        }

        var tokenHits = 0;
        var strongTokenHits = 0;
        for (var i = 0; i < requestTokens.Length; i++) {
            var token = requestTokens[i];
            if (token.Length == 0) {
                continue;
            }

            if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                tokenHits++;
                if (IsStrongToolEvidenceToken(token)) {
                    strongTokenHits++;
                }
            }
        }

        return (tokenHits, strongTokenHits);
    }

    private void TryHydrateThreadToolEvidenceFromSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        lock (_threadToolEvidenceLock) {
            if (_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var existing) && existing.Count > 0) {
                return;
            }
        }

        if (!TryLoadThreadToolEvidenceSnapshot(normalizedThreadId, out var snapshotEntries) || snapshotEntries.Length == 0) {
            return;
        }

        var bySignature = new Dictionary<string, ThreadToolEvidenceEntry>(StringComparer.Ordinal);
        for (var i = 0; i < snapshotEntries.Length; i++) {
            var entry = snapshotEntries[i];
            var signature = BuildToolEvidenceSignature(entry.ToolName, entry.ArgumentsJson);
            if (signature.Length == 0) {
                continue;
            }

            bySignature[signature] = entry;
        }

        if (bySignature.Count == 0) {
            PersistThreadToolEvidenceSnapshot(normalizedThreadId, Array.Empty<ThreadToolEvidenceEntry>());
            return;
        }

        lock (_threadToolEvidenceLock) {
            if (_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var existing) && existing.Count > 0) {
                return;
            }

            _threadToolEvidenceByThreadId[normalizedThreadId] = bySignature;
            TrimThreadToolEvidenceEntriesNoLock(bySignature);
            TrimThreadToolEvidenceContextsNoLock(DateTime.UtcNow.Ticks);
        }
    }

    private static ThreadToolEvidenceEntry[] SnapshotThreadToolEvidenceEntries(
        IReadOnlyDictionary<string, ThreadToolEvidenceEntry> bySignature) {
        if (bySignature.Count == 0) {
            return Array.Empty<ThreadToolEvidenceEntry>();
        }

        var entries = new ThreadToolEvidenceEntry[bySignature.Count];
        var index = 0;
        foreach (var pair in bySignature) {
            entries[index++] = pair.Value;
        }

        return entries;
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

    private string[] CollectThreadHostCandidatesByDomainIntentFamily(string threadId, string family) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedFamily.Length == 0) {
            return Array.Empty<string>();
        }

        TryHydrateThreadToolEvidenceFromSnapshot(normalizedThreadId);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature) || bySignature.Count == 0) {
                return Array.Empty<string>();
            }

            foreach (var entry in bySignature.Values) {
                var entryFamily = ResolveDomainIntentFamily(entry.ToolName);
                if (!string.Equals(entryFamily, normalizedFamily, StringComparison.Ordinal)) {
                    continue;
                }

                CollectHostCandidatesFromSerializedJson(entry.ArgumentsJson, candidates);
                CollectHostCandidatesFromSerializedJson(entry.Output, candidates);
            }
        }

        return candidates.Count == 0 ? Array.Empty<string>() : candidates.ToArray();
    }

    private static void CollectHostCandidatesFromSerializedJson(string payload, HashSet<string> candidates) {
        var normalized = (payload ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized[0] != '{') {
            return;
        }

        try {
            using var doc = JsonDocument.Parse(normalized, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return;
            }

            CollectHostCandidates(doc.RootElement, candidates, depth: 0, maxDepth: 4, budget: 256);
        } catch (JsonException) {
            // Best-effort host candidate extraction only.
        }
    }

    private void ClearThreadToolEvidence() {
        lock (_threadToolEvidenceLock) {
            _threadToolEvidenceByThreadId.Clear();
        }
        ClearThreadToolEvidenceSnapshotsNoThrow();
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

    internal string[] CollectThreadHostCandidatesByDomainIntentFamilyForTesting(string threadId, string family) {
        return CollectThreadHostCandidatesByDomainIntentFamily(threadId, family);
    }

    internal static string[] ExtractExplicitRequestedToolNamesForTesting(string userRequest) {
        return ExtractExplicitRequestedToolNames(userRequest);
    }
}
