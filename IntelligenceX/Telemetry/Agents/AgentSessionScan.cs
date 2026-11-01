using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

// Cache only parser observations. Clock aging, title indexes, Desktop metadata, Git and PRs are reapplied each scan.
internal sealed class AgentSessionScan {
    private const string ParserVersion = "3";
    private readonly SourceRootRecord _root;
    private readonly Action<string> _report;
    private readonly IRawArtifactStore? _store;
    private readonly IReadOnlyDictionary<string, RawArtifactDescriptor>? _cached;
    private bool _cacheFailed;
    internal int FilesRead, FilesReused, FilesSkipped, FilesTailed;
    internal long BytesRead;

    internal AgentSessionScan(SourceRootRecord root, IRawArtifactStore? store, Action<string> report) {
        _root = root;
        _store = store;
        _report = report;
        try { _cached = store?.GetBySourceRootAdapter(root.Id, Adapter); }
        catch (Exception) { CacheFailed(); }
    }

    private string Adapter => "agent-work/" + _root.ProviderId;

    internal AgentSessionState? Read(string path, DateTimeOffset? since, CancellationToken cancellationToken) {
        var before = RawArtifactDescriptor.CreateFile(_root.Id, Adapter, path, ParserVersion);
        // A live strip never opens an old transcript, even to recover canonical identity.
        if (since.HasValue && before.LastWriteTimeUtc < since) {
            FilesSkipped++;
            return null;
        }
        AgentSessionCacheEntry? cached = null;
        RawArtifactDescriptor? descriptor = null;
        if (_cached is not null && _cached.TryGetValue(before.Path, out descriptor)
            && descriptor.ParserVersion == ParserVersion && descriptor.StateJson is not null) {
            try { cached = JsonSerializer.Deserialize(descriptor.StateJson, AgentSessionCacheJsonContext.Default.AgentSessionCacheEntry); }
            catch (JsonException) { /* Cache damage falls back to source. */ }
        }
        if (cached is not null && descriptor!.Fingerprint == before.Fingerprint) {
            FilesReused++;
            Replay(cached.Diagnostics);
            return cached.Session;
        }
        var created = File.GetCreationTimeUtc(path);
        var tail = cached is not null && cached.TailSafe && cached.Offset == descriptor!.SizeBytes
            && before.SizeBytes > cached.Offset && cached.CreatedAtUtc == created
            && cached.AnchorHash == AnchorHash(path, cached.Offset);
        var diagnostics = tail ? new Dictionary<string, int>(cached!.Diagnostics, StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        if (tail) Replay(diagnostics);
        void Report(string code) { diagnostics.TryGetValue(code, out var count); diagnostics[code] = count + 1; _report(code); }
        var window = new AgentSessionReadWindow { StartOffset = tail ? cached!.Offset : 0 };
        FilesRead++;
        if (tail) FilesTailed++;
        var state = _root.ProviderId == "codex"
            ? CodexAgentSessionReader.Read(path, _root, Report, cancellationToken, tail ? cached!.State : null, window)
            : ClaudeAgentSessionReader.Read(path, _root, Report, cancellationToken, tail ? cached!.State : null, window);
        BytesRead += window.EndOffset - window.StartOffset;
        var result = new AgentSessionCacheEntry { State = state, Diagnostics = diagnostics,
            Offset = window.EndOffset, TailSafe = window.TailSafe, CreatedAtUtc = created };
        if (_store is not null && !_cacheFailed) {
            var after = RawArtifactDescriptor.CreateFile(_root.Id, Adapter, path, ParserVersion);
            if (after.Fingerprint == before.Fingerprint) {
                result.AnchorHash = AnchorHash(path, window.EndOffset);
                before.StateJson = JsonSerializer.Serialize(result, AgentSessionCacheJsonContext.Default.AgentSessionCacheEntry);
                try { _store.Upsert(before); }
                catch (Exception) { CacheFailed(); }
            }
        }
        return result.Session;
    }

    private void Replay(Dictionary<string, int> diagnostics) {
        foreach (var diagnostic in diagnostics)
            for (var i = 0; i < diagnostic.Value; i++) _report(diagnostic.Key);
    }

    // Verify the old prefix and append boundary without retaining text. Rewrites/truncation fall back to a full parse.
    private static string AnchorHash(string path, long length) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var count = (int)Math.Min(256, length);
        var buffer = new byte[count * 2];
        ReadExactly(stream, buffer, 0, count);
        stream.Position = length - count;
        ReadExactly(stream, buffer, count, count);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(buffer)).Replace("-", string.Empty);
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count) {
        while (count > 0) {
            var read = stream.Read(buffer, offset, count);
            if (read == 0) throw new IOException("Session changed during checkpoint verification.");
            offset += read;
            count -= read;
        }
    }

    private void CacheFailed() {
        if (!_cacheFailed) _report("cache-unavailable");
        _cacheFailed = true;
    }
}

internal sealed class AgentSessionCacheEntry {
    public AgentSessionParserState State { get; init; } = new();
    [JsonIgnore]
    public AgentSessionState? Session => State.Sidechain ? null : AgentSessionLogReader.Finish(State.Session, State.Active);
    public Dictionary<string, int> Diagnostics { get; init; } = new();
    public long Offset { get; init; }
    public bool TailSafe { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? AnchorHash { get; set; }
}

internal sealed class AgentSessionParserState {
    public AgentSessionState Session { get; init; } = new();
    public bool Active { get; init; }
    public bool HasSessionMetadata { get; init; }
    public bool Sidechain { get; init; }
    public string? InputCallId { get; init; }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentSessionCacheEntry))]
internal partial class AgentSessionCacheJsonContext : JsonSerializerContext { }
