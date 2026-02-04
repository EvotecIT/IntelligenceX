using System;
using System.Collections.Generic;
using System.Threading;

namespace IntelligenceX.Reviewer;

internal static class SecretsAudit {
    private static readonly object Sync = new();
    private static readonly List<string> Pending = new();
    private static readonly AsyncLocal<SecretsAuditSession?> Current = new();
    private static volatile bool Enabled = true;

    public static SecretsAuditSession? TryStart(ReviewSettings settings) {
        Enabled = settings.SecretsAudit;
        if (!settings.SecretsAudit) {
            lock (Sync) {
                Pending.Clear();
            }
            Current.Value = null;
            return null;
        }

        var session = new SecretsAuditSession();
        Current.Value = session;
        lock (Sync) {
            if (Pending.Count > 0) {
                session.AddPending(Pending);
                Pending.Clear();
            }
        }
        return session;
    }

    public static void Record(string message) {
        if (!Enabled) {
            return;
        }
        if (string.IsNullOrWhiteSpace(message)) {
            return;
        }
        var session = Current.Value;
        if (session is not null) {
            session.Record(message);
            return;
        }
        lock (Sync) {
            Pending.Add(message.Trim());
        }
    }

    internal static void EndSession(SecretsAuditSession session) {
        if (Current.Value == session) {
            Current.Value = null;
        }
    }
}

internal sealed class SecretsAuditSession : IDisposable {
    private readonly HashSet<string> _dedupe = new(StringComparer.Ordinal);
    private readonly List<string> _entries = new();
    private readonly object _lock = new();
    private bool _disposed;

    public IReadOnlyList<string> Entries {
        get {
            lock (_lock) {
                return _entries.ToArray();
            }
        }
    }

    public void WriteSummary() {
        List<string> snapshot;
        lock (_lock) {
            if (_entries.Count == 0) {
                return;
            }
            snapshot = new List<string>(_entries);
        }

        Console.WriteLine("Secrets audit:");
        foreach (var entry in snapshot) {
            Console.WriteLine($"- {entry}");
        }
    }

    internal void AddPending(IEnumerable<string> entries) {
        foreach (var entry in entries) {
            Record(entry);
        }
    }

    internal void Record(string message) {
        var trimmed = message.Trim();
        if (trimmed.Length == 0) {
            return;
        }
        lock (_lock) {
            if (_dedupe.Add(trimmed)) {
                _entries.Add(trimmed);
            }
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        SecretsAudit.EndSession(this);
    }
}
