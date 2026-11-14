using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting.Probe;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ProbeReaderTests : IDisposable {
    private const string SyntheticToken = "synthetic-probe-credential";
    private const string At = "2026-11-13T10:00:00Z";
    private readonly string _directory = LedgerTestPaths.NewDirectory("probe");

    [Fact]
    public async Task PendingTicketSurvivesRestartAndAllowedTokenIsSavedOnceForAuthenticatedSnapshots() {
        var pairPosts = 0;
        var polls = 0;
        var snapshots = 0;
        using var handler = new Handler(async (request, cancellationToken) => {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Equal(47820, request.RequestUri.Port);
            if (request.RequestUri.AbsolutePath == "/v1/snapshot") {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(SyntheticToken, request.Headers.Authorization?.Parameter);
                using (var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(StatePath, cancellationToken)))
                    Assert.Equal(SyntheticToken, persisted.RootElement.GetProperty("token").GetString());
                snapshots++;
                return Reply(Snapshot());
            }
            Assert.Null(request.Headers.Authorization);
            switch (request.RequestUri.AbsolutePath) {
                case "/v1/info": return Reply("{\"version\":1,\"futureField\":true}");
                case "/v1/pair":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)))
                        Assert.Equal("synthetic-reader", body.RootElement.GetProperty("reader").GetString());
                    pairPosts++;
                    return Reply("{\"id\":\"synthetic-ticket\"}");
                case "/v1/pair/synthetic-ticket":
                    polls++;
                    return Reply(polls == 1 ? "{\"state\":\"pending\"}" :
                        "{\"state\":\"allowed\",\"token\":\"" + SyntheticToken + "\"}");
                default: throw new InvalidOperationException("Unexpected Probe route.");
            }
        });
        using var client = new HttpClient(handler);
        var pending = await Reader(client).ReadAsync();
        Assert.Equal("unavailable", pending.State);
        Assert.Equal("pairing-pending", pending.Reason);
        using (var state = JsonDocument.Parse(await File.ReadAllTextAsync(StatePath))) {
            Assert.Equal("synthetic-ticket", state.RootElement.GetProperty("ticket").GetString());
            Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("token").ValueKind);
        }

        var available = await Reader(client).ReadAsync();
        AssertFacts(available);
        var saved = await File.ReadAllBytesAsync(StatePath);
        var sentinelTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(StatePath, sentinelTime);
        AssertFacts(await Reader(client).ReadAsync());
        Assert.Equal(saved, await File.ReadAllBytesAsync(StatePath));
        Assert.Equal(sentinelTime, File.GetLastWriteTimeUtc(StatePath));
        using (var state = JsonDocument.Parse(saved))
            Assert.Equal(SyntheticToken, state.RootElement.GetProperty("token").GetString());
        Assert.Equal(1, pairPosts);
        Assert.Equal(2, polls);
        Assert.Equal(2, snapshots);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.DoesNotContain(SyntheticToken, JsonSerializer.Serialize(available));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        if (!OperatingSystem.IsWindows()) {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StatePath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_directory));
        }
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("expired")]
    public async Task TerminalPairingStateSurvivesRestartWithoutAnotherPairOrPoll(string terminal) {
        var requests = 0;
        using var handler = new Handler((request, _) => {
            requests++;
            return Task.FromResult(request.RequestUri!.AbsolutePath switch {
                "/v1/info" => Reply("{\"version\":1}"),
                "/v1/pair" => Reply("{\"id\":\"synthetic-ticket\"}"),
                "/v1/pair/synthetic-ticket" => Reply("{\"state\":\"" + terminal + "\"}"),
                _ => throw new InvalidOperationException("Unexpected Probe route.")
            });
        });
        using var client = new HttpClient(handler);
        Assert.Equal(terminal, (await Reader(client).ReadAsync()).Reason);
        Assert.Equal(terminal, (await Reader(client).ReadAsync()).Reason);
        Assert.Equal(3, requests);
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(StatePath));
        Assert.Equal(terminal, state.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task RevokedCredentialIsPersistedWithoutRepairingOrReusingTheToken() {
        await SaveAllowedAsync();
        var requests = 0;
        using var handler = new Handler((request, _) => {
            requests++;
            Assert.Equal("/v1/snapshot", request.RequestUri!.AbsolutePath);
            Assert.Equal(SyntheticToken, request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        using var client = new HttpClient(handler);
        Assert.Equal("unauthorized", (await Reader(client).ReadAsync()).Reason);
        Assert.Equal("unauthorized", (await Reader(client).ReadAsync()).Reason);
        Assert.Equal(1, requests);
        Assert.DoesNotContain(SyntheticToken, await File.ReadAllTextAsync(StatePath));
    }

    [Fact]
    public async Task LostPairResponseRequiresManualResetInsteadOfCreatingAnotherTicket() {
        var posts = 0;
        using var handler = new Handler((request, _) => {
            if (request.RequestUri!.AbsolutePath == "/v1/info")
                return Task.FromResult(Reply("{\"version\":1}"));
            Assert.Equal("/v1/pair", request.RequestUri.AbsolutePath);
            posts++;
            throw new HttpRequestException("Synthetic disconnected pairing response.");
        });
        using var client = new HttpClient(handler);
        Assert.Equal("transport-unavailable", (await Reader(client).ReadAsync()).Reason);
        Assert.Equal("pairing-interrupted", (await Reader(client).ReadAsync()).Reason);
        Assert.Equal(1, posts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HigherMajorVersionIsRefusedBeforePairingOrSnapshotProjection(bool paired) {
        if (paired) await SaveAllowedAsync();
        var requests = 0;
        using var handler = new Handler((request, _) => {
            requests++;
            Assert.Equal(paired ? "/v1/snapshot" : "/v1/info", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Reply("{\"version\":2,\"at\":\"" + At + "\",\"cpu\":{\"use\":0.8}}"));
        });
        using var client = new HttpClient(handler);
        var result = await Reader(client).ReadAsync();
        Assert.Equal("unavailable", result.State);
        Assert.Equal("unsupported-version", result.Reason);
        Assert.Null(result.CpuUse);
        Assert.Equal(1, requests);
        Assert.Equal(paired, File.Exists(StatePath));
    }

    [Fact]
    public async Task PausedSnapshotExposesPauseTimeWithoutMachineMetrics() {
        await SaveAllowedAsync();
        using var handler = new Handler((_, _) => Task.FromResult(Reply(
            "{\"version\":1,\"at\":\"" + At + "\",\"paused\":true,\"pausedUntil\":\"2026-11-13T11:00:00Z\",\"cpu\":{\"use\":0.8}}")));
        using var client = new HttpClient(handler);
        var result = await Reader(client).ReadAsync();
        Assert.Equal("paused", result.State);
        Assert.Equal(DateTimeOffset.Parse(At), result.SampledAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-13T11:00:00Z"), result.PausedUntilUtc);
        Assert.Null(result.CpuUse);
        Assert.Null(result.Volumes);
    }

    [Theory]
    [InlineData("\"use\":1e400")]
    [InlineData("\"loadAverage\":[1e400]")]
    public async Task NonFiniteMetricsRemainOptionalAndSerializable(string metric) {
        await SaveAllowedAsync();
        using var handler = new Handler((_, _) => Task.FromResult(Reply(
            "{\"version\":1,\"at\":\"" + At + "\",\"cpu\":{" + metric + "}}")));
        using var client = new HttpClient(handler);
        var result = await Reader(client).ReadAsync();
        Assert.Equal("unavailable", result.State);
        Assert.Equal("invalid-response", result.Reason);
        Assert.Null(result.CpuUse);
        Assert.Null(result.LoadAverage);
        Assert.NotEmpty(JsonSerializer.SerializeToUtf8Bytes(result, LedgerJson.Options));
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("schema")]
    [InlineData("length")]
    [InlineData("stream")]
    [InlineData("timeout")]
    public async Task OptionalFailuresReturnBoundedUnavailableEvidence(string failure) {
        await SaveAllowedAsync();
        var stalled = false;
        using var handler = new Handler((_, _) => {
            if (failure == "connect") throw new HttpRequestException("Synthetic unavailable Probe.");
            var response = failure switch {
                "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "json" => Reply("{"),
                "schema" => Reply("{\"version\":1,\"at\":\"" + At + "\",\"volumes\":[null]}"),
                "length" => Reply(new string(' ', 128 * 1024 + 1)),
                "stream" => new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StreamContent(new UnknownLengthStream(new byte[128 * 1024 + 1]))
                },
                "timeout" => new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StreamContent(new StalledStream(() => stalled = true))
                },
                _ => throw new InvalidOperationException("Unknown fixture.")
            };
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var elapsed = Stopwatch.StartNew();
        var result = await Reader(client).ReadAsync();
        Assert.Equal("unavailable", result.State);
        Assert.Equal(failure switch {
            "connect" => "transport-unavailable", "http" => "http-503", "json" or "schema" => "invalid-response",
            "timeout" => "timeout", _ => "invalid-data"
        }, result.Reason);
        Assert.NotNull(result.ObservedAtUtc);
        Assert.Null(result.SampledAtUtc);
        Assert.Null(result.CpuUse);
        if (failure == "timeout") {
            Assert.True(stalled);
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8));
        }
    }

    [Fact]
    public async Task DiscoveryAndPairingShareOneReadDeadline() {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pairingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) => {
            if (request.RequestUri!.AbsolutePath == "/v1/info") {
                discoveryStarted.SetResult();
                await finishDiscovery.Task.WaitAsync(token);
                return Reply("{\"version\":1}");
            }
            Assert.Equal("/v1/pair", request.RequestUri.AbsolutePath);
            pairingStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply("{\"id\":\"synthetic-ticket\"}");
        });
        using var client = new HttpClient(handler);
        var reader = new ProbeReader(client, _directory, "synthetic-reader", timeProvider: clock);
        var pending = reader.ReadAsync();
        await discoveryStarted.Task;
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        finishDiscovery.SetResult();
        await pairingStarted.Task;
        clock.Advance(TimeSpan.FromMilliseconds(899));
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = await pending;
        Assert.Equal("unavailable", result.State);
        Assert.Equal("timeout", result.Reason);
    }

    [Theory]
    [InlineData("http://192.0.2.1:47820")]
    [InlineData("http://reader:secret@127.0.0.1:47820")]
    [InlineData("http://127.0.0.1:47820/path")]
    [InlineData("http://127.0.0.1:47820/?query")]
    [InlineData("http://127.0.0.1:47820/#fragment")]
    public async Task NonOriginOrNonLoopbackEndpointIsRejectedBeforeSending(string endpoint) {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No request should be sent."));
        using var client = new HttpClient(handler);
        var result = await new ProbeReader(client, _directory, "synthetic-reader", endpoint).ReadAsync();
        Assert.Equal("unavailable", result.State);
        Assert.Equal("invalid-endpoint", result.Reason);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task DefaultLedgerAuthorizationIsRejectedBeforeSendingOrPairing() {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No request should be sent."));
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-ledger-credential");
        Assert.Equal("invalid-client", (await Reader(client).ReadAsync()).Reason);
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("ancestor")]
    [InlineData("reader-state.json")]
    [InlineData("reader.lock")]
    public async Task LinkedStatePathsAreRejectedBeforeTouchingTheTarget(string linkedPart) {
        var outside = Path.Combine(_directory, "foreign");
        var plain = Path.Combine(_directory, "private");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(plain);
        var target = Path.Combine(outside, "preserve.txt");
        await File.WriteAllTextAsync(target, "synthetic-foreign-content");
        var foreignMode = UnixFileMode.UserRead | UnixFileMode.GroupRead;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, foreignMode);
        var link = Path.Combine(plain, linkedPart == "ancestor" ? "linked" : linkedPart);
        var stateDirectory = plain;
        if (linkedPart == "ancestor") {
            Directory.CreateSymbolicLink(link, outside);
            stateDirectory = Path.Combine(link, "probe-reader");
        } else File.CreateSymbolicLink(link, target);
        try {
            using var handler = new Handler((_, _) => throw new InvalidOperationException("No request should be sent."));
            using var client = new HttpClient(handler);
            var result = await new ProbeReader(client, stateDirectory, "synthetic-reader").ReadAsync();
            Assert.Equal("unavailable", result.State);
            Assert.Equal("invalid-data", result.Reason);
            Assert.Equal("synthetic-foreign-content", await File.ReadAllTextAsync(target));
            Assert.False(Directory.Exists(Path.Combine(outside, "probe-reader")));
            Assert.False(File.Exists(Path.Combine(plain, "reader-state.json")) && linkedPart != "reader-state.json");
            Assert.False(File.Exists(Path.Combine(plain, "reader.lock")) && linkedPart != "reader.lock");
            if (!OperatingSystem.IsWindows()) Assert.Equal(foreignMode, File.GetUnixFileMode(target));
        } finally {
            if (linkedPart == "ancestor") Directory.Delete(link);
            else File.Delete(link);
        }
    }

    private ProbeReader Reader(HttpClient client) => new(client, _directory, "synthetic-reader");
    private string StatePath => Path.Combine(_directory, "reader-state.json");

    private async Task SaveAllowedAsync() {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(new {
            endpoint = "http://127.0.0.1:47820/", reader = "synthetic-reader", state = "allowed", ticket = "synthetic-ticket", token = SyntheticToken
        }));
    }

    private static string Snapshot() => JsonSerializer.Serialize(new {
        version = 1, at = At, cpu = new { use = 0.25, loadAverage = new[] { 1.5, 1.0, 0.5 }, futureCpu = true },
        memory = new { used = 123L, total = 456L, pressure = "normal", swapUsed = 12L },
        volumes = Enumerable.Range(0, 10).Select(i => new { name = "ignored-name", mount = "/volume-" + i, free = 10L + i, total = 100L + i }),
        activity = new { runnerBusy = true, runnerListening = false, watched = new[] { "ignored-app" } },
        unknownFutureObject = new { enabled = true }, processes = new[] { new { name = "ignored-process" } }
    });

    private static void AssertFacts(ProbeAvailability result) {
        Assert.Equal("available", result.State);
        Assert.Null(result.Reason);
        Assert.Equal(DateTimeOffset.Parse(At), result.SampledAtUtc);
        Assert.NotNull(result.ObservedAtUtc);
        Assert.Equal(0.25, result.CpuUse);
        Assert.Equal(new[] { 1.5, 1.0, 0.5 }, result.LoadAverage);
        Assert.Equal("normal", result.MemoryPressure);
        Assert.Equal(123, result.MemoryUsedBytes);
        Assert.Equal(456, result.MemoryTotalBytes);
        Assert.Equal(12, result.SwapUsedBytes);
        Assert.True(result.RunnerBusy);
        Assert.False(result.RunnerListening);
        Assert.True(result.VolumesCapped);
        Assert.Equal(8, result.Volumes!.Length);
        Assert.Equal(new ProbeVolumeView("/volume-0", 10, 100), result.Volumes[0]);
        Assert.Equal(new ProbeVolumeView("/volume-7", 17, 107), result.Volumes[7]);
    }

    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes) {
        public override bool CanSeek => false;
    }

    private sealed class StalledStream(Action reading) : Stream {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            reading();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
