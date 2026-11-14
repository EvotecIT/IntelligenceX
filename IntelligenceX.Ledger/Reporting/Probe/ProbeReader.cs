using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting.Probe;

/// <summary>Optional loopback Probe reader with private, durable owner-approved pairing.</summary>
internal sealed class ProbeReader {
    private const int MaximumBodyBytes = 128 * 1024;
    private const int MaximumVolumes = 8;
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private readonly HttpClient _client;
    private readonly Uri? _endpoint;
    private readonly string _directory;
    private readonly string _reader;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Uses a caller-owned, dedicated client with redirects and proxies disabled and no default authorization.
    /// The state directory must inherit private permissions on Windows and be outside the reporter spool.
    /// </summary>
    public ProbeReader(HttpClient client, string stateDirectory, string readerName, string? endpoint = null, TimeProvider? timeProvider = null) {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(readerName);
        _client = client;
        _directory = Path.GetFullPath(stateDirectory);
        _reader = readerName;
        _time = timeProvider ?? TimeProvider.System;
        var address = endpoint ?? "http://127.0.0.1:47820";
        if (Uri.TryCreate(address, UriKind.Absolute, out var origin) && origin.IsLoopback &&
            origin.Scheme is "http" or "https" && origin.UserInfo.Length == 0 &&
            origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0)
            _endpoint = origin;
    }

    /// <summary>
    /// Reads raw machine facts within a two-second deadline. Optional-source failures produce unavailable evidence.
    /// Denial, expiry and revoked authorization require a manual reset of the private reader-state.json.
    /// </summary>
    public async Task<ProbeAvailability> ReadAsync(CancellationToken cancellationToken = default) {
        using var timeout = new CancellationTokenSource(ReadDeadline, _time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var entered = false;
        try {
            if (_endpoint is null) return Unavailable("invalid-endpoint");
            if (_client.DefaultRequestHeaders.Authorization is not null) return Unavailable("invalid-client");
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            EnsurePlainPath(Path.Combine(_directory, "reader-state.json"));
            EnsurePlainPath(Path.Combine(_directory, "reader.lock"));
            PrepareDirectory();
            // An exclusive file handle serializes pairing across reader instances and reporter processes.
            await using var stateLock = OpenPrivate(Path.Combine(_directory, "reader.lock"), FileMode.OpenOrCreate);
            var state = await LoadStateAsync(deadline.Token).ConfigureAwait(false);
            if (state is not null && (state.Endpoint != _endpoint.AbsoluteUri || state.Reader != _reader))
                return Unavailable("state-mismatch");
            if (state is not null && state.State is not ("pending" or "allowed"))
                return Unavailable(state.State is "denied" or "expired" or "unauthorized" ? state.State : "pairing-interrupted");

            if (state?.State != "allowed") {
                var info = await SendAsync<ProbeInfo>(HttpMethod.Get, "v1/info", null, null, deadline.Token).ConfigureAwait(false);
                if (info.Version != 1) return Unavailable("unsupported-version");
                if (state is null) {
                    // Persist before the one-time POST so a lost response cannot create pairing spam.
                    state = new(_endpoint.AbsoluteUri, _reader, "pairing-started", null, null);
                    await SaveStateAsync(state, deadline.Token).ConfigureAwait(false);
                    var ticket = await SendAsync<ProbePairTicket>(HttpMethod.Post, "v1/pair", null,
                        new ProbePairRequest(_reader), deadline.Token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(ticket.Id) || ticket.Id.Length > 256)
                        return Unavailable("invalid-response");
                    state = state with { State = "pending", Ticket = ticket.Id };
                    await SaveStateAsync(state, deadline.Token).ConfigureAwait(false);
                }
                if (string.IsNullOrWhiteSpace(state.Ticket) || state.Ticket.Length > 256)
                    return Unavailable("invalid-state");
                var pairing = await SendAsync<ProbePairState>(HttpMethod.Get, "v1/pair/" + Uri.EscapeDataString(state.Ticket),
                    null, null, deadline.Token).ConfigureAwait(false);
                if (pairing.State == "pending") return Unavailable("pairing-pending");
                if (pairing.State is "denied" or "expired") {
                    await SaveStateAsync(state with { State = pairing.State, Token = null }, deadline.Token).ConfigureAwait(false);
                    return Unavailable(pairing.State);
                }
                if (pairing.State != "allowed" || string.IsNullOrWhiteSpace(pairing.Token) ||
                    pairing.Token.Length > 4096 || pairing.Token.Any(char.IsWhiteSpace) || pairing.Token.Any(char.IsControl))
                    return Unavailable("invalid-response");
                state = state with { State = "allowed", Token = pairing.Token };
                await SaveStateAsync(state, deadline.Token).ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(state.Token) || state.Token.Length > 4096 ||
                state.Token.Any(char.IsWhiteSpace) || state.Token.Any(char.IsControl))
                return Unavailable("invalid-state");
            ProbeSnapshot snapshot;
            try {
                snapshot = await SendAsync<ProbeSnapshot>(HttpMethod.Get, "v1/snapshot", state.Token, null, deadline.Token)
                    .ConfigureAwait(false);
            } catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
                await SaveStateAsync(state with { State = "unauthorized", Token = null }, deadline.Token).ConfigureAwait(false);
                return Unavailable("unauthorized");
            }
            if (snapshot.Version != 1) return Unavailable("unsupported-version");
            if (snapshot.At == default || snapshot.Volumes?.Any(v => v is null || v.Mount is null) == true)
                return Unavailable("invalid-response");
            if (snapshot.Paused) return new("paused", null) {
                SampledAtUtc = snapshot.At, ObservedAtUtc = DateTimeOffset.UtcNow, PausedUntilUtc = snapshot.PausedUntil
            };
            if (snapshot.Cpu?.Use is { } cpu && !double.IsFinite(cpu) ||
                snapshot.Cpu?.LoadAverage?.Any(value => !double.IsFinite(value)) == true)
                return Unavailable("invalid-response");
            return new("available", null) {
                SampledAtUtc = snapshot.At,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                CpuUse = snapshot.Cpu?.Use,
                LoadAverage = snapshot.Cpu?.LoadAverage?.ToArray(),
                MemoryPressure = snapshot.Memory?.Pressure,
                MemoryUsedBytes = snapshot.Memory?.Used,
                MemoryTotalBytes = snapshot.Memory?.Total,
                SwapUsedBytes = snapshot.Memory?.SwapUsed,
                Volumes = snapshot.Volumes?.Take(MaximumVolumes).Select(v => new ProbeVolumeView(v.Mount, v.Free, v.Total)).ToArray(),
                VolumesCapped = snapshot.Volumes?.Count > MaximumVolumes,
                RunnerBusy = snapshot.Activity?.RunnerBusy,
                RunnerListening = snapshot.Activity?.RunnerListening
            };
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return Unavailable("timeout");
        } catch (HttpRequestException error) {
            return Unavailable(error.StatusCode is { } status ? "http-" + (int)status : "transport-unavailable");
        } catch (JsonException) {
            return Unavailable("invalid-response");
        } catch (InvalidDataException) {
            return Unavailable("invalid-data");
        } catch (IOException) {
            return Unavailable("state-or-transport-unavailable");
        } catch (UnauthorizedAccessException) {
            return Unavailable("state-unavailable");
        } catch (InvalidOperationException) {
            return Unavailable("invalid-client");
        } finally {
            if (entered) _gate.Release();
        }
    }

