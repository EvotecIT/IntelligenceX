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
    public Task<LeaseReply> AcquireLeaseAsync(LeaseAcquire value, CancellationToken token = default) =>
        LeaseCommandAsync("acquire", value, LedgerReadJson.Default.LeaseAcquire, token);
    public Task<LeaseReply> RenewLeaseAsync(LeaseRenew value, CancellationToken token = default) =>
        LeaseCommandAsync("renew", value, LedgerReadJson.Default.LeaseRenew, token);
    public Task<LeaseReply> ReleaseLeaseAsync(LeaseRelease value, CancellationToken token = default) =>
        LeaseCommandAsync("release", value, LedgerReadJson.Default.LeaseRelease, token);

    private async Task<LeaseReply> LeaseCommandAsync<T>(string action, T value, JsonTypeInfo<T> type, CancellationToken token) {
        if (_leaseWriteToken is null) throw new InvalidOperationException("Lease mutation requires explicit write configuration.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ledger/v1/leases/" + action);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _leaseWriteToken);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, type));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        return await ReadResponseAsync(response, LedgerReadJson.Default.LeaseReply, deadline.Token).ConfigureAwait(false);
    }
}
