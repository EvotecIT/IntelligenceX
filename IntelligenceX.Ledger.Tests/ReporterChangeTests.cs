using System.Net;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ReporterChangeTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-reporter-changes", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyComparisonCatalogReplaysSessionsOnceForTerminalOrderingCorrection(bool retainedDelivery) {
        var session = Observation() with { Session = new() { TurnId = "synthetic-turn", TurnOpen = false,
            Outcome = "completed", TurnStartedAtUtc = Now.AddHours(-1), TurnEndedAtUtc = Now, LastActivityAtUtc = Now } };
        var work = Observation() with { Work = new() { ActiveClaim = false } };
        using (var spool = new ReporterSpool(_directory, 1024 * 1024)) {
            var changes = new ReporterChangeTracker(spool, _directory);
            Assert.True(changes.TryAppend(session));
            Assert.True(changes.TryAppend(work));
            if (retainedDelivery) changes.Checkpoint();
            else changes.Acknowledge(spool.Pending().Select(e => e.EventId));
        }
        var file = Path.Combine(_directory, "reporter-comparisons-v1.catalog");
        var catalog = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        foreach (var entry in catalog) entry.Value!.AsObject().Remove("sessionOrderRevision");
        File.WriteAllText(file, catalog.ToJsonString());
        using (var spool = new ReporterSpool(_directory, 1024 * 1024)) {
            var changes = new ReporterChangeTracker(spool, _directory);
            var replay = session with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid() };
            Assert.True(changes.TryAppend(replay));
            Assert.False(changes.TryAppend(work));
            var sessions = spool.Pending().Where(e => e.Session is not null).ToArray();
            Assert.Equal(retainedDelivery ? 2 : 1, sessions.Length);
            Assert.Equal("completed", Assert.Single(sessions, e => e.EventId == replay.EventId).Session!.Outcome);
            changes.Acknowledge(spool.Pending().Select(e => e.EventId));
        }
        using var finalSpool = new ReporterSpool(_directory, 1024 * 1024);
        var final = new ReporterChangeTracker(finalSpool, _directory);
        Assert.False(final.TryAppend(session));
        Assert.Empty(finalSpool.Pending());
    }

    [Theory]
    [InlineData("history")]
    [InlineData("check-time")]
    [InlineData("set-order")]
    public void SemanticWorkComparisonSurvivesRefreshAndRestart(string refresh) {
        var original = Observation() with { Work = new() { HadPullRequests = true, ActiveClaim = false,
            ClaimsAtUtc = Now, PullRequestsKnown = true, PullRequestsAtUtc = Now,
            OpenPullRequests = ["synthetic-a", "synthetic-b"] } };
        var refreshed = original with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
            Work = refresh switch {
                "history" => original.Work with { HadPullRequests = false },
                "check-time" => original.Work with { ClaimsAtUtc = Now.AddMinutes(1), PullRequestsAtUtc = Now.AddMinutes(1) },
                _ => original.Work with { OpenPullRequests = ["synthetic-b", "synthetic-a"] }
            } };
        using (var spool = new ReporterSpool(_directory, 1024 * 1024)) {
            var changes = new ReporterChangeTracker(spool, _directory);
            Assert.True(changes.TryAppend(original));
            Assert.False(changes.TryAppend(refreshed));
            changes.Acknowledge(spool.Pending().Select(value => value.EventId));
        }
        using var restartedSpool = new ReporterSpool(_directory, 1024 * 1024);
        var restarted = new ReporterChangeTracker(restartedSpool, _directory);
        Assert.False(restarted.TryAppend(refreshed with { Epoch = Guid.NewGuid() }));
        Assert.True(restarted.TryAppend(refreshed with { EventId = Guid.NewGuid(),
            Work = refreshed.Work! with { GoalHeld = true, GoalHeldAtUtc = Now.AddMinutes(2) } }));
        Assert.True(Assert.Single(restartedSpool.Pending()).Work!.HadPullRequests);
    }

    [Fact]
    public void ObservationOnlyReadsAndRestartsDoNotReplaySessionOrWorkFacts() {
        var settings = new ReporterSettings { Endpoint = "http://127.0.0.1/", Token = new('w', 32),
            MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", SpoolDirectory = _directory };
        var session = new AgentSessionState { Id = "synthetic-session", Provider = "codex", SourceRootId = "synthetic-root",
            ArchiveStateKnown = true, TurnId = "synthetic-turn", TurnOpen = true,
            TurnStartedAtUtc = Now.AddMinutes(-1), LastTurnActivityAtUtc = Now };
        LedgerEvent SessionAt(DateTimeOffset observed) {
            var archives = new SessionArchiveCache();
            archives.Observe([session], observed);
            return Observation() with { ObservedAtUtc = observed, Session = LocalReporter.SessionEvidence(settings, session, archives.For(session)) };
        }
        var first = SessionAt(Now);
        var work = Observation() with { Sequence = 2, Work = new() { HadPullRequests = false } };
        using (var spool = new ReporterSpool(_directory, 1024 * 1024)) {
            var changes = new ReporterChangeTracker(spool, _directory);
            Assert.True(changes.TryAppend(first));
            Assert.True(changes.TryAppend(work));
            session = session with { Title = "Synthetic refreshed title", LastActivityAtUtc = Now.AddSeconds(30) };
            Assert.False(changes.TryAppend(SessionAt(Now.AddSeconds(30))));
            Assert.False(changes.TryAppend(work with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddSeconds(30) }));
            changes.Acknowledge(spool.Pending().Select(e => e.EventId));
            Assert.Empty(spool.Pending());
        }
        using var restartedSpool = new ReporterSpool(_directory, 1024 * 1024);
        var restarted = new ReporterChangeTracker(restartedSpool, _directory);
        Assert.False(restarted.TryAppend(SessionAt(Now.AddMinutes(1))));
        Assert.False(restarted.TryAppend(work with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), ObservedAtUtc = Now.AddMinutes(1) }));
        var active = SessionAt(Now.AddMinutes(2));
        active = active with { Session = active.Session! with { LastActivityAtUtc = Now.AddMinutes(2) } };
        Assert.False(restarted.TryAppend(active));
        Assert.Empty(restartedSpool.Pending());
        Assert.Equal(Now.AddMinutes(2), Assert.Single(restarted.Progress).LastActivityAtUtc);
        Assert.Equal(first.Session!.TurnId, Assert.Single(restarted.Progress).TurnId);
        var archived = active with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddMinutes(3),
            Session = active.Session with { Archived = true, ArchiveObservedAtUtc = Now.AddMinutes(3) } };
        Assert.True(restarted.TryAppend(archived));
        Assert.Equal(Now.AddMinutes(3), restartedSpool.Pending().Single(e => e.Session?.Archived == true).Session!.ArchiveObservedAtUtc);
        Assert.Equal(Now.AddMinutes(2), Assert.Single(restartedSpool.Pending()).Session!.LastActivityAtUtc);
        Assert.True(restarted.TryAppend(work with { EventId = Guid.NewGuid(), Sequence = 5, Work = new() { HadPullRequests = true } }));
        Assert.True(Assert.Single(restartedSpool.Pending(), e => e.Work is not null).Work!.HadPullRequests);
        Assert.Equal(new long[] { 1, 2 }, restartedSpool.Pending().Select(e => e.Sequence));
    }

    [Fact]
    public void GlobalRechecksIgnoreObservationTimesAndOrderingButRetainChangedProofAndAvailability() {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var changes = new ReporterChangeTracker(spool, _directory);
        var first = Observation() with { ItemId = "project-evidence", Provider = "project", GlobalWork = GlobalAt(Now) };
        Assert.True(changes.TryAppend(first));
        var recheck = first with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(5),
            GlobalWork = GlobalAt(Now.AddMinutes(5)) with { ActiveClaimItems = ["synthetic-b", "synthetic-a"],
                OpenPullRequestsByBranch = new() { ["synthetic-branch"] = ["synthetic-pr-b", "synthetic-pr-a"] } } };
        Assert.False(changes.TryAppend(recheck));
        var merged = recheck with { EventId = Guid.NewGuid(), Sequence = 3, GlobalWork = recheck.GlobalWork! with {
            PullRequestProofs = [new("synthetic-pr-a", "merged", Now.AddMinutes(5))] } };
        Assert.True(changes.TryAppend(merged));
        Assert.Equal(Now.AddMinutes(5), spool.Pending().Single(e => e.EventId == merged.EventId).GlobalWork!.PullRequestProofs![0].CheckedAtUtc);
        var unavailable = merged with { EventId = Guid.NewGuid(), Sequence = 4,
            GlobalWork = merged.GlobalWork! with { ClaimsAtUtc = null, ActiveClaimItems = [] } };
        Assert.True(changes.TryAppend(unavailable));
        Assert.True(changes.TryAppend(merged with { EventId = Guid.NewGuid(), Sequence = 5, ObservedAtUtc = Now.AddMinutes(6) }));
        Assert.Equal(4, spool.Pending().Length);
    }

    [Fact]
    public void PendingEventsRecoverComparisonAfterInterruptedCheckpointAndAdmissionFailureRemainsRetryable() {
        var observation = Observation() with { Session = new() { TurnOpen = true, LastActivityAtUtc = Now } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, LedgerJson.Options).Length;
        using (var spool = new ReporterSpool(_directory, bytes)) {
            var changes = new ReporterChangeTracker(spool, _directory);
            Assert.True(changes.TryAppend(observation));
            var changed = observation with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
                Session = observation.Session with { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now.AddMinutes(1) } };
            Assert.False(changes.TryAppend(changed));
            // No checkpoint is written before this simulated interrupted reporter exits.
        }
        using var restartedSpool = new ReporterSpool(_directory, 1024 * 1024);
        var restarted = new ReporterChangeTracker(restartedSpool, _directory);
        var replay = observation with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid() };
        Assert.True(restarted.TryAppend(replay));
        Assert.Equal(new[] { observation.EventId, replay.EventId }.Order(), restartedSpool.Pending().Select(e => e.EventId).Order());
        Assert.False(restarted.TryAppend(observation with { EventId = Guid.NewGuid() }));
        restarted.Acknowledge([observation.EventId, replay.EventId]);
        Assert.Empty(restartedSpool.Pending());
        Assert.False(new ReporterChangeTracker(restartedSpool, _directory).TryAppend(observation));
        var terminal = observation with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
            Session = observation.Session with { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now.AddMinutes(1) } };
        Assert.True(restarted.TryAppend(terminal));
        Assert.Equal("completed", Assert.Single(restartedSpool.Pending()).Session!.Outcome);
    }

    [Fact]
    public void FailedComparisonCheckpointKeepsAcknowledgedEventsDurableUntilRetry() {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var changes = new ReporterChangeTracker(spool, _directory);
        var observation = Observation() with { Session = new() { TurnId = "synthetic-turn", TurnOpen = true, LastActivityAtUtc = Now } };
        Assert.True(changes.TryAppend(observation));
        var checkpoint = Path.Combine(_directory, "reporter-comparisons-v1.catalog");
        using (var interruptedWrite = new FileStream(checkpoint + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) {
            Assert.ThrowsAny<IOException>(() => changes.Acknowledge([observation.EventId]));
            Assert.Equal(observation.EventId, Assert.Single(spool.Pending()).EventId);
        }
        changes.Acknowledge([observation.EventId]);
        Assert.Empty(spool.Pending());
        Assert.True(File.Exists(checkpoint));
    }

    [Fact]
    public async Task HealthPostsLatestCoverageWithoutSpoolingAndDurableEventsRetryIndependently() {
        var observation = Observation() with { Session = new() { TurnId = "synthetic-turn", TurnOpen = true, LastActivityAtUtc = Now } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, LedgerJson.Options).Length;
        using var spool = new ReporterSpool(_directory, bytes);
        var changes = new ReporterChangeTracker(spool, _directory);
        Assert.True(changes.TryAppend(observation));
        using var handler = new UploadHandler { RejectEvents = true, RejectHealth = true };
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/") };
        var health = Observation() with { ItemId = "machine", Provider = "machine", SourceVersion = "synthetic-version",
            Coverage = ["collection-starting"] };
        await LocalReporter.DeliverAsync(client, spool, health, default, changes);
        Assert.Equal(observation.EventId, Assert.Single(spool.Pending()).EventId);
        Assert.Equal("collection-starting", Assert.Single(Assert.Single(handler.Health).Coverage));
        Assert.False(changes.TryAppend(observation with { EventId = Guid.NewGuid(), ObservedAtUtc = Now.AddSeconds(30),
            Session = observation.Session! with { LastActivityAtUtc = Now.AddSeconds(30) } }));
        handler.RejectEvents = false;
        handler.RejectHealth = false;
        await LocalReporter.DeliverAsync(client, spool, health with { ObservedAtUtc = Now.AddMinutes(1), Coverage = ["queue-not-visible"] }, default, changes);
        Assert.Empty(spool.Pending());
        Assert.Equal(observation.EventId, Assert.Single(handler.AcceptedEvents).EventId);
        var latest = handler.Health.Last();
        Assert.Equal("synthetic-machine", latest.MachineId);
        Assert.Equal("synthetic-reporter", latest.ReporterId);
        Assert.Equal("synthetic-version", latest.SourceVersion);
        Assert.Equal(Now.AddMinutes(1), latest.ObservedAtUtc);
        Assert.Equal("queue-not-visible", Assert.Single(latest.Coverage));
        var progress = Assert.Single(latest.Progress!);
        Assert.Equal("synthetic-session", progress.ItemId);
        Assert.Equal("synthetic-turn", progress.TurnId);
        Assert.Equal(Now.AddSeconds(30), progress.LastActivityAtUtc);
        Assert.Equal(2, handler.Health.Count);
        Assert.DoesNotContain(handler.AcceptedEvents, e => e.Heartbeat || e.ItemId is "machine" or "source-health");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("synthetic-turn", false)]
    [InlineData("synthetic-turn", null)]
    public void ActivityRemainsDurableForTerminalOrUnknownTurns(string? turnId, bool? open) {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var changes = new ReporterChangeTracker(spool, _directory);
        var first = Observation() with { Session = new() { TurnId = turnId, TurnOpen = open, LastActivityAtUtc = Now } };
        Assert.True(changes.TryAppend(first));
        Assert.True(changes.TryAppend(first with { EventId = Guid.NewGuid(), ObservedAtUtc = Now.AddMinutes(1),
            Session = first.Session with { LastActivityAtUtc = Now.AddMinutes(1) } }));
        Assert.Equal(Now.AddMinutes(1), spool.Pending().Last().Session!.LastActivityAtUtc);
        Assert.Equal(2, spool.Pending().Length);
        Assert.Empty(changes.Progress);
    }

    [Fact]
    public async Task HealthCapsOpenTurnProgressAndReportsTheCoverageGap() {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var changes = new ReporterChangeTracker(spool, _directory);
        for (var index = 0; index < 129; index++)
            Assert.True(changes.TryAppend(Observation() with { ItemId = "synthetic-" + index,
                Session = new() { TurnId = "synthetic-turn", TurnOpen = true, LastActivityAtUtc = Now.AddSeconds(index) } }));
        using var handler = new UploadHandler();
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/") };
        await LocalReporter.DeliverAsync(client, spool, Observation() with { ObservedAtUtc = Now.AddMinutes(5) }, default, changes);
        var health = Assert.Single(handler.Health);
        Assert.Equal(128, health.Progress!.Length);
        Assert.Equal("progress-truncated", Assert.Single(health.Coverage));
        Assert.DoesNotContain(health.Progress, p => p.ItemId == "synthetic-0");
        Assert.Contains(health.Progress, p => p.ItemId == "synthetic-128" && p.LastActivityAtUtc == Now.AddSeconds(128));
        Assert.Empty(spool.Pending());
    }

    [Fact]
    public async Task UnchangedGlobalRefreshPreservesCheckTimesOutsideTheDurableStreamAndRetriesConflict() {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var changes = new ReporterChangeTracker(spool, _directory);
        var first = Observation() with { ItemId = "project-evidence", Provider = "project", GlobalWork = GlobalAt(Now) };
        Assert.True(changes.TryAppend(first));
        using var handler = new UploadHandler();
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/") };
        Assert.False(await LocalReporter.RefreshProjectEvidenceAsync(client, spool, changes, first, default));
        Assert.Empty(handler.ProjectRefreshes);
        await LocalReporter.DeliverAsync(client, spool, Observation(), default, changes);
        var refreshed = first with { EventId = Guid.NewGuid(), ObservedAtUtc = Now.AddMinutes(5), GlobalWork = GlobalAt(Now.AddMinutes(5)) };
        Assert.False(changes.TryAppend(refreshed));
        handler.ProjectStatus = HttpStatusCode.Conflict;
        Assert.False(await LocalReporter.RefreshProjectEvidenceAsync(client, spool, changes, refreshed, default));
        handler.ProjectStatus = HttpStatusCode.OK;
        Assert.True(await LocalReporter.RefreshProjectEvidenceAsync(client, spool, changes, refreshed, default));
        Assert.Equal(2, handler.ProjectRefreshes.Count);
        var latest = handler.ProjectRefreshes.Last().GlobalWork!;
        Assert.Equal(Now.AddMinutes(5), latest.ClaimsAtUtc);
        Assert.Equal(Now.AddMinutes(5), latest.PullRequestsAtUtc);
        Assert.Equal(Now.AddMinutes(5), Assert.Single(latest.PullRequestProofs!).CheckedAtUtc);
        Assert.Equal(Now.AddMinutes(5), latest.LinksByItem!["synthetic-a"].CheckedAtUtc);
        var changed = refreshed with { GlobalWork = refreshed.GlobalWork! with { ActiveClaimItems = [] } };
        Assert.False(await LocalReporter.RefreshProjectEvidenceAsync(client, spool, changes, changed, default));
        Assert.Single(handler.AcceptedEvents);
        Assert.Empty(spool.Pending());
    }

    private sealed class UploadHandler : HttpMessageHandler {
        internal bool RejectEvents { get; set; }
        internal bool RejectHealth { get; set; }
        internal List<LedgerEvent> AcceptedEvents { get; } = [];
        internal List<ReporterHealth> Health { get; } = [];
        internal List<LedgerEvent> ProjectRefreshes { get; } = [];
        internal HttpStatusCode ProjectStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath == "/api/ledger/v1/project-evidence") {
                ProjectRefreshes.Add(JsonSerializer.Deserialize<LedgerEvent>(body, LedgerJson.Options)!);
                return new(ProjectStatus);
            }
            if (request.RequestUri!.AbsolutePath == "/api/ledger/v1/reporters/health") {
                Health.Add(JsonSerializer.Deserialize<ReporterHealth>(body, LedgerJson.Options)!);
                return new(RejectHealth ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }
            Assert.Equal("/api/ledger/v1/events", request.RequestUri.AbsolutePath);
            if (RejectEvents) return new(HttpStatusCode.ServiceUnavailable);
            var events = JsonSerializer.Deserialize<LedgerEvent[]>(body, LedgerJson.Options)!;
            AcceptedEvents.AddRange(events);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(
                new { acknowledgedEventIds = events.Select(e => e.EventId).ToArray() }, LedgerJson.Options)) };
        }
    }

    private static GlobalWorkFact GlobalAt(DateTimeOffset at) => new(at, ["synthetic-a", "synthetic-b"], at,
        new() { ["synthetic-branch"] = ["synthetic-pr-a", "synthetic-pr-b"] },
        [new("synthetic-pr-a", "open", at)],
        new() { ["synthetic-a"] = new(["synthetic-pr-a"], true, at) });

    private static LedgerEvent Observation() => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = "synthetic-machine",
        ReporterId = "synthetic-reporter", ItemId = "synthetic-session", Provider = "codex", Source = "watcher", ObservedAtUtc = Now
    };

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
