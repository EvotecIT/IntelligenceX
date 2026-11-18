using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    private string? _leaseWriteToken;

    /// <summary>Enables only the explicit lease mutation methods with separate write credentials.</summary>
    public static LedgerReadClient ForLeases(LeaseClientSettings settings) => new(settings.ReadSettings()) { _leaseWriteToken = settings.WriteToken };
    public Task<LeaseQuery> WhoAsync(string key, bool prefix = false, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/leases?key=" + Uri.EscapeDataString(key) + "&prefix=" + prefix.ToString().ToLowerInvariant(), LedgerReadJson.Default.LeaseQuery, token);
    public Task<MachineStatus> MachineStatusAsync(string id, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/machine-status/" + Uri.EscapeDataString(id), LedgerReadJson.Default.MachineStatus, token);
    public Task<BudgetStatus> BudgetStatusAsync(CancellationToken token = default) =>
        GetAsync("api/ledger/v1/budget-status", LedgerReadJson.Default.BudgetStatus, token);
    public Task<SessionStatus> SessionStatusAsync(string id, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/session-status?id=" + Uri.EscapeDataString(id), LedgerReadJson.Default.SessionStatus, token);
    public Task<PullRequestReadiness> PullRequestReadinessAsync(string repo, int pr, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/pr-ready?repo=" + Uri.EscapeDataString(repo) + "&pr=" + pr, LedgerReadJson.Default.PullRequestReadiness, token);
    public Task<SessionCleanupPlan> CleanupPlanAsync(string? machine = null, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/cleanup" + (machine is null ? "" : "?machine=" + Uri.EscapeDataString(machine)), LedgerReadJson.Default.SessionCleanupPlan, token);
    public Task<LeaseReply> AcquireLeaseAsync(LeaseAcquire value, CancellationToken token = default) =>
        LeaseCommandAsync("acquire", value, LedgerReadJson.Default.LeaseAcquire, token);
    public Task<LeaseReply> RenewLeaseAsync(LeaseRenew value, CancellationToken token = default) =>
        LeaseCommandAsync("renew", value, LedgerReadJson.Default.LeaseRenew, token);
    public Task<LeaseReply> ReleaseLeaseAsync(LeaseRelease value, CancellationToken token = default) =>
        LeaseCommandAsync("release", value, LedgerReadJson.Default.LeaseRelease, token);

    private Task<LeaseReply> LeaseCommandAsync<T>(string action, T value, JsonTypeInfo<T> type, CancellationToken token) =>
        WriteAsync("api/ledger/v1/leases/" + action, value, type, LedgerReadJson.Default.LeaseReply, token);

    /// <summary>Reads the tree a session belongs to: its root, every descendant, their state and an archive plan.</summary>
    public Task<SessionTree> SessionTreeAsync(string id, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/session-tree?id=" + Uri.EscapeDataString(id), LedgerReadJson.Default.SessionTree, token);
    /// <summary>Reads a tree's history across sessions, asks, reservations, branches and pull requests.</summary>
    public Task<SessionTrail> SessionTrailAsync(string id, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/session-trail?id=" + Uri.EscapeDataString(id), LedgerReadJson.Default.SessionTrail, token);
    /// <summary>Takes over a stopped session's reservations and work; uses the lease write credentials.</summary>
    public Task<SessionTakeoverReply> TakeOverSessionAsync(SessionTakeoverCommand value, CancellationToken token = default) =>
        WriteAsync("api/ledger/v1/session-takeover", value, LedgerReadJson.Default.SessionTakeoverCommand, LedgerReadJson.Default.SessionTakeoverReply, token);

    private async Task<TResult> WriteAsync<T, TResult>(string route, T value, JsonTypeInfo<T> type, JsonTypeInfo<TResult> result, CancellationToken token) {
        if (_leaseWriteToken is null) throw new InvalidOperationException("Lease mutation requires explicit write configuration.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _leaseWriteToken);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, type));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        return await ReadResponseAsync(response, result, deadline.Token).ConfigureAwait(false);
    }
}
