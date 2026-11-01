using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Agents;

// Source retries publish transitions, never an identical failure back into the collection queue.
internal sealed class AgentWorkDiagnostics(Action changed) {
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentSourceDiagnostic> _items = new(StringComparer.Ordinal);

    internal AgentSourceDiagnostic[] Snapshot { get { lock (_gate) return _items.Values.ToArray(); } }

    internal void Set(string provider, string? code, string? sourceRootId = null) {
        var key = sourceRootId is null ? provider : provider + ":" + sourceRootId;
        lock (_gate) {
            if (code is null) { if (!_items.Remove(key)) return; }
            else {
                if (_items.TryGetValue(key, out var previous) && previous.Code == code) return;
                _items[key] = new AgentSourceDiagnostic { Provider = provider, Code = code };
            }
        }
        changed();
    }
}
