using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ReporterEvidenceTests {
    [Fact]
    public void MessageIdentityScopesTheNativeGuidByMachineAndProvider() {
        var messageId = Guid.Parse("5bb12af3-193d-4140-8ded-b765197dbe34");
        var id = LedgerScope.MessageItemId("synthetic-machine", "claude", messageId);
        Assert.Equal("e229d5db431388d28dbca36158350e693ad2bd1ac237234549bd395d664536c1", id);
        Assert.NotEqual(id, LedgerScope.MessageItemId("synthetic-other-machine", "claude", messageId));
        Assert.NotEqual(id, LedgerScope.MessageItemId("synthetic-machine", "codex", messageId));
        Assert.NotEqual(id, LedgerScope.MessageItemId("synthetic-machine", "claude", Guid.Parse("0e107a1a-f920-4e31-976b-aed7fb597f38")));
        Assert.NotEqual(id, LedgerScope.SessionItemId("synthetic-machine", "claude", messageId.ToString()));
    }

    [Theory]
    [InlineData("rate_limit", 0, true, "cold")]
    [InlineData("rate_limit", 8192, true, "warm")]
    [InlineData("rate_limit", 0, true, "warm-multiple")]
    [InlineData("private sentinel rate_limit", 8192, false, "cold")]
    public async Task ClaudeNativeLimitHoldsTheReportedSessionUntilSuccessfulTurn(string code, int padding, bool limited, string scanMode) {
        var directory = Path.Combine(Path.GetTempPath(), "ix-reporter-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var path = Path.Combine(directory, "synthetic-session.jsonl");
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var roots = new[] { new SourceRootRecord("synthetic-root", "claude", UsageSourceKind.LocalLogs, directory) };
            var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
            var settings = new ReporterSettings { Endpoint = "http://127.0.0.1:0/", Token = new('w', 32),
                MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", SpoolDirectory = directory };
            var admission = new LedgerSettings { DataDirectory = directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
                ExpectedReporters = new() { [settings.MachineId] = [settings.ReporterId] } };
            var sourceSession = Guid.Parse("4cb95e65-5e84-480d-8322-4807f90de17e");
            var sourceMessage = Guid.Parse("5bb12af3-193d-4140-8ded-b765197dbe34");
            var sessionItemId = LedgerScope.SessionItemId(settings.MachineId, "claude", sourceSession.ToString());
            var messageItemId = LedgerScope.MessageItemId(settings.MachineId, "claude", sourceMessage);
            using var store = new LedgerStore(Path.Combine(directory, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var epoch = Guid.NewGuid();
            long sequence = 0;
            async Task<LedgerEvent[]> Publish(AgentSessionState observed, DateTimeOffset at) {
                var observation = new LedgerEvent {
                    EventId = Guid.NewGuid(), Epoch = epoch, Sequence = ++sequence,
                    ItemId = sessionItemId, MachineId = settings.MachineId, ReporterId = settings.ReporterId,
                    Provider = "claude", Source = "watcher", ObservedAtUtc = at,
                    Session = LocalReporter.SessionEvidence(settings, observed, null)
                };
                var messages = LocalReporter.SessionMessageEvents(observation, observed.PeerMessages)
                    .Select(e => e with { Sequence = ++sequence }).ToArray();
                var events = new[] { observation }.Concat(messages).ToArray();
                Assert.All(events, e => Assert.True(EventAdmission.Valid(e, admission, at)));
                await store.IngestAsync(events, at);
                return messages;
            }
            AgentSessionState Read(DateTimeOffset at) => Assert.Single(LocalAgentWorkStateService.ReadLocal(roots, at, options: options).Sessions);
            File.WriteAllText(path, JsonSerializer.Serialize(new { type = "user", uuid = "synthetic-input",
                sessionId = sourceSession, timestamp = now.AddMinutes(-1),
                origin = new { kind = "peer", from = "private sentinel sender", msg_id = sourceMessage },
                message = new { content = "private sentinel " + new string('x', padding) } }) + "\n");
            if (scanMode != "cold") Assert.Equal(sourceMessage, Read(now).SourceMessageId);
            var error = limited ? code : code + new string('x', padding);
            File.AppendAllText(path, JsonSerializer.Serialize(new { type = "assistant", uuid = "synthetic-error", parentUuid = "synthetic-input",
                isApiErrorMessage = true, timestamp = now, error,
                message = new { stop_reason = "stop_sequence", content = "private sentinel " + new string('x', padding) } }) + "\n");
            if (scanMode == "warm-multiple") {
                var failed = Read(now);
                Assert.Equal(AgentTurnOutcome.Failed, failed.LastTurnOutcome);
                Assert.Equal(sourceMessage, failed.SourceMessageId);
            }
            // No reporter publication occurs until a later personal turn has replaced the current peer turn.
            File.AppendAllText(path, JsonSerializer.Serialize(new { type = "user", uuid = "synthetic-personal", parentUuid = "synthetic-error",
                timestamp = now.AddMinutes(1), message = new { content = "private sentinel personal" } }) + "\n");
            var retry = Read(now.AddMinutes(1));
            Assert.Null(retry.SourceMessageId);
            Assert.True(retry.TurnOpen);
            var fact = LocalReporter.SessionEvidence(settings, retry, null);
            Assert.Null(fact.SourceMessageId);
            Assert.Equal(limited, fact.UsageLimit);
            var failedEvents = await Publish(retry, now.AddMinutes(1));
            Assert.Equal(3, failedEvents.Length);
            Assert.All(failedEvents, e => Assert.Equal(messageItemId, e.ItemId));
            var terminalEvent = Assert.Single(failedEvents, e => e.Delivery?.Stage == "terminal");
            Assert.Equal(sessionItemId, terminalEvent.Delivery!.TargetItemId);
            Assert.Equal("failed", terminalEvent.Delivery.Outcome);
            Assert.Equal(limited ? "usage-limit" : null, terminalEvent.Delivery.CauseCode);
            Assert.DoesNotContain("private sentinel", JsonSerializer.Serialize(failedEvents, LedgerJson.Options));
            Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(LocalAgentWorkStateService.ReadLocal(roots, now.AddMinutes(1), options: options)));
            Assert.DoesNotContain("private sentinel", Assert.Single(options.Cache!.GetAll()).StateJson!);
            // Rehydration from the durable cache and later success both retain the old message's failure.
            var cached = Read(now.AddMinutes(2));
            Assert.Equal(limited, LocalReporter.SessionEvidence(settings, cached, null).UsageLimit);
            Assert.Equal("failed", Assert.Single(await Publish(cached, now.AddMinutes(2)), e => e.Session is not null).Session!.Outcome);
            File.AppendAllText(path, JsonSerializer.Serialize(new { type = "assistant", uuid = "synthetic-success", parentUuid = "synthetic-personal",
                timestamp = now.AddMinutes(3), message = new { stop_reason = "end_turn" } }) + "\n");
            var recovered = Read(now.AddMinutes(3));
            Assert.Equal(AgentTurnOutcome.Completed, recovered.LastTurnOutcome);
            Assert.Equal(AgentFailureCause.Unknown, recovered.FailureCause);
            Assert.Equal(AgentFailureEvidence.None, recovered.FailureEvidence);
            Assert.False(LocalReporter.SessionEvidence(settings, recovered, null).UsageLimit);
            await Publish(recovered, now.AddMinutes(3));
            var retained = (await store.EvaluateAllAsync(now.AddMinutes(3))).Single(x => x.Id == messageItemId);
            Assert.Equal("delivery", retained.Kind);
            Assert.Equal(sourceMessage, retained.Session!.SourceMessageId);
            Assert.Equal(sourceSession, retained.Session.SourceSessionId);
            Assert.Equal("failed", retained.Session.Outcome);
            Assert.False(retained.Session.TurnOpen);
            var delivery = Assert.Single(retained.Deliveries.Values);
            Assert.Equal(messageItemId, delivery.Id);
            Assert.Equal(sessionItemId, delivery.TargetItemId);
            Assert.Equal("known", delivery.Stages["started"].Knowledge);
            Assert.Equal(now.AddMinutes(-1), delivery.Stages["started"].AtUtc);
            Assert.Equal("known", delivery.Stages["terminal"].Knowledge);
            Assert.Equal(now, delivery.Stages["terminal"].AtUtc);
            Assert.Equal("failed", delivery.Stages["terminal"].Outcome);
            Assert.All(new[] { "requested", "admitted", "submitted", "accepted" },
                stage => Assert.Equal("unknown", delivery.Stages[stage].Knowledge));
            if (limited) {
                Assert.Equal("needs-you", retained.Current!.Value);
                Assert.Equal("usage-limit", retained.Current.ReasonCode);
                Assert.Equal("suppressed", retained.Current.WakeEligibility!.State);
            }
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("user", false, false)]
    [InlineData("assistant", true, false)]
    [InlineData("tool-result", false, true)]
    [InlineData("tool-result", false, false, "synthetic-other-branch", 8192)]
    [InlineData("assistant", false, false, "synthetic-other-branch", 8192)]
    [InlineData("tool-result", false, true, "synthetic-tool-call", 8192)]
    public async Task ClaudePeerCorrelationFollowsTurnBoundaries(string next, bool closeFirst, bool correlated, string? parent = null, int padding = 0) {
        var directory = Path.Combine(Path.GetTempPath(), "ix-peer-turns", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var messageId = Guid.Parse("5bb12af3-193d-4140-8ded-b765197dbe34");
            var path = Path.Combine(directory, "synthetic-session.jsonl");
            var roots = new[] { new SourceRootRecord("synthetic-root", "claude", UsageSourceKind.LocalLogs, directory) };
            var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
            File.WriteAllText(path, JsonSerializer.Serialize(new { type = "user", uuid = "synthetic-peer-turn", timestamp = now,
                origin = new { kind = "peer", msg_id = messageId } }) + "\n");
            Assert.Equal(messageId, Assert.Single(LocalAgentWorkStateService.ReadLocal(roots, now, options: options).Sessions).SourceMessageId);
            if (closeFirst)
                File.AppendAllText(path, "{\"type\":\"assistant\",\"message\":{\"stop_reason\":\"end_turn\"}}\n");
            else if (next == "tool-result")
                File.AppendAllText(path, JsonSerializer.Serialize(new { type = "assistant", uuid = "synthetic-tool-call", parentUuid = "synthetic-peer-turn",
                    message = new { stop_reason = "tool_use", content = new[] { new { type = "tool_use", id = "synthetic-tool", name = "Read" } } } }) + "\n");
            var content = next == "tool-result" ? "{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"synthetic-tool\"}]}" : "{}";
            File.AppendAllText(path, "{\"type\":\"" + (next == "tool-result" ? "user" : next)
                + "\",\"uuid\":\"synthetic-next-record\",\"timestamp\":\"" + now.AddMinutes(1).ToString("O")
                + "\",\"parentUuid\":" + JsonSerializer.Serialize(parent) + ",\"message\":" + content
                + ",\"ignored\":\"" + new string('x', padding) + "\"}\n");
            var session = Assert.Single(LocalAgentWorkStateService.ReadLocal(roots, now.AddMinutes(1), options: options).Sessions);
            Assert.Equal(correlated ? messageId : null, session.SourceMessageId);
            if (parent is null || correlated) {
                Assert.Equal(correlated ? "synthetic-peer-turn" : "synthetic-next-record", session.TurnId);
                Assert.Equal(correlated ? now : now.AddMinutes(1), session.TurnStartedAtUtc);
            }
            File.AppendAllText(path, JsonSerializer.Serialize(new { type = "assistant", uuid = "synthetic-error", parentUuid = "synthetic-next-record",
                timestamp = now.AddMinutes(2), isApiErrorMessage = true, error = "rate_limit", message = new { stop_reason = "stop_sequence" } }) + "\n");
            session = Assert.Single(LocalAgentWorkStateService.ReadLocal(roots, now.AddMinutes(2), options: options).Sessions);
            var settings = new ReporterSettings { Endpoint = "http://127.0.0.1:0/", Token = new('w', 32),
                MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", SpoolDirectory = directory };
            var observation = new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = settings.MachineId,
                ReporterId = settings.ReporterId, ItemId = LedgerScope.SessionItemId(settings.MachineId, "claude", session.Id),
                Provider = "claude", Source = "watcher", ObservedAtUtc = now.AddMinutes(2), Session = LocalReporter.SessionEvidence(settings, session, null)
            };
            var events = LocalReporter.SessionMessageEvents(observation, session.PeerMessages).Select((e, index) => e with { Sequence = index + 1 }).ToArray();
            using var store = new LedgerStore(Path.Combine(directory, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            await store.IngestAsync(events, now.AddMinutes(2));
            var peer = Assert.Single(await store.EvaluateAllAsync(now.AddMinutes(2)));
            Assert.Equal(messageId, peer.Session!.SourceMessageId);
            var delivery = Assert.Single(peer.Deliveries.Values);
            if (correlated) {
                Assert.Equal("failed", delivery.Stages["terminal"].Outcome);
                Assert.Equal("usage-limit", delivery.Stages["terminal"].CauseCode);
            } else if (closeFirst) Assert.Equal("completed", delivery.Stages["terminal"].Outcome);
            else Assert.Equal("unknown", delivery.Stages["terminal"].Knowledge);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PeerMessageRetentionReportsCoverageLossAcrossCacheReuse() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-peer-retention", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var path = Path.Combine(directory, "synthetic-session.jsonl");
            using (var writer = new StreamWriter(path))
                for (var index = 0; index < 257; index++) {
                    writer.WriteLine(JsonSerializer.Serialize(new { type = "user", uuid = "synthetic-peer-" + index, timestamp = now,
                        origin = new { kind = "peer", msg_id = Guid.NewGuid() } }));
                    writer.WriteLine(JsonSerializer.Serialize(new { type = "assistant", timestamp = now, message = new { stop_reason = "end_turn" } }));
                }
            var roots = new[] { new SourceRootRecord("synthetic-root", "claude", UsageSourceKind.LocalLogs, directory) };
            var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
            foreach (var snapshot in new[] { LocalAgentWorkStateService.ReadLocal(roots, now, options: options),
                LocalAgentWorkStateService.ReadLocal(roots, now, options: options) }) {
                var messages = Assert.Single(snapshot.Sessions).PeerMessages;
                Assert.Equal(256, messages.Count);
                Assert.Equal("synthetic-peer-1", messages[0].TurnId);
                Assert.Equal("synthetic-peer-256", messages[^1].TurnId);
                Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "peer-message-retention-limit");
            }
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("unknown", true, false, false)]
    [InlineData("completed", true, false, false)]
    [InlineData("unknown", false, false, false)]
    [InlineData("failed", false, true, false)]
    [InlineData("completed", false, true, false)]
    [InlineData("interrupted", false, true, false)]
    [InlineData("incomplete", false, true, false)]
    [InlineData("failed", false, true, true)]
    public async Task NativeMessageStagesRequireTheirOwnObservedEvidence(string outcome, bool open, bool terminal, bool endedAtKnown) {
        var directory = Path.Combine(Path.GetTempPath(), "ix-reporter-stages", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var messageId = Guid.Parse("5bb12af3-193d-4140-8ded-b765197dbe34");
            var sessionEvent = new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = "synthetic-machine",
                ReporterId = "synthetic-reporter", ItemId = "synthetic-session", Provider = "claude", Source = "watcher",
                ObservedAtUtc = now, Session = new() { SourceMessageId = messageId, TurnOpen = open, Outcome = outcome,
                    TurnEndedAtUtc = endedAtKnown ? now : null }
            };
            var events = LocalReporter.SessionMessageEvents(sessionEvent).Select((e, index) => e with { Sequence = index + 1 }).ToArray();
            var admission = new LedgerSettings { DataDirectory = directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
                ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };
            Assert.All(events, e => Assert.True(EventAdmission.Valid(e, admission, now)));
            Assert.DoesNotContain(events, e => e.Delivery?.Stage == "started");
            Assert.Equal(terminal, events.Any(e => e.Delivery?.Stage == "terminal"));
            using var store = new LedgerStore(Path.Combine(directory, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            await store.IngestAsync(events, now);
            var item = Assert.Single(await store.EvaluateAllAsync(now));
            Assert.Equal(messageId, item.Session!.SourceMessageId);
            if (terminal) {
                var delivery = Assert.Single(item.Deliveries.Values);
                Assert.Equal("unknown", delivery.Stages["started"].Knowledge);
                Assert.Equal("known", delivery.Stages["terminal"].Knowledge);
                Assert.Equal(endedAtKnown ? now : null, delivery.Stages["terminal"].AtUtc);
                Assert.Equal(outcome is "completed" or "failed" ? outcome : null, delivery.Stages["terminal"].Outcome);
            } else Assert.Empty(item.Deliveries);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyProjectClaimsPublisherBuildsGlobalEvidence(bool publisher) {
        var settings = new ReporterSettings { Endpoint = "http://127.0.0.1:0/", Token = new('w', 32),
            MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", SpoolDirectory = Path.GetTempPath(), GitHub = true,
            ProjectClaims = publisher ? new() { Organization = "synthetic", ProjectNumber = 1,
                AgentFieldId = 1, MachineFieldId = 2, StatusFieldId = 3, PullRequestFieldId = 4 } : null };
        var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
        const string url = "https://github.com/Sample/Project/pull/1";
        var snapshot = new AgentWorkSnapshot { GitHubCheckedAtUtc = now, PullRequests = [new() {
            Url = url, HeadRepository = "Sample/Project", Branch = "synthetic-branch" }] };
        var claims = new ClaimSnapshot(["synthetic-item"], ["synthetic-item"], now, new() { ["synthetic-item"] = [url] }, []);
        var evidence = LocalReporter.ProjectEvidence(settings, snapshot, claims, []);
        if (!publisher) { Assert.Null(evidence); return; }
        Assert.NotNull(evidence);
        Assert.Equal("synthetic-item", Assert.Single(evidence.ActiveClaimItems));
        Assert.Equal(now, evidence.ClaimsAtUtc);
        var key = LocalReporter.ItemId("branch", "sample/project", "synthetic-branch");
        Assert.Equal(LocalReporter.ItemId("pr", "github", url), Assert.Single(evidence.OpenPullRequestsByBranch[key]));
        Assert.True(evidence.LinksByItem!["synthetic-item"].Complete);
    }
}
