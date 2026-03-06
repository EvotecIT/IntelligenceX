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
        string[] CapabilityEnabledPackIds,
        string[] CapabilityRoutingFamilies,
        string[] CapabilitySkills,
        string[] CapabilityHealthyToolNames,
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
        var capabilityEnabledPackIds = ResolveWorkingMemoryCapabilityEnabledPackIds(existing.CapabilityEnabledPackIds);
        var capabilityRoutingFamilies = ResolveWorkingMemoryCapabilityRoutingFamilies(existing.CapabilityRoutingFamilies);
        var capabilitySkills = ResolveWorkingMemoryCapabilitySkills(existing.CapabilitySkills);
        var capabilityHealthyToolNames = ResolveWorkingMemoryCapabilityHealthyToolNames(
            recentToolNames,
            existing.CapabilityHealthyToolNames);

        if (resolvedIntentAnchor.Length == 0
            && resolvedDomainIntentFamily.Length == 0
            && recentToolNames.Length == 0
            && recentEvidenceSnippets.Length == 0
            && capabilityEnabledPackIds.Length == 0
            && capabilityRoutingFamilies.Length == 0
            && capabilitySkills.Length == 0
            && capabilityHealthyToolNames.Length == 0) {
            return;
        }

        var checkpoint = new WorkingMemoryCheckpoint(
            IntentAnchor: resolvedIntentAnchor,
            DomainIntentFamily: resolvedDomainIntentFamily,
            RecentToolNames: recentToolNames,
            RecentEvidenceSnippets: recentEvidenceSnippets,
            CapabilityEnabledPackIds: capabilityEnabledPackIds,
            CapabilityRoutingFamilies: capabilityRoutingFamilies,
            CapabilitySkills: capabilitySkills,
            CapabilityHealthyToolNames: capabilityHealthyToolNames,
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

        if (ShouldTreatAsPassiveCompactFollowUp(normalizedThreadId, normalizedFollowUp)) {
            return false;
        }

        if (ShouldSkipWorkingMemoryAugmentationForStructuredSelection(normalizedThreadId, normalizedFollowUp)) {
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
            && checkpoint.RecentEvidenceSnippets.Length == 0
            && checkpoint.CapabilityEnabledPackIds.Length == 0
            && checkpoint.CapabilityRoutingFamilies.Length == 0
            && checkpoint.CapabilitySkills.Length == 0
            && checkpoint.CapabilityHealthyToolNames.Length == 0) {
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

        if (checkpoint.CapabilityEnabledPackIds.Length > 0
            || checkpoint.CapabilityRoutingFamilies.Length > 0
            || checkpoint.CapabilitySkills.Length > 0
            || checkpoint.CapabilityHealthyToolNames.Length > 0) {
            builder.AppendLine();
            builder.AppendLine("[Capability snapshot]");
            builder.AppendLine(CapabilitySnapshotMarker);
            if (checkpoint.CapabilityEnabledPackIds.Length > 0) {
                builder.Append("enabled_packs: ").AppendLine(string.Join(", ", checkpoint.CapabilityEnabledPackIds));
            }

            if (checkpoint.CapabilityRoutingFamilies.Length > 0) {
                builder.Append("routing_families: ").AppendLine(string.Join(", ", checkpoint.CapabilityRoutingFamilies));
            }

            if (checkpoint.CapabilitySkills.Length > 0) {
                builder.Append("skills: ").AppendLine(string.Join(", ", checkpoint.CapabilitySkills));
            }

            if (checkpoint.CapabilityHealthyToolNames.Length > 0) {
                builder.Append("healthy_tools: ").AppendLine(string.Join(", ", checkpoint.CapabilityHealthyToolNames));
            }
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
        if (normalized.Length == 0
            || LooksLikeContinuationFollowUp(normalized)
            || LooksLikeStructuredIntentPayload(normalized)) {
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

    private string[] ResolveWorkingMemoryCapabilityEnabledPackIds(IReadOnlyList<string> fallbackEnabledPackIds) {
        var enabledPackIds = _packAvailability
            .Where(static pack => pack.Enabled)
            .Select(static pack => NormalizePackId(pack.Id))
            .Where(static packId => packId.Length > 0);
        var normalized = NormalizeDistinctStrings(enabledPackIds, MaxCapabilitySnapshotPackIds);
        if (normalized.Length > 0) {
            return normalized;
        }

        return NormalizeDistinctStrings(
            (fallbackEnabledPackIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0),
            MaxCapabilitySnapshotPackIds);
    }

    private string[] ResolveWorkingMemoryCapabilityRoutingFamilies(IReadOnlyList<string> fallbackRoutingFamilies) {
        var routingFamilies = _routingCatalogDiagnostics.FamilyActions
            .Select(static summary => summary.Family);
        var normalized = NormalizeCapabilitySnapshotRoutingFamilies(routingFamilies);
        if (normalized.Length > 0) {
            return normalized;
        }

        return NormalizeCapabilitySnapshotRoutingFamilies(fallbackRoutingFamilies ?? Array.Empty<string>());
    }

    private string[] ResolveWorkingMemoryCapabilitySkills(IReadOnlyList<string> fallbackSkills) {
        return ResolveCapabilitySnapshotSkills(_pluginAvailability, _routingCatalogDiagnostics, _connectedRuntimeSkillInventory, fallbackSkills);
    }

    private static string BuildSkillSnapshotValue(string family, string actionId) {
        var normalizedFamily = NormalizeSkillSnapshotToken(family);
        var normalizedActionId = NormalizeSkillSnapshotToken(actionId);
        if (normalizedFamily.Length == 0 || normalizedActionId.Length == 0) {
            return string.Empty;
        }

        return normalizedFamily + "." + normalizedActionId;
    }

    private static string NormalizeSkillSnapshotValue(string value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 128) {
            return string.Empty;
        }

        if (normalized.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
            return string.Empty;
        }

        return normalized;
    }

    private static string NormalizeSkillSnapshotToken(string value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 64) {
            return string.Empty;
        }

        if (normalized.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
            return string.Empty;
        }

        return normalized;
    }

    private string[] ResolveWorkingMemoryCapabilityHealthyToolNames(
        IReadOnlyList<string> recentToolNames,
        IReadOnlyList<string> fallbackHealthyToolNames) {
        var candidates = new List<string>();
        if (recentToolNames is { Count: > 0 }) {
            candidates.AddRange(recentToolNames);
        }

        var nowUtc = DateTime.UtcNow;
        lock (_toolRoutingStatsLock) {
            var healthyFromStats = _toolRoutingStats
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(static pair => (ToolName: pair.Key.Trim(), Stats: pair.Value))
                .Where(static pair => pair.ToolName.Length > 0 && pair.Stats.LastSuccessUtcTicks > 0)
                .Select(pair => {
                    var hasSeen = TryGetUtcDateTimeFromTicks(pair.Stats.LastSuccessUtcTicks, out var seenUtc);
                    return (pair.ToolName, HasSeen: hasSeen, SeenUtc: seenUtc);
                })
                .Where(pair => pair.HasSeen && pair.SeenUtc <= nowUtc && nowUtc - pair.SeenUtc <= UserIntentContextMaxAge)
                .OrderByDescending(static pair => pair.SeenUtc)
                .ThenBy(static pair => pair.ToolName, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => pair.ToolName)
                .Take(MaxCapabilitySnapshotHealthyTools)
                .ToArray();
            if (healthyFromStats.Length > 0) {
                candidates.AddRange(healthyFromStats);
            }
        }

        if (candidates.Count == 0 && fallbackHealthyToolNames is { Count: > 0 }) {
            candidates.AddRange(fallbackHealthyToolNames);
        }

        return NormalizeCapabilitySnapshotHealthyToolNames(candidates);
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

        ClearPersistedWorkingMemoryCheckpointStore();
    }

    private bool ShouldSkipWorkingMemoryAugmentationForStructuredSelection(string threadId, string normalizedFollowUp) {
        if (TryParseExplicitActSelection(normalizedFollowUp, out _, out _)) {
            return true;
        }

        if (TryReadActionSelectionIntent(normalizedFollowUp, out _, out _)) {
            return true;
        }

        if (TryParseDomainIntentMarkerSelection(normalizedFollowUp, DomainIntentMarker, out _)
            || TryParseDomainIntentChoiceMarkerSelection(normalizedFollowUp, out _)
            || TryNormalizeDomainIntentFamily(normalizedFollowUp, out _)
            || TryParseDomainIntentFamilyFromDomainScopePayload(normalizedFollowUp, out _)) {
            return true;
        }

        return HasFreshPendingDomainIntentClarificationForWorkingMemory(threadId)
               && TryParsePendingDomainIntentClarificationSelection(normalizedFollowUp, out _);
    }

    private bool HasFreshPendingDomainIntentClarificationForWorkingMemory(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        long clarificationSeenTicks;
        lock (_toolRoutingContextLock) {
            _pendingDomainIntentClarificationSeenUtcTicks.TryGetValue(normalizedThreadId, out clarificationSeenTicks);
        }

        if (clarificationSeenTicks <= 0) {
            if (!TryLoadPendingDomainIntentClarificationSnapshot(normalizedThreadId, out clarificationSeenTicks)) {
                return false;
            }

            lock (_toolRoutingContextLock) {
                _pendingDomainIntentClarificationSeenUtcTicks[normalizedThreadId] = clarificationSeenTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        var nowUtc = DateTime.UtcNow;
        if (!TryGetUtcDateTimeFromTicks(clarificationSeenTicks, out var clarificationSeenUtc)
            || clarificationSeenUtc > nowUtc
            || nowUtc - clarificationSeenUtc > DomainIntentClarificationContextMaxAge) {
            lock (_toolRoutingContextLock) {
                _pendingDomainIntentClarificationSeenUtcTicks.Remove(normalizedThreadId);
            }

            RemovePendingDomainIntentClarificationSnapshot(normalizedThreadId);
            return false;
        }

        return true;
    }

    internal void RememberWorkingMemoryCheckpointForTesting(
        string threadId,
        string intentAnchor,
        string domainIntentFamily,
        IReadOnlyList<string> recentToolNames,
        IReadOnlyList<string> recentEvidenceSnippets,
        IReadOnlyList<string>? enabledPackIds = null,
        IReadOnlyList<string>? routingFamilies = null,
        IReadOnlyList<string>? skills = null,
        IReadOnlyList<string>? healthyToolNames = null,
        long? seenUtcTicks = null) {
        var checkpoint = new WorkingMemoryCheckpoint(
            IntentAnchor: NormalizeWorkingMemoryIntentAnchor(intentAnchor),
            DomainIntentFamily: (domainIntentFamily ?? string.Empty).Trim(),
            RecentToolNames: NormalizeWorkingMemoryList(recentToolNames ?? Array.Empty<string>(), MaxWorkingMemoryToolNames),
            RecentEvidenceSnippets: NormalizeWorkingMemoryList(recentEvidenceSnippets ?? Array.Empty<string>(), MaxWorkingMemoryEvidenceLines),
            CapabilityEnabledPackIds: NormalizeDistinctStrings(
                (enabledPackIds ?? Array.Empty<string>())
                .Select(static packId => NormalizePackId(packId))
                .Where(static packId => packId.Length > 0),
                MaxCapabilitySnapshotPackIds),
            CapabilityRoutingFamilies: NormalizeCapabilitySnapshotRoutingFamilies(routingFamilies ?? Array.Empty<string>()),
            CapabilitySkills: ResolveWorkingMemoryCapabilitySkills(skills ?? Array.Empty<string>()),
            CapabilityHealthyToolNames: NormalizeCapabilitySnapshotHealthyToolNames(healthyToolNames ?? Array.Empty<string>()),
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
