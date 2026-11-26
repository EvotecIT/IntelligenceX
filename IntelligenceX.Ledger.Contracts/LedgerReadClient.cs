using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Authenticated read transport with bounded responses and no redirect or proxy credential forwarding.</summary>
public sealed partial class LedgerReadClient : IDisposable {
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    // A host that is off often drops connection attempts silently, so the connect step gets its own short limit and
    // fails as unreachable instead of running into the whole request's timeout.
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private readonly string _endpoint;
    private readonly string? _fallback;
    private readonly HttpClient _client;
    // Commands never reuse a connection. A request on a reused connection that drops is retried on a new one, so a
    // connect failure on that retry would hide a first attempt the service may have received.
    private readonly HttpClient _commands;
    // Image uploads and downloads move up to 8 MB, so they get their own client with a longer timeout; every other
    // request keeps the short one.
    private readonly HttpClient _transfers;

    /// <summary>Creates a reader for the explicitly configured local service.</summary>
    public LedgerReadClient(LedgerClientSettings settings) {
        settings.Validate();
        _endpoint = settings.Endpoint;
        _fallback = settings.Fallback;
        _client = Create(settings, TimeSpan.FromSeconds(15), pooled: true);
        _commands = Create(settings, TimeSpan.FromSeconds(15), pooled: false);
        _transfers = Create(settings, TimeSpan.FromMinutes(2), pooled: false);
    }

    private static HttpClient Create(LedgerClientSettings settings, TimeSpan timeout, bool pooled) {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = ConnectAsync };
        if (!pooled) handler.PooledConnectionIdleTimeout = TimeSpan.Zero;
        if (settings.CertificateSha256 is { } fingerprint) {
            var expected = Convert.FromHexString(fingerprint);
            handler.SslOptions = new SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null &&
                    CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), expected)
            };
        }
        var client = new HttpClient(handler) { BaseAddress = new Uri(settings.Endpoint), Timeout = timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken);
        return client;
    }

    /// <summary>Opens the TCP connection within <see cref="ConnectTimeout"/>. Running out of time is reported as a socket
    /// timeout, so it reads as a connect failure rather than as the request timing out after it was sent.</summary>
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token) {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ConnectTimeout);
        try {
            await socket.ConnectAsync(context.DnsEndPoint, deadline.Token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            socket.Dispose();
            throw new SocketException((int)SocketError.TimedOut);
        } catch {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Sends one request and reports a failure to connect as <see cref="LedgerUnreachableException"/>.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage message, CancellationToken token) {
        try { return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
        catch (HttpRequestException error) when (LedgerUnreachableException.From(error, _endpoint, _fallback) is { } unreachable) { throw unreachable; }
    }

    /// <summary>Reads a current-work projection whose verdicts were evaluated by the service.</summary>
    public Task<LedgerWorkView> WorkAsync(CancellationToken token = default, string? verdict = null, int limit = 200) =>
        WorkAsync(token, verdict, limit, includeOpen: false);

    /// <summary>Reads current work and optionally the recent open-session roster.</summary>
    public Task<LedgerWorkView> WorkAsync(CancellationToken token, string? verdict, int limit, bool includeOpen) =>
        GetAsync("api/ledger/v1/work?limit=" + limit + (verdict is null ? "" : "&verdict=" + Uri.EscapeDataString(verdict)) +
            (includeOpen ? "&include=open" : ""),
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

    private async Task<T> GetAsync<T>(string route, JsonTypeInfo<T> type, CancellationToken token, bool acceptUnavailable = false) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await ReadAsync(route, type, deadline.Token, acceptUnavailable).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested && deadline.IsCancellationRequested) {
            throw new TaskCanceledException("Ledger read request timed out.", error, deadline.Token);
        }
    }

    private async Task<T> ReadAsync<T>(string route, JsonTypeInfo<T> type, CancellationToken token, bool acceptUnavailable = false) {
        using var message = new HttpRequestMessage(HttpMethod.Get, route);
        using var response = await SendAsync(_client, message, token).ConfigureAwait(false);
        return await ReadResponseAsync(response, type, token, acceptUnavailable).ConfigureAwait(false);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken token,
        bool acceptUnavailable = false) {
        // A health probe answers 503 with its reason in the body.
        if (!(acceptUnavailable && response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)) response.EnsureSuccessStatusCode();
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
    public void Dispose() {
        _client.Dispose();
        _commands.Dispose();
        _transfers.Dispose();
    }
}
