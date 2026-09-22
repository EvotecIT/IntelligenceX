using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>
/// Deduplicates one account/window/threshold warning while allowing another warning
/// after its previously observed reset time has passed and a new reset is reported.
/// </summary>
internal sealed class LimitNotificationWindowTracker {
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset?> _resetAt = new(StringComparer.Ordinal);

    /// <summary>Records a window and returns whether it starts a new warning generation.</summary>
    internal bool Observe(string key, DateTimeOffset? resetsAt, DateTimeOffset nowUtc) {
        if (_active.Add(key)) {
            _resetAt[key] = resetsAt;
            return true;
        }

        if (!_resetAt.TryGetValue(key, out var previous) || !previous.HasValue) {
            if (resetsAt.HasValue) _resetAt[key] = resetsAt;
            return false;
        }

        // Relative reset-after readings drift by seconds from scan to scan. Only a
        // reset already reached and a later reported reset establish a new window.
        if (previous.Value <= nowUtc && resetsAt.HasValue && resetsAt.Value > previous.Value) {
            _resetAt[key] = resetsAt;
            return true;
        }
        return false;
    }

    /// <summary>Rearms only observed healthy windows; unavailable readings cannot change their state.</summary>
    internal void RetainObserved(ISet<string> activeKeys, ISet<string> observedKeys) {
        foreach (var key in _active.Where(key => observedKeys.Contains(key) && !activeKeys.Contains(key)).ToArray()) {
            _active.Remove(key);
            _resetAt.Remove(key);
        }
    }

    internal void Clear() {
        _active.Clear();
        _resetAt.Clear();
    }
}
