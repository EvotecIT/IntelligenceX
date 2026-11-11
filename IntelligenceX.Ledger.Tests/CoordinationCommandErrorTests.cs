using System.Net;
using System.Net.Sockets;
using System.Text;
using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationCommandErrorTests {
    [Fact]
    public async Task ServiceRejectionRetainsCodeAndLeavesCommittedGoalReadable() {
        var credential = new string('c', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 1, 1, 4, 4096, ["reservation"])]),
            Principals = [new("requester", "client", credential, ["sample"])]
        }, async (endpoint, _, token) => {
            using var client = LedgerReadClient.ForCoordination(new(endpoint, credential));
            var command = new CoordinationGoalCommand(new("goal", "", 0, false, false, "authorization", ["accepted"]), 0);
            var committed = await client.SetGoalAsync(command, token);
            var error = await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() => client.SetGoalAsync(command, token));
            Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
            Assert.Equal("goal-revision-conflict", error.ErrorCode);
            var retained = await client.GoalAsync("goal", token);
            Assert.Equal(committed.Revision, retained.Revision);
            Assert.Equal(committed.ControlRevision, retained.ControlRevision);
            Assert.Equal(committed.AcceptanceCriteria, retained.AcceptanceCriteria);
        });
    }

    [Theory]
    [InlineData(409, "{\"error\":\"write-help-source-handoff-required\"}", false, "write-help-source-handoff-required")]
    [InlineData(409, "{\"error\":\"reauthorization-goal-held\"}", true, "reauthorization-goal-held")]
    [InlineData(409, "{\"error\":\"private response text\"}", false, null)]
    [InlineData(409, "{\"error\":\"safe-code\",\"details\":\"private\"}", false, null)]
    [InlineData(409, "{\"error\":\"first\",\"error\":\"second\"}", false, null)]
    [InlineData(409, "{\"error\":3}", false, null)]
    [InlineData(409, "[]", false, null)]
    [InlineData(409, "not-json-private", false, null)]
    [InlineData(503, "{\"error\":\"reauthorization-goal-held\"}", false, null)]
    [InlineData(400, "{\"error\":\"reauthorization-goal-held\"}", false, null)]
    public async Task HttpErrorsExposeOnlyValidatedConflictCodes(int status, string body, bool chunked, string? expectedCode) {
        await WithResponse(status, body, chunked, async client => {
            var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.AcceptAsync("request", "result"));
            Assert.Equal((HttpStatusCode)status, error.StatusCode);
            if (expectedCode is not null) Assert.Equal(expectedCode, Assert.IsType<CoordinationCommandRejectedException>(error).ErrorCode);
            else {
                Assert.IsNotType<CoordinationCommandRejectedException>(error);
                Assert.DoesNotContain(body, error.ToString(), StringComparison.Ordinal);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedConflictBodiesRemainUncertain(bool chunked) {
        var body = "{\"error\":\"safe-code\"}" + new string(' ', 4096);
        await WithResponse(409, body, chunked, async client => {
            var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.AcceptAsync("request", "result"));
            Assert.IsNotType<CoordinationCommandRejectedException>(error);
        });
    }

    [Fact]
    public async Task OversizedCodeRemainsUncertain() {
        await WithResponse(409, "{\"error\":\"" + new string('a', 129) + "\"}", false, async client => {
            var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.AcceptAsync("request", "result"));
            Assert.IsNotType<CoordinationCommandRejectedException>(error);
        });
    }

    private static async Task WithResponse(int status, string body, bool chunked, Func<LedgerReadClient, Task> run) {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = ServeAsync();
        try {
            using var client = LedgerReadClient.ForCoordination(new($"http://127.0.0.1:{port}/", new('s', 32)));
            await run(client);
            await serving;
        } finally {
            await deadline.CancelAsync();
            listener.Stop();
            try { await serving; } catch (OperationCanceledException) { }
        }

        async Task ServeAsync() {
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            Assert.StartsWith("POST /api/coordination/v1/requests/request/accept HTTP/1.1", await reader.ReadLineAsync(deadline.Token));
            var contentLength = 0;
            while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } header) {
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(header[15..], System.Globalization.CultureInfo.InvariantCulture);
            }
            var requestBody = new char[contentLength];
            Assert.Equal(contentLength, await reader.ReadBlockAsync(requestBody.AsMemory(), deadline.Token));
            var payload = Encoding.UTF8.GetBytes(body);
            var framing = chunked ? "Transfer-Encoding: chunked" : "Content-Length: " + payload.Length;
            var wireBody = chunked ? payload.Length.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + "\r\n" + body + "\r\n0\r\n\r\n" : body;
            var response = $"HTTP/1.1 {status} Error\r\nContent-Type: application/json\r\n{framing}\r\nConnection: close\r\n\r\n{wireBody}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), deadline.Token);
        }
    }
}
