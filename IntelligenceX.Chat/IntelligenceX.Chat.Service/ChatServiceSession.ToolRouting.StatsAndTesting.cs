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
            _lastUserIntentByThreadId.TryGetValue(normalizedThreadId, out intent);
            intentTicks = _lastUserIntentSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (string.IsNullOrWhiteSpace(intent)) {
            if (!TryLoadUserIntentSnapshot(normalizedThreadId, out var persistedIntent, out var persistedTicks)) {
                return raw;
            }

            intent = persistedIntent;
            intentTicks = persistedTicks;
            lock (_toolRoutingContextLock) {
                _lastUserIntentByThreadId[normalizedThreadId] = intent;
                _lastUserIntentSeenUtcTicks[normalizedThreadId] = intentTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (LooksLikeStructuredIntentPayload(intent!)) {
            lock (_toolRoutingContextLock) {
                _lastUserIntentByThreadId.Remove(normalizedThreadId);
                _lastUserIntentSeenUtcTicks.Remove(normalizedThreadId);
                TrimWeightedRoutingContextsNoLock();
            }
            RemoveUserIntentSnapshot(normalizedThreadId);
            return raw;
        }

        if (intentTicks > 0) {
            if (intentTicks < DateTime.MinValue.Ticks || intentTicks > DateTime.MaxValue.Ticks) {
                // Defensive: avoid exceptions from ticks->DateTime conversion if ticks are corrupted/out of range.
                lock (_toolRoutingContextLock) {
                    _lastUserIntentByThreadId.Remove(normalizedThreadId);
                    _lastUserIntentSeenUtcTicks.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemoveUserIntentSnapshot(normalizedThreadId);
                return raw;
            }
            var age = DateTime.UtcNow - new DateTime(intentTicks, DateTimeKind.Utc);
            if (age > UserIntentContextMaxAge) {
                lock (_toolRoutingContextLock) {
                    _lastUserIntentByThreadId.Remove(normalizedThreadId);
                    _lastUserIntentSeenUtcTicks.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemoveUserIntentSnapshot(normalizedThreadId);
                return raw;
            }
        }

        var refreshedSeenTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = refreshedSeenTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistUserIntentSnapshot(normalizedThreadId, intent!, refreshedSeenTicks);

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
        PersistToolRoutingStatsSnapshot();
    }

    private void TrimWeightedRoutingContextsNoLock() {
        Debug.Assert(Monitor.IsEntered(_toolRoutingContextLock));

        // Weighted-tool-subset, user-intent, pending-action, structured-next-action, planner-thread, domain-intent, and pack-preflight
        // contexts share the same
        // key space (active thread id), so trim all when any grows beyond its cap.
        var weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        var intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
        var pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
        var structuredNextActionRemoveCount = _structuredNextActionByThreadId.Count - MaxTrackedStructuredNextActionContexts;
        var plannerRemoveCount = _plannerThreadIdByActiveThreadId.Count - MaxTrackedPlannerThreadContexts;
        var domainIntentRemoveCount = _domainIntentFamilyByThreadId.Count - MaxTrackedDomainIntentFamilyContexts;
        var domainClarificationRemoveCount =
            _pendingDomainIntentClarificationSeenUtcTicks.Count - MaxTrackedDomainIntentClarificationContexts;
        var packPreflightRemoveCount = _packPreflightToolNamesByThreadId.Count - MaxTrackedPackPreflightContexts;
        var removeCount = Math.Max(
            Math.Max(
                Math.Max(Math.Max(weightedRemoveCount, intentRemoveCount), Math.Max(pendingRemoveCount, structuredNextActionRemoveCount)),
                Math.Max(plannerRemoveCount, Math.Max(domainIntentRemoveCount, Math.Max(domainClarificationRemoveCount, packPreflightRemoveCount)))),
            0);
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
                _pendingActionsCallToActionTokensByThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _structuredNextActionByThreadId.Keys.ToArray()) {
            if (!_structuredNextActionByThreadId.TryGetValue(threadId, out var snapshot) || snapshot.SeenUtcTicks <= 0) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (!TryGetUtcDateTimeFromTicks(snapshot.SeenUtcTicks, out var seenUtc)) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            var age = DateTime.UtcNow - seenUtc;
            if (age > StructuredNextActionContextMaxAge) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _plannerThreadIdByActiveThreadId.Keys.ToArray()) {
            if (!_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _plannerThreadIdByActiveThreadId.Remove(threadId);
                _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (age > PlannerThreadContextMaxAge) {
                _plannerThreadIdByActiveThreadId.Remove(threadId);
                _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _domainIntentFamilyByThreadId.Keys.ToArray()) {
            if (!_domainIntentFamilySeenUtcTicks.TryGetValue(threadId, out var ticks) || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)) {
                _domainIntentFamilyByThreadId.Remove(threadId);
                _domainIntentFamilySeenUtcTicks.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (DateTime.UtcNow - seenUtc > DomainIntentFamilyContextMaxAge) {
                _domainIntentFamilyByThreadId.Remove(threadId);
                _domainIntentFamilySeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _pendingDomainIntentClarificationSeenUtcTicks.Keys.ToArray()) {
            if (!_pendingDomainIntentClarificationSeenUtcTicks.TryGetValue(threadId, out var ticks)
                || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)
                || DateTime.UtcNow - seenUtc > DomainIntentClarificationContextMaxAge) {
                _pendingDomainIntentClarificationSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _packPreflightToolNamesByThreadId.Keys.ToArray()) {
            if (!_packPreflightSeenUtcTicks.TryGetValue(threadId, out var ticks) || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)) {
                _packPreflightToolNamesByThreadId.Remove(threadId);
                _packPreflightSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (DateTime.UtcNow - seenUtc > PackPreflightContextMaxAge) {
                _packPreflightToolNamesByThreadId.Remove(threadId);
                _packPreflightSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        if (removedInvalid) {
            weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
            intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
            pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
            structuredNextActionRemoveCount = _structuredNextActionByThreadId.Count - MaxTrackedStructuredNextActionContexts;
            plannerRemoveCount = _plannerThreadIdByActiveThreadId.Count - MaxTrackedPlannerThreadContexts;
            domainIntentRemoveCount = _domainIntentFamilyByThreadId.Count - MaxTrackedDomainIntentFamilyContexts;
            domainClarificationRemoveCount =
                _pendingDomainIntentClarificationSeenUtcTicks.Count - MaxTrackedDomainIntentClarificationContexts;
            packPreflightRemoveCount = _packPreflightToolNamesByThreadId.Count - MaxTrackedPackPreflightContexts;
            removeCount = Math.Max(
                Math.Max(
                    Math.Max(Math.Max(weightedRemoveCount, intentRemoveCount), Math.Max(pendingRemoveCount, structuredNextActionRemoveCount)),
                    Math.Max(plannerRemoveCount, Math.Max(domainIntentRemoveCount, Math.Max(domainClarificationRemoveCount, packPreflightRemoveCount)))),
                0);
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
        foreach (var threadId in _structuredNextActionByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _plannerThreadIdByActiveThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _domainIntentFamilyByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _pendingDomainIntentClarificationSeenUtcTicks.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _packPreflightToolNamesByThreadId.Keys) {
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
                if (_structuredNextActionByThreadId.TryGetValue(threadId, out var structuredNextAction)
                    && structuredNextAction.SeenUtcTicks > ticks) {
                    ticks = structuredNextAction.SeenUtcTicks;
                }
                if (_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(threadId, out var plannerTicks) && plannerTicks > ticks) {
                    ticks = plannerTicks;
                }
                if (_domainIntentFamilySeenUtcTicks.TryGetValue(threadId, out var domainIntentTicks) && domainIntentTicks > ticks) {
                    ticks = domainIntentTicks;
                }
                if (_pendingDomainIntentClarificationSeenUtcTicks.TryGetValue(threadId, out var clarificationTicks) && clarificationTicks > ticks) {
                    ticks = clarificationTicks;
                }
                if (_packPreflightSeenUtcTicks.TryGetValue(threadId, out var preflightTicks) && preflightTicks > ticks) {
                    ticks = preflightTicks;
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
            _pendingActionsCallToActionTokensByThreadId.Remove(threadId);
            _structuredNextActionByThreadId.Remove(threadId);
            _plannerThreadIdByActiveThreadId.Remove(threadId);
            _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
            _domainIntentFamilyByThreadId.Remove(threadId);
            _domainIntentFamilySeenUtcTicks.Remove(threadId);
            _pendingDomainIntentClarificationSeenUtcTicks.Remove(threadId);
            _packPreflightToolNamesByThreadId.Remove(threadId);
            _packPreflightSeenUtcTicks.Remove(threadId);
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

    internal void PersistToolRoutingStatsForTesting() {
        PersistToolRoutingStatsSnapshot();
    }

    internal void UpdateToolRoutingStatsForTesting(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(outputs);
        UpdateToolRoutingStats(calls, outputs);
    }

    internal string ExpandContinuationUserRequestForTesting(string threadId, string userRequest) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(userRequest);
        return ExpandContinuationUserRequest(threadId, userRequest);
    }

    internal void RememberUserIntentForTesting(string threadId, string userRequest) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(userRequest);
        RememberUserIntent(threadId, userRequest);
    }

    internal void RememberPendingActionsForTesting(string threadId, string assistantReply) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(assistantReply);
        RememberPendingActions(threadId, assistantReply);
    }

    internal bool HasFreshPendingActionsContextForTesting(string threadId) {
        ArgumentNullException.ThrowIfNull(threadId);
        return HasFreshPendingActionsContext(threadId);
    }

    internal double ReadToolRoutingAdjustmentForTesting(string toolName) {
        return ReadToolRoutingAdjustment(toolName);
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

    internal void SetPreferredDomainIntentFamilyForTesting(string threadId, string family, long? seenUtcTicks = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedFamily.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _domainIntentFamilyByThreadId[normalizedThreadId] = normalizedFamily;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = seenUtcTicks ?? DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    internal string? GetPreferredDomainIntentFamilyForTesting(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return null;
        }

        lock (_toolRoutingContextLock) {
            return _domainIntentFamilyByThreadId.TryGetValue(normalizedThreadId, out var family)
                ? family
                : null;
        }
    }

    internal bool TryGetCurrentDomainIntentFamilyForTesting(string threadId, out string family) {
        return TryGetCurrentDomainIntentFamily(threadId, out family);
    }

    internal bool TryApplyDomainIntentAffinityForTesting(
        string threadId,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentAffinity(threadId, selectedTools, out filteredTools, out family, out removedCount);
    }

    internal bool TryApplyDomainIntentSignalRoutingHintForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentSignalRoutingHint(threadId, userRequest, selectedTools, out filteredTools, out family, out removedCount);
    }

    internal void RememberPreferredDomainIntentFamilyForTesting(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        RememberPreferredDomainIntentFamily(threadId, toolCalls, toolOutputs, mutatingToolHintsByName);
    }

    internal static bool HasConflictingDomainIntentSignalsForTesting(string userRequest) {
        return HasConflictingDomainIntentSignals(userRequest);
    }

    internal static bool ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> allDefinitions) {
        return ShouldForceDomainIntentClarificationForConflictingSignals(userRequest, allDefinitions);
    }

    internal static bool ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting(
        bool compactFollowUpTurn,
        bool hasPreferredDomainIntentFamily,
        bool hasFreshPendingActionContext,
        bool conflictingDomainSignals) {
        return ShouldSuppressDomainIntentClarificationForCompactFollowUp(
            compactFollowUpTurn,
            hasPreferredDomainIntentFamily,
            hasFreshPendingActionContext,
            conflictingDomainSignals);
    }

    internal void RememberPendingDomainIntentClarificationRequestForTesting(string threadId) {
        RememberPendingDomainIntentClarificationRequest(threadId);
    }

    internal bool TryResolvePendingDomainIntentClarificationSelectionForTesting(string threadId, string userRequest, out string family) {
        return TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, out family);
    }

    internal bool TryResolvePendingDomainIntentClarificationSelectionForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> availableDefinitions,
        out string family) {
        return TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, availableDefinitions, out family);
    }

    internal static string BuildDomainIntentClarificationTextForTesting(bool hasAdFamily, bool hasPublicFamily) {
        return BuildDomainIntentClarificationText(new DomainIntentFamilyAvailability(HasAd: hasAdFamily, HasPublic: hasPublicFamily));
    }

    internal static string BuildDomainIntentClarificationVisibleTextForTesting(bool hasAdFamily, bool hasPublicFamily) {
        return BuildDomainIntentClarificationVisibleText(new DomainIntentFamilyAvailability(HasAd: hasAdFamily, HasPublic: hasPublicFamily));
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
}
