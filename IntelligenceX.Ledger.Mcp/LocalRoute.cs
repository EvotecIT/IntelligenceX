using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>This machine's local coordination store, which the agent service serves while the Ledger can't be reached.
/// Its client file sits beside the session's Ledger client file. New asks for this machine go there when the Ledger is
/// unreachable, and every request it holds is answered from there, so a request never exists in both places.</summary>
public sealed class LocalRoute {
    internal const string FileName = "local-client.json";
    public static readonly LocalRoute None = new(null, null);

    private LocalRoute(LedgerReadClient? client, string? machineId) {
        Client = client;
        MachineId = machineId;
    }

    internal LedgerReadClient? Client { get; }
    internal string? MachineId { get; }
    internal bool Available => Client is not null;

    /// <summary>The local route for this coordination client file, or <see cref="None"/> when the agent hasn't written one.</summary>
    public static LocalRoute Load(string coordinationConfig) {
        var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(coordinationConfig))!, FileName);
        if (!File.Exists(file)) return None;
        // A damaged local file must never keep the Ledger tools from starting.
        try {
            var settings = CoordinationClientSettings.Load(file);
            return settings.MachineId is { Length: > 0 } machine ? new(LedgerReadClient.ForCoordination(settings), machine) : None;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) {
            return None;
        }
    }

    internal static LocalRoute For(LedgerReadClient client, string machineId) => new(client, machineId);

    /// <summary>True when the local store holds this request. A store that isn't running holds nothing.</summary>
    internal async Task<bool> HoldsAsync(string requestId, CancellationToken token) {
        if (Client is null) return false;
        try {
            await Client.RequestAsync(requestId, token).ConfigureAwait(false);
            return true;
        } catch (HttpRequestException) {
            return false;
        } catch (TaskCanceledException) when (!token.IsCancellationRequested) {
            return false;
        }
    }

    /// <summary>The local client for a request the local store holds, otherwise the Ledger's.</summary>
    internal async Task<LedgerReadClient> ClientForAsync(string requestId, LedgerReadClient ledger, CancellationToken token) =>
        await HoldsAsync(requestId, token).ConfigureAwait(false) ? Client! : ledger;
}
