using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerSessionObservationHttpTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ItemReadRetainsNullableNativeTurnFactAndItsDirectEvidence(bool? turnOpen) {
        await CoordinationHttpTests.WithService(new CoordinationConfiguration {
            InitialPolicy = new(1, [new("synthetic-machine", 1, 1, 2, 1024, ["review"])]),
            Principals = [new("host", "host", new('h', 32), [], "synthetic-machine")]
        }, async (endpoint, settings, token) => {
            var observedAt = DateTimeOffset.UtcNow;
            var sessionId = Guid.Parse("10000000-0000-4000-8000-000000000001");
            var observation = new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", Provider = "codex",
                ItemId = "synthetic-session", ItemKind = "session", Source = "watcher", SourceVersion = "synthetic-parser",
                ObservedAtUtc = observedAt, Session = new() {
                    SourceSessionId = sessionId, TurnId = "synthetic-turn", TurnOpen = turnOpen,
                    TurnStartedAtUtc = observedAt, LastActivityAtUtc = observedAt,
                    Archived = false, ArchiveObservedAtUtc = observedAt
                }
            };
            using var http = new HttpClient { BaseAddress = new(endpoint) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", settings.WriteToken);
            using (var admitted = await http.PostAsJsonAsync("api/ledger/v1/events", new[] {
                observation, observation with { EventId = Guid.NewGuid(), Sequence = 2, ItemId = "machine",
                    Session = null, Heartbeat = true }
            }, LedgerJson.Options, token)) Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
            http.DefaultRequestHeaders.Authorization = new("Bearer", settings.ReadToken);
            using var detail = await http.GetFromJsonAsync<JsonDocument>("api/ledger/v1/items/synthetic-session", token);
            var session = detail!.RootElement.GetProperty("session");
            Assert.Equal(sessionId, session.GetProperty("sourceSessionId").GetGuid());
            Assert.Equal("synthetic-turn", session.GetProperty("turnId").GetString());
            var expectedKind = turnOpen switch {
                true => JsonValueKind.True,
                false => JsonValueKind.False,
                null => JsonValueKind.Null
            };
            Assert.Equal(expectedKind, session.GetProperty("turnOpen").ValueKind);
            var evidence = detail.RootElement.GetProperty("sessionEvidence");
            Assert.Equal(observation.EventId, evidence.GetProperty("eventId").GetGuid());
            Assert.Equal("watcher", evidence.GetProperty("source").GetString());
            Assert.Equal("synthetic-parser", evidence.GetProperty("sourceVersion").GetString());
            Assert.Equal(observedAt, evidence.GetProperty("observedAtUtc").GetDateTimeOffset());
        });
    }
}
