using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

// Cache only parser observations. Clock aging, title indexes, Desktop metadata, Git and PRs are reapplied each scan.
internal sealed class AgentSessionScan {
    private const string ParserVersion = "2";
    private readonly SourceRootRecord _root;
    private readonly Action<string> _report;
    private readonly IRawArtifactStore? _store;
    private readonly IReadOnlyDictionary<string, RawArtifactDescriptor>? _cached;
    private bool _cacheFailed;
    internal int FilesRead, FilesReused, FilesSkipped;
    internal long BytesRead;

    internal AgentSessionScan(SourceRootRecord root, IRawArtifactStore? store, Action<string> report) {
        _root = root;
        _store = store;
        _report = report;
        try { _cached = store?.GetBySourceRootAdapter(root.Id, Adapter); }
        catch (Exception) { CacheFailed(); }
    }

    private string Adapter => "agent-work/" + _root.ProviderId;

    internal AgentSessionState? Read(string path, DateTimeOffset? since, HashSet<string> recentMetadataIds, CancellationToken cancellationToken) {
        var before = RawArtifactDescriptor.CreateFile(_root.Id, Adapter, path, ParserVersion);
        AgentSessionCacheEntry? result = null;
        if (_cached is not null && _cached.TryGetValue(before.Path, out var cached)
            && cached.Fingerprint == before.Fingerprint && cached.ParserVersion == ParserVersion && cached.StateJson is not null) {
            try {
                result = JsonSerializer.Deserialize(cached.StateJson, AgentSessionCacheJsonContext.Default.AgentSessionCacheEntry);
            } catch (JsonException) { /* A damaged cache is disposable; reread its source. */ }
        }
        // Names are not canonical identity: recovered/renamed files can contain another session ID.
        // When Desktop knows recent sessions, first establish identity from a verified cache or source.
        var canSkip = recentMetadataIds.Count == 0 || (result?.Session is not null && !recentMetadataIds.Contains(result.Session.Id));
        if (since.HasValue && before.LastWriteTimeUtc < since && canSkip) {
            FilesSkipped++;
            return null;
        }
        if (result is not null) {
            FilesReused++;
            foreach (var diagnostic in result.Diagnostics)
                for (var i = 0; i < diagnostic.Value; i++) _report(diagnostic.Key);
            return result.Session;
        }
        var diagnostics = new Dictionary<string, int>(StringComparer.Ordinal);
        void Report(string code) { diagnostics.TryGetValue(code, out var count); diagnostics[code] = count + 1; _report(code); }
        FilesRead++;
        BytesRead += before.SizeBytes ?? 0;
        var session = _root.ProviderId == "codex"
            ? CodexAgentSessionReader.Read(path, _root, Report, cancellationToken)
            : ClaudeAgentSessionReader.Read(path, _root, Report, cancellationToken);
        if (_store is not null && !_cacheFailed) {
            // Never stamp a partially observed, growing file with a newer fingerprint.
            var after = RawArtifactDescriptor.CreateFile(_root.Id, Adapter, path, ParserVersion);
            if (after.Fingerprint == before.Fingerprint) {
                before.StateJson = JsonSerializer.Serialize(new AgentSessionCacheEntry { Session = session, Diagnostics = diagnostics },
                    AgentSessionCacheJsonContext.Default.AgentSessionCacheEntry);
                try { _store.Upsert(before); }
                catch (Exception) { CacheFailed(); }
            }
        }
        return session;
    }

    private void CacheFailed() {
        if (!_cacheFailed) _report("cache-unavailable");
        _cacheFailed = true;
    }
}

internal sealed class AgentSessionCacheEntry {
    public AgentSessionState? Session { get; init; }
    public Dictionary<string, int> Diagnostics { get; init; } = new();
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentSessionCacheEntry))]
internal partial class AgentSessionCacheJsonContext : JsonSerializerContext { }
