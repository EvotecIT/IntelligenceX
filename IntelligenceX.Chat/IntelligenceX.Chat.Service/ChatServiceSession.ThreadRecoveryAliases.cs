using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private string ResolveRecoveredThreadAlias(string? threadId) {
        var normalized = (threadId ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        lock (_threadRecoveryAliasLock) {
            var nowUtc = DateTime.UtcNow;
            var nowTicks = nowUtc.Ticks;
            TrimRecoveredThreadAliasesNoLock(nowUtc);

            var current = normalized;
            // Defensive max hop count to avoid infinite loops in case of accidental cycles.
            for (var i = 0; i < 8; i++) {
                if (!_recoveredThreadAliasesByThreadId.TryGetValue(current, out var next)) {
                    break;
                }

                _recoveredThreadAliasSeenUtcTicksByThreadId[current] = nowTicks;
                if (string.IsNullOrWhiteSpace(next)) {
                    _recoveredThreadAliasesByThreadId.Remove(current);
                    _recoveredThreadAliasSeenUtcTicksByThreadId.Remove(current);
                    break;
                }

                next = next.Trim();
                if (next.Length == 0 || string.Equals(next, current, StringComparison.Ordinal)) {
                    _recoveredThreadAliasesByThreadId.Remove(current);
                    _recoveredThreadAliasSeenUtcTicksByThreadId.Remove(current);
                    break;
                }

                current = next;
            }

            return current;
        }
    }

    private void RememberRecoveredThreadAlias(string originalThreadId, string recoveredThreadId, long? seenUtcTicks = null) {
        var original = (originalThreadId ?? string.Empty).Trim();
        var recovered = (recoveredThreadId ?? string.Empty).Trim();
        if (original.Length == 0 || recovered.Length == 0 || string.Equals(original, recovered, StringComparison.Ordinal)) {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var normalizedTicks = seenUtcTicks ?? nowUtc.Ticks;
        if (!TryGetUtcDateTimeFromTicks(normalizedTicks, out nowUtc)) {
            nowUtc = DateTime.UtcNow;
            normalizedTicks = nowUtc.Ticks;
        } else {
            normalizedTicks = nowUtc.Ticks;
        }

        lock (_threadRecoveryAliasLock) {
            _recoveredThreadAliasesByThreadId[original] = recovered;
            _recoveredThreadAliasSeenUtcTicksByThreadId[original] = normalizedTicks;
            TrimRecoveredThreadAliasesNoLock(nowUtc);
        }
    }

    private void TrimRecoveredThreadAliasesNoLock(DateTime nowUtc) {
        var remove = new List<string>();
        foreach (var pair in _recoveredThreadAliasesByThreadId) {
            var threadId = pair.Key;
            var nextThreadId = (pair.Value ?? string.Empty).Trim();
            if (nextThreadId.Length == 0 || string.Equals(nextThreadId, threadId, StringComparison.Ordinal)) {
                remove.Add(threadId);
                continue;
            }

            if (!_recoveredThreadAliasSeenUtcTicksByThreadId.TryGetValue(threadId, out var ticks)
                || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)
                || nowUtc - seenUtc > ThreadRecoveryAliasContextMaxAge) {
                remove.Add(threadId);
            }
        }

        for (var i = 0; i < remove.Count; i++) {
            _recoveredThreadAliasesByThreadId.Remove(remove[i]);
            _recoveredThreadAliasSeenUtcTicksByThreadId.Remove(remove[i]);
        }

        var overflow = _recoveredThreadAliasesByThreadId.Count - MaxTrackedThreadRecoveryAliases;
        if (overflow <= 0) {
            return;
        }

        var oldest = _recoveredThreadAliasSeenUtcTicksByThreadId
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(overflow)
            .Select(pair => pair.Key)
            .ToArray();

        for (var i = 0; i < oldest.Length; i++) {
            _recoveredThreadAliasesByThreadId.Remove(oldest[i]);
            _recoveredThreadAliasSeenUtcTicksByThreadId.Remove(oldest[i]);
        }
    }

    private void ClearRecoveredThreadAliases() {
        lock (_threadRecoveryAliasLock) {
            _recoveredThreadAliasesByThreadId.Clear();
            _recoveredThreadAliasSeenUtcTicksByThreadId.Clear();
        }
    }

    internal string ResolveRecoveredThreadAliasForTesting(string threadId) {
        return ResolveRecoveredThreadAlias(threadId);
    }

    internal void RememberRecoveredThreadAliasForTesting(string originalThreadId, string recoveredThreadId, long? seenUtcTicks = null) {
        RememberRecoveredThreadAlias(originalThreadId, recoveredThreadId, seenUtcTicks);
    }
}
