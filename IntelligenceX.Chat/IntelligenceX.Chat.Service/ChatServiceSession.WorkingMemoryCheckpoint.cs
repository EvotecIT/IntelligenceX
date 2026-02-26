using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxTrackedWorkingMemoryContexts = 256;
    private const int MaxWorkingMemoryIntentChars = 600;
    private const int MaxWorkingMemoryToolNames = 8;
    private const int MaxWorkingMemoryEvidenceLines = 3;
    private const int MaxWorkingMemoryEvidenceChars = 220;
    private const int MaxWorkingMemoryAugmentedRequestChars = 1600;
    private static readonly TimeSpan WorkingMemoryContextMaxAge = TimeSpan.FromHours(24);
    private const string WorkingMemoryMarker = "ix:working-memory:v1";
    private readonly object _workingMemoryCheckpointLock = new();
    private readonly Dictionary<string, WorkingMemoryCheckpoint> _workingMemoryCheckpointByThreadId = new(StringComparer.Ordinal);

    private readonly record struct WorkingMemoryCheckpoint(
        string IntentAnchor,
        string DomainIntentFamily,
        string[] RecentToolNames,
        string[] RecentEvidenceSnippets,
        long SeenUtcTicks);

    private void RememberWorkingMemoryCheckpoint(
        string threadId,
        string userIntent,
        string routedUserRequest,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        if (!TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var existing)) {
            existing = default;
        }

        var resolvedIntentAnchor = ResolveWorkingMemoryIntentAnchor(userIntent, routedUserRequest, existing.IntentAnchor);
        var resolvedDomainIntentFamily = ResolveWorkingMemoryDomainIntentFamily(normalizedThreadId, existing.DomainIntentFamily);
        var (recentToolNames, recentEvidenceSnippets) = ResolveWorkingMemoryToolSignals(
            toolCalls,
            toolOutputs,
            mutatingToolHintsByName,
            fallbackToolNames: existing.RecentToolNames,
            fallbackEvidenceSnippets: existing.RecentEvidenceSnippets);

        if (resolvedIntentAnchor.Length == 0
            && resolvedDomainIntentFamily.Length == 0
            && recentToolNames.Length == 0
            && recentEvidenceSnippets.Length == 0) {
            return;
        }

        var checkpoint = new WorkingMemoryCheckpoint(
            IntentAnchor: resolvedIntentAnchor,
            DomainIntentFamily: resolvedDomainIntentFamily,
            RecentToolNames: recentToolNames,
            RecentEvidenceSnippets: recentEvidenceSnippets,
            SeenUtcTicks: DateTime.UtcNow.Ticks);
        UpsertWorkingMemoryCheckpoint(normalizedThreadId, checkpoint);
    }

    private bool TryAugmentRoutedUserRequestFromWorkingMemoryCheckpoint(
        string threadId,
        string userRequest,
        string routedUserRequest,
        out string augmentedUserRequest) {
        augmentedUserRequest = routedUserRequest;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var normalizedFollowUp = (userRequest ?? string.Empty).Trim();
        if (normalizedFollowUp.Length == 0 || !LooksLikeContinuationFollowUp(normalizedFollowUp)) {
            return false;
        }

        var normalizedRouted = (routedUserRequest ?? string.Empty).Trim();
        if (!string.Equals(normalizedFollowUp, normalizedRouted, StringComparison.Ordinal)) {
            return false;
        }

        if (!TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var checkpoint)) {
            return false;
        }

        if (checkpoint.IntentAnchor.Length == 0
            && checkpoint.DomainIntentFamily.Length == 0
            && checkpoint.RecentToolNames.Length == 0
            && checkpoint.RecentEvidenceSnippets.Length == 0) {
            return false;
        }

        var builder = new StringBuilder(1024);
        builder.AppendLine("[Working memory checkpoint]");
        builder.AppendLine(WorkingMemoryMarker);
        if (checkpoint.IntentAnchor.Length > 0) {
            builder.Append("intent_anchor: ").AppendLine(checkpoint.IntentAnchor);
        }

        if (checkpoint.DomainIntentFamily.Length > 0) {
            builder.Append("domain_scope_family: ").AppendLine(checkpoint.DomainIntentFamily);
        }

        if (checkpoint.RecentToolNames.Length > 0) {
            builder.Append("recent_tools: ").AppendLine(string.Join(", ", checkpoint.RecentToolNames));
        }

        for (var i = 0; i < checkpoint.RecentEvidenceSnippets.Length; i++) {
            builder.Append("recent_evidence_")
                .Append(i + 1)
                .Append(": ")
                .AppendLine(checkpoint.RecentEvidenceSnippets[i]);
        }

        builder.Append("follow_up: ").Append(normalizedFollowUp);
        var expanded = builder.ToString().Trim();
        if (expanded.Length == 0) {
            return false;
        }

        if (expanded.Length > MaxWorkingMemoryAugmentedRequestChars) {
            expanded = expanded.Substring(0, MaxWorkingMemoryAugmentedRequestChars).TrimEnd();
        }

        if (expanded.Length == 0 || string.Equals(expanded, routedUserRequest, StringComparison.Ordinal)) {
            return false;
        }

        augmentedUserRequest = expanded;
        return true;
    }

    private void UpsertWorkingMemoryCheckpoint(string threadId, WorkingMemoryCheckpoint checkpoint) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || checkpoint.SeenUtcTicks <= 0) {
            return;
        }

        lock (_workingMemoryCheckpointLock) {
            _workingMemoryCheckpointByThreadId[normalizedThreadId] = checkpoint;
            TrimWorkingMemoryCheckpointsNoLock();
        }

        PersistWorkingMemoryCheckpointSnapshot(normalizedThreadId, checkpoint);
    }

    private bool TryGetWorkingMemoryCheckpoint(string threadId, out WorkingMemoryCheckpoint checkpoint) {
        checkpoint = default;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        lock (_workingMemoryCheckpointLock) {
            if (_workingMemoryCheckpointByThreadId.TryGetValue(normalizedThreadId, out checkpoint)
                && IsWorkingMemoryCheckpointFresh(checkpoint)) {
                return true;
            }

            _workingMemoryCheckpointByThreadId.Remove(normalizedThreadId);
        }

        if (!TryLoadWorkingMemoryCheckpointSnapshot(normalizedThreadId, out checkpoint) || !IsWorkingMemoryCheckpointFresh(checkpoint)) {
            RemoveWorkingMemoryCheckpointSnapshot(normalizedThreadId);
            checkpoint = default;
            return false;
        }

        lock (_workingMemoryCheckpointLock) {
            _workingMemoryCheckpointByThreadId[normalizedThreadId] = checkpoint;
            TrimWorkingMemoryCheckpointsNoLock();
        }

        return true;
    }

    private static bool IsWorkingMemoryCheckpointFresh(WorkingMemoryCheckpoint checkpoint) {
        if (!TryGetUtcDateTimeFromTicks(checkpoint.SeenUtcTicks, out var seenUtc)) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        if (seenUtc > nowUtc) {
            return false;
        }

        return nowUtc - seenUtc <= WorkingMemoryContextMaxAge;
    }

    private static string ResolveWorkingMemoryIntentAnchor(string userIntent, string routedUserRequest, string fallbackIntentAnchor) {
        var directIntentAnchor = NormalizeWorkingMemoryIntentAnchor(userIntent);
        if (directIntentAnchor.Length > 0) {
            return directIntentAnchor;
        }

        if (TryReadIntentAnchorFromWorkingMemoryPrompt(routedUserRequest, out var promptIntentAnchor)) {
            return promptIntentAnchor;
        }

        if (TryReadIntentAnchorFromLegacyContinuationExpansion(routedUserRequest, out var legacyIntentAnchor)) {
            return legacyIntentAnchor;
        }

        return NormalizeWorkingMemoryIntentAnchor(fallbackIntentAnchor);
    }

    private string ResolveWorkingMemoryDomainIntentFamily(string threadId, string fallbackDomainIntentFamily) {
        if (TryGetCurrentDomainIntentFamily(threadId, out var currentFamily) && !string.IsNullOrWhiteSpace(currentFamily)) {
            return currentFamily.Trim();
        }

        var fallback = (fallbackDomainIntentFamily ?? string.Empty).Trim();
        return fallback.Length == 0 ? string.Empty : fallback;
    }

    private static (string[] ToolNames, string[] EvidenceSnippets) ResolveWorkingMemoryToolSignals(
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName,
        IReadOnlyList<string> fallbackToolNames,
        IReadOnlyList<string> fallbackEvidenceSnippets) {
        if (toolCalls is null || toolCalls.Count == 0 || toolOutputs is null || toolOutputs.Count == 0) {
            return (
                NormalizeWorkingMemoryList(fallbackToolNames, MaxWorkingMemoryToolNames),
                NormalizeWorkingMemoryList(fallbackEvidenceSnippets, MaxWorkingMemoryEvidenceLines));
        }

        var callNameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mutatingToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var call = toolCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0 || toolName.Length == 0) {
                continue;
            }

            callNameByCallId[callId] = toolName;
            if (mutatingToolHintsByName.TryGetValue(toolName, out var isMutating) && isMutating) {
                mutatingToolNames.Add(toolName);
            }
        }

        if (callNameByCallId.Count == 0) {
            return (
                NormalizeWorkingMemoryList(fallbackToolNames, MaxWorkingMemoryToolNames),
                NormalizeWorkingMemoryList(fallbackEvidenceSnippets, MaxWorkingMemoryEvidenceLines));
        }

        var toolNames = new List<string>();
        var evidenceSnippets = new List<string>();
        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || !callNameByCallId.TryGetValue(callId, out var toolName)) {
                continue;
            }

            if (mutatingToolNames.Contains(toolName)) {
                continue;
            }

            var success = output.Ok != false
                          && string.IsNullOrWhiteSpace(output.ErrorCode)
                          && string.IsNullOrWhiteSpace(output.Error);
            if (!success) {
                continue;
            }

            toolNames.Add(toolName);
            var snippet = BuildWorkingMemoryEvidenceSnippet(output);
            if (snippet.Length > 0) {
                evidenceSnippets.Add(toolName + ": " + snippet);
            }
        }

        if (toolNames.Count == 0 && evidenceSnippets.Count == 0) {
            return (
                NormalizeWorkingMemoryList(fallbackToolNames, MaxWorkingMemoryToolNames),
                NormalizeWorkingMemoryList(fallbackEvidenceSnippets, MaxWorkingMemoryEvidenceLines));
        }

        return (
            NormalizeWorkingMemoryList(toolNames, MaxWorkingMemoryToolNames),
            NormalizeWorkingMemoryList(evidenceSnippets, MaxWorkingMemoryEvidenceLines));
    }

    private static string BuildWorkingMemoryEvidenceSnippet(ToolOutputDto output) {
        var summary = (output.SummaryMarkdown ?? string.Empty).Trim();
        var snippet = summary.Length > 0 ? summary : BuildToolEvidenceSnippet(output.Output ?? string.Empty);
        if (snippet.Length > MaxWorkingMemoryEvidenceChars) {
            snippet = snippet.Substring(0, MaxWorkingMemoryEvidenceChars).TrimEnd() + "...";
        }

        return snippet;
    }

    private static string NormalizeWorkingMemoryIntentAnchor(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || LooksLikeContinuationFollowUp(normalized)) {
            return string.Empty;
        }

        if (normalized.Length > MaxWorkingMemoryIntentChars) {
            normalized = normalized.Substring(0, MaxWorkingMemoryIntentChars).TrimEnd();
        }

        return normalized;
    }

    private static string[] NormalizeWorkingMemoryList(IEnumerable<string> values, int maxItems) {
        return NormalizeDistinctStrings(values ?? Array.Empty<string>(), maxItems);
    }

    private static bool TryReadIntentAnchorFromWorkingMemoryPrompt(string routedUserRequest, out string intentAnchor) {
        intentAnchor = string.Empty;
        var raw = routedUserRequest ?? string.Empty;
        if (raw.IndexOf(WorkingMemoryMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        using var reader = new System.IO.StringReader(raw);
        while (reader.ReadLine() is { } line) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("intent_anchor:", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator < 0 || separator + 1 >= trimmed.Length) {
                continue;
            }

            var value = NormalizeWorkingMemoryIntentAnchor(trimmed[(separator + 1)..]);
            if (value.Length == 0) {
                continue;
            }

            intentAnchor = value;
            return true;
        }

        return false;
    }

    private static bool TryReadIntentAnchorFromLegacyContinuationExpansion(string routedUserRequest, out string intentAnchor) {
        intentAnchor = string.Empty;
        var raw = (routedUserRequest ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return false;
        }

        var marker = raw.IndexOf("\nFollow-up:", StringComparison.OrdinalIgnoreCase);
        if (marker <= 0) {
            return false;
        }

        var prefix = NormalizeWorkingMemoryIntentAnchor(raw[..marker]);
        if (prefix.Length == 0) {
            return false;
        }

        intentAnchor = prefix;
        return true;
    }

    private void TrimWorkingMemoryCheckpointsNoLock() {
        if (_workingMemoryCheckpointByThreadId.Count <= MaxTrackedWorkingMemoryContexts) {
            return;
        }

        var removeCount = _workingMemoryCheckpointByThreadId.Count - MaxTrackedWorkingMemoryContexts;
        if (removeCount <= 0) {
            return;
        }

        var threadIdsToRemove = _workingMemoryCheckpointByThreadId
            .Select(static pair => (ThreadId: pair.Key, SeenTicks: pair.Value.SeenUtcTicks))
            .OrderBy(static pair => pair.SeenTicks)
            .ThenBy(static pair => pair.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static pair => pair.ThreadId)
            .ToArray();
        foreach (var threadId in threadIdsToRemove) {
            _workingMemoryCheckpointByThreadId.Remove(threadId);
        }
    }

    private void ClearWorkingMemoryCheckpoints() {
        lock (_workingMemoryCheckpointLock) {
            _workingMemoryCheckpointByThreadId.Clear();
        }

        ClearWorkingMemoryCheckpointSnapshots();
    }

    internal void RememberWorkingMemoryCheckpointForTesting(
        string threadId,
        string intentAnchor,
        string domainIntentFamily,
        IReadOnlyList<string> recentToolNames,
        IReadOnlyList<string> recentEvidenceSnippets,
        long? seenUtcTicks = null) {
        var checkpoint = new WorkingMemoryCheckpoint(
            IntentAnchor: NormalizeWorkingMemoryIntentAnchor(intentAnchor),
            DomainIntentFamily: (domainIntentFamily ?? string.Empty).Trim(),
            RecentToolNames: NormalizeWorkingMemoryList(recentToolNames ?? Array.Empty<string>(), MaxWorkingMemoryToolNames),
            RecentEvidenceSnippets: NormalizeWorkingMemoryList(recentEvidenceSnippets ?? Array.Empty<string>(), MaxWorkingMemoryEvidenceLines),
            SeenUtcTicks: seenUtcTicks.GetValueOrDefault(DateTime.UtcNow.Ticks));
        UpsertWorkingMemoryCheckpoint(threadId, checkpoint);
    }

    internal bool TryGetWorkingMemoryCheckpointForTesting(
        string threadId,
        out string intentAnchor,
        out string domainIntentFamily,
        out string[] recentToolNames,
        out string[] recentEvidenceSnippets) {
        intentAnchor = string.Empty;
        domainIntentFamily = string.Empty;
        recentToolNames = Array.Empty<string>();
        recentEvidenceSnippets = Array.Empty<string>();
        if (!TryGetWorkingMemoryCheckpoint(threadId, out var checkpoint)) {
            return false;
        }

        intentAnchor = checkpoint.IntentAnchor;
        domainIntentFamily = checkpoint.DomainIntentFamily;
        recentToolNames = checkpoint.RecentToolNames;
        recentEvidenceSnippets = checkpoint.RecentEvidenceSnippets;
        return true;
    }

    internal bool TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
        string threadId,
        string userRequest,
        string routedUserRequest,
        out string augmentedUserRequest) {
        return TryAugmentRoutedUserRequestFromWorkingMemoryCheckpoint(threadId, userRequest, routedUserRequest, out augmentedUserRequest);
    }
}
