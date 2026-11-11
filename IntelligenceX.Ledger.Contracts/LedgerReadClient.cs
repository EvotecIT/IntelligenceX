using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Authenticated read transport with bounded responses and no redirect or proxy credential forwarding.</summary>
public sealed partial class LedgerReadClient : IDisposable {
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;

    /// <summary>Creates a reader for the explicitly configured local service.</summary>
    public LedgerReadClient(LedgerClientSettings settings) {
        settings.Validate();
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        if (settings.CertificateSha256 is { } fingerprint) {
            var expected = Convert.FromHexString(fingerprint);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.RawData), expected);
        }
        _client = new HttpClient(handler) { BaseAddress = new Uri(settings.Endpoint), Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken);
    }

    /// <summary>Reads a current-work projection whose verdicts were evaluated by the service.</summary>
    public Task<LedgerWorkView> WorkAsync(CancellationToken token = default, string? verdict = null, int limit = 200) =>
        GetAsync("api/ledger/v1/work?limit=" + limit + (verdict is null ? "" : "&verdict=" + Uri.EscapeDataString(verdict)),
            LedgerReadJson.Default.LedgerWorkView, token);

    /// <summary>Reads one complete evidence-backed item.</summary>
    public Task<LedgerItemView> ItemAsync(string id, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/items/" + Uri.EscapeDataString(id), LedgerReadJson.Default.LedgerItemView, token);

    /// <summary>Reads the explicitly enrolled project directory.</summary>
    public Task<ProjectDirectoryView> ProjectsAsync(CancellationToken token = default) =>
        GetAsync("api/coordination/v1/projects", LedgerReadJson.Default.ProjectDirectoryView, token);

    /// <summary>Obtains compact project context and optional relevant knowledge.</summary>
    public Task<ProjectContextView> ProjectAsync(string project, string? query = null, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/projects/" + Uri.EscapeDataString(project) +
            (query is null ? "" : "?query=" + Uri.EscapeDataString(query)), LedgerReadJson.Default.ProjectContextView, token);

    /// <summary>Searches maintained sources within one enrolled project.</summary>
    public Task<KnowledgeSearchView> SearchAsync(string project, string query, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/knowledge?project=" + Uri.EscapeDataString(project) + "&query=" +
            Uri.EscapeDataString(query), LedgerReadJson.Default.KnowledgeSearchView, token);

    private async Task<T> GetAsync<T>(string route, JsonTypeInfo<T> type, CancellationToken token) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await ReadAsync(route, type, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested && deadline.IsCancellationRequested) {
            throw new TaskCanceledException("Ledger read request timed out.", error, deadline.Token);
        }
    }

    private async Task<T> ReadAsync<T>(string route, JsonTypeInfo<T> type, CancellationToken token) {
        using var response = await _client.GetAsync(route, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        return await ReadResponseAsync(response, type, token).ConfigureAwait(false);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken token) {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("Ledger response exceeds its bound.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0) {
            if (output.Length + count > MaximumResponseBytes) throw new InvalidDataException("Ledger response exceeds its bound.");
            output.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize(output.ToArray(), type) ?? throw new InvalidDataException("Ledger response is missing.");
    }

    /// <summary>Releases the owned transport.</summary>
    public void Dispose() => _client.Dispose();
}
