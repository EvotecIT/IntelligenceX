using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    /// <summary>Uses the same bounded transport with a separately enrolled coordination principal.</summary>
    public static LedgerReadClient ForCoordination(CoordinationClientSettings settings) => new(settings.TransportSettings());
    public Task<CoordinationSummary> CoordinationStatusAsync(string project, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/summary?project=" + Uri.EscapeDataString(project), LedgerReadJson.Default.CoordinationSummary, token);
    public Task<CoordinationRequestView> RequestAsync(string id, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/requests/" + Uri.EscapeDataString(id), LedgerReadJson.Default.CoordinationRequestView, token);
    public Task<CoordinationWorkPage> AssignedAsync(CancellationToken token = default, long afterOrder = 0) =>
        GetAsync("api/coordination/v1/requests?afterOrder=" + afterOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LedgerReadJson.Default.CoordinationWorkPage, token);
    public Task<CoordinationChanges> ChangesAsync(long after, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/changes?after=" + after, LedgerReadJson.Default.CoordinationChanges, token);
    public Task<CoordinationGoal> GoalAsync(string id, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/goals/" + Uri.EscapeDataString(id), LedgerReadJson.Default.CoordinationGoal, token);
    public Task<CoordinationPolicy> PolicyAsync(CancellationToken token = default) =>
        GetAsync("api/coordination/v1/policy", LedgerReadJson.Default.CoordinationPolicy, token);
    public Task<CoordinationGoal> SetGoalAsync(CoordinationGoalCommand command, CancellationToken token = default) =>
        PostAsync("goals", command, LedgerReadJson.Default.CoordinationGoalCommand, LedgerReadJson.Default.CoordinationGoal, token);
    public Task<CoordinationPolicy> SetPolicyAsync(CoordinationPolicyCommand command, CancellationToken token = default) =>
        PostAsync("policy", command, LedgerReadJson.Default.CoordinationPolicyCommand, LedgerReadJson.Default.CoordinationPolicy, token);
    public Task<CoordinationWork> SubmitAsync(CoordinationRequest request, bool apply = false, CancellationToken token = default) =>
        PostAsync(apply ? "requests" : "requests/preview", request, LedgerReadJson.Default.CoordinationRequest, LedgerReadJson.Default.CoordinationWork, token);
    public Task<CoordinationWork> AcceptAsync(string id, string resultRef, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/accept", new(resultRef), LedgerReadJson.Default.CoordinationAcceptance, LedgerReadJson.Default.CoordinationWork, token);
    public Task<CoordinationWork> ReceiptAsync(string id, string stage, ExecutionReceipt receipt, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/receipts/" + Uri.EscapeDataString(stage), receipt,
            LedgerReadJson.Default.ExecutionReceipt, LedgerReadJson.Default.CoordinationWork, token);
    public Task<CoordinationAcknowledgement> HostAsync(CoordinationHost host, CancellationToken token = default) =>
        PostAsync("hosts", host, LedgerReadJson.Default.CoordinationHost, LedgerReadJson.Default.CoordinationAcknowledgement, token);
    public Task<CoordinationAcknowledgement> WorkerAsync(CoordinationWorker worker, CancellationToken token = default) =>
        PostAsync("workers", worker, LedgerReadJson.Default.CoordinationWorker, LedgerReadJson.Default.CoordinationAcknowledgement, token);

    private async Task<TResult> PostAsync<T, TResult>(string route, T value, JsonTypeInfo<T> inputType,
        JsonTypeInfo<TResult> outputType, CancellationToken token) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, inputType);
        if (bytes.Length > 65536) throw new InvalidDataException("Coordination command exceeds its bound.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/coordination/v1/" + route) { Content = content };
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            await ThrowCommandRejectionAsync(response, deadline.Token).ConfigureAwait(false);
        return await ReadResponseAsync(response, outputType, deadline.Token).ConfigureAwait(false);
    }

    private static async Task ThrowCommandRejectionAsync(HttpResponseMessage response, CancellationToken token) {
        const int maximumErrorBytes = 4096;
        if (response.Content.Headers.ContentLength > maximumErrorBytes) return;
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[maximumErrorBytes + 1];
        var length = 0;
        while (length < bytes.Length) {
            var count = await input.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
            if (count == 0) break;
            length += count;
        }
        if (length > maximumErrorBytes) return;
        string? code;
        try {
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 1 || properties[0].Name != "error" ||
                properties[0].Value.ValueKind != JsonValueKind.String) return;
            code = properties[0].Value.GetString();
        } catch (JsonException) { return; }
        if (code is not { Length: > 0 and <= 128 } ||
            code.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))) return;
        throw new CoordinationCommandRejectedException(code);
    }
}

public sealed record CoordinationAcknowledgement(bool Acknowledged);
