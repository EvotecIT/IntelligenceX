using System.Net;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>This machine's local coordination store, which the agent service serves while the Ledger can't be reached.
/// Its client file sits beside the session's Ledger client file. New asks for this machine go there when the Ledger is
/// unreachable, and every request it holds is answered from there, so a request never exists in both places.</summary>
/// <remarks>The agent writes the file when its store starts and removes it when it stops, and a session's tools may have
/// started before either, so the file is read again whenever it changes.</remarks>
public sealed class LocalRoute {
    internal const string FileName = "local-client.json";
    public static readonly LocalRoute None = new(null);

    private readonly string? _file;
    private readonly object _gate = new();
    private (DateTime Written, long Length)? _seen;
    private Store? _store;

    private LocalRoute(string? file) => _file = file;

    /// <summary>The local store's client and machine.</summary>
    internal sealed record Store(LedgerReadClient Client, string MachineId);

    /// <summary>The local route for this coordination client file. The agent may not have written its file yet.</summary>
    public static LocalRoute Load(string coordinationConfig) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(coordinationConfig))!, FileName));

    internal static LocalRoute For(LedgerReadClient client, string machineId) => new(null) { _store = new(client, machineId) };

    /// <summary>The local store as the client file describes it now, or null while there is none.</summary>
    internal Store? Current() {
        if (_file is null) return _store;
        lock (_gate) {
            var info = new FileInfo(_file);
            (DateTime, long)? seen = info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
            if (seen == _seen) return _store;
            _seen = seen;
            _store = null;
            if (seen is null) return null;
            // A damaged local file leaves the tools on the Ledger alone; a rewrite of it is read again.
            try {
                var settings = CoordinationClientSettings.Load(_file);
                if (settings.MachineId is { Length: > 0 } machine) _store = new(LedgerReadClient.ForCoordination(settings), machine);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) {
            }
            return _store;
        }
    }

    /// <summary>The local store when it holds this request, otherwise null. Only the store's own "not found" says it
    /// doesn't: a store that can't answer may hold it, so the request must not go to the Ledger instead, and its error
    /// is the caller's result.</summary>
    internal async Task<Store?> HolderAsync(string requestId, CancellationToken token) {
        if (Current() is not { } store) return null;
        try {
            await store.Client.RequestAsync(requestId, token).ConfigureAwait(false);
            return store;
        } catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) {
            return null;
        }
    }

    /// <summary>The local client for a request the local store holds, otherwise the Ledger's.</summary>
    internal async Task<LedgerReadClient> ClientForAsync(string requestId, LedgerReadClient ledger, CancellationToken token) =>
        (await HolderAsync(requestId, token).ConfigureAwait(false))?.Client ?? ledger;
}