    private static ProbeAvailability Unavailable(string reason) => new("unavailable", reason) { ObservedAtUtc = DateTimeOffset.UtcNow };

    private async Task<T> SendAsync<T>(HttpMethod method, string route, string? bearer, ProbePairRequest? body, CancellationToken token) {
        if (_client.DefaultRequestHeaders.Authorization is not null)
            throw new InvalidOperationException("Probe requires a dedicated client.");
        using var request = new HttpRequestMessage(method, new Uri(_endpoint!, route));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        // A caller-provided handler must disable redirects. Reject any surfaced redirect as an HTTP failure.
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Probe redirect refused.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBodyBytes)
            throw new InvalidDataException("Probe response exceeds its bound.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0) {
            if (output.Length + count > MaximumBodyBytes)
                throw new InvalidDataException("Probe response exceeds its bound.");
            output.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize<T>(output.GetBuffer().AsSpan(0, (int)output.Length), Json)
            ?? throw new InvalidDataException("Probe response is missing.");
    }

    private void PrepareDirectory() {
        EnsurePlainPath(_directory);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
        else {
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            EnsurePlainPath(_directory);
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void EnsurePlainPath(string path) {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) {
            try {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Probe reader state requires plain paths.");
            } catch (FileNotFoundException) {
                // Missing components will be created only after all existing ancestors are checked.
            } catch (DirectoryNotFoundException) {
                // A missing ancestor does not establish that its own parents are plain.
            }
        }
    }

    private static FileStream OpenPrivate(string path, FileMode mode) {
        EnsurePlainPath(path);
        var options = new FileStreamOptions {
            Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows() && mode is FileMode.CreateNew or FileMode.OpenOrCreate)
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return stream;
    }

    private async Task<ReaderState?> LoadStateAsync(CancellationToken token) {
        var path = Path.Combine(_directory, "reader-state.json");
        EnsurePlainPath(path);
        if (!File.Exists(path)) return null;
        await using var input = OpenPrivate(path, FileMode.Open);
        if (input.Length > MaximumBodyBytes) throw new InvalidDataException("Probe reader state exceeds its bound.");
        return await JsonSerializer.DeserializeAsync<ReaderState>(input, Json, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Probe reader state is missing.");
    }

    private async Task SaveStateAsync(ReaderState state, CancellationToken token) {
        var path = Path.Combine(_directory, "reader-state.json");
        EnsurePlainPath(path);
        var temporary = Path.Combine(_directory, "reader-state-" + Guid.NewGuid().ToString("N") + ".tmp");
        try {
            await using (var output = OpenPrivate(temporary, FileMode.CreateNew)) {
                await JsonSerializer.SerializeAsync(output, state, Json, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        } finally {
            File.Delete(temporary);
        }
    }

    private sealed record ReaderState(string Endpoint, string Reader, string State, string? Ticket, string? Token) {
        public override string ToString() => nameof(ReaderState);
    }
}
