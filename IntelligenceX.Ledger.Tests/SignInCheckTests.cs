using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class SignInCheckTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
    private readonly List<SignInFact> _published = [];
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);

    private AgentService Service() {
        Directory.CreateDirectory(_directory);
        var settings = new AgentSettings {
            MachineId = "win-pc", ReporterConfig = Path.Combine(_directory, "reporter.json"), CoordinationConfig = Path.Combine(_directory, "host.json"),
            StateDirectory = Path.Combine(_directory, "state"), WorkspaceRoot = Path.Combine(_directory, "work"), Capabilities = ["windows-powershell"]
        };
        var ledger = new LedgerReadClient(new LedgerClientSettings { Endpoint = "http://127.0.0.1:9/", ReadToken = new string('r', 32) });
        return new AgentService(settings, "win-pc", ledger, _published.Add) { SignInPrompt = (_, _) => Task.CompletedTask };
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData(0, "signed-in")]
    [InlineData(1, "signed-out")]
    public void ACodexCheckThatDidNotFinishIsUnknownNotSignedOut(int? exit, string expected) =>
        Assert.Equal(expected, AgentService.CodexStatus(exit));

    [Theory]
    [InlineData(null, "", "unknown")]
    [InlineData(1, "", "unknown")]
    [InlineData(1, "{\"error\":\"internal\"}", "unknown")]
    [InlineData(0, "", "signed-in")]
    [InlineData(1, "{\"loggedIn\":false}", "signed-out")]
    [InlineData(0, "{\"loggedIn\":true,\"loginExpiresAt\":\"2026-01-12T11:00:00Z\"}", "expired")]
    [InlineData(0, "{\"loggedIn\":true,\"loginExpiresAt\":\"2026-01-12T13:00:00Z\"}", "signed-in")]
    public void AClaudeCheckIsSignedOutOnlyWhenItSaysSo(int? exit, string output, string expected) =>
        Assert.Equal(expected, AgentService.ClaudeStatus(exit, output, Now));

    [Fact]
    public void OneSignedOutCheckReadsUnknownAndTheSecondInARowAsksForSignIn() {
        var service = Service();
        service.ObserveCheck("codex", "signed-in");
        service.ObserveCheck("codex", "signed-out");
        Assert.Equal(("unknown", false), (_published[^1].Reason, _published[^1].Required));
        service.ObserveCheck("codex", "signed-out");
        Assert.Equal(("signed-out", true), (_published[^1].Reason, _published[^1].Required));
        Assert.Equal(["signed-in", "unknown", "signed-out"], _published.Select(fact => fact.Reason));
    }

    [Fact]
    public void ASignedOutCheckBetweenGoodOnesNeverAsksForSignIn() {
        var service = Service();
        service.ObserveCheck("claude", "signed-in");
        service.ObserveCheck("claude", "signed-out");
        service.ObserveCheck("claude", "signed-in");
        service.ObserveCheck("claude", "expired");
        Assert.DoesNotContain(_published, fact => fact.Required);
    }

    [Fact]
    public void ASuccessfulTurnRestartsTheConfirmationCount() {
        var service = Service();
        service.ObserveCheck("codex", "signed-out");
        service.Observe("codex", true, "turn");
        service.ObserveCheck("codex", "signed-out");
        Assert.DoesNotContain(_published, fact => fact.Required);
    }

    [Fact]
    public void AnUnknownCheckDoesNotClearASignInFailureFromASend() {
        var service = Service();
        service.Observe("claude", false, "expired");
        service.ObserveCheck("claude", "unknown");
        service.ObserveCheck("claude", "signed-out");
        Assert.Equal(("expired", true), (_published[^1].Reason, _published[^1].Required));
    }

    [Fact]
    public void AnUnknownCheckKeepsAConfirmedSignOutAndASignInClearsIt() {
        var service = Service();
        service.ObserveCheck("codex", "signed-out");
        service.ObserveCheck("codex", "signed-out");
        service.ObserveCheck("codex", "unknown");
        Assert.Equal(("signed-out", true), (_published[^1].Reason, _published[^1].Required));
        service.ObserveCheck("codex", "signed-in");
        Assert.Equal(("signed-in", false), (_published[^1].Reason, _published[^1].Required));
    }

    [Fact]
    public async Task TheLedgerShowsAnUnknownCheckAsDoneWithItsOwnReason() {
        Directory.CreateDirectory(_directory);
        var settings = new LedgerSettings { DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["win-pc"] = ["win-pc-watcher"] } };
        var unknown = new LedgerEvent {
            EventId = Guid.NewGuid(), Epoch = new Guid("00000000-0000-0000-0000-000000000001"), Sequence = 1, MachineId = "win-pc",
            ReporterId = "win-pc-watcher", ItemId = "win-pc:sign-in:codex", ItemKind = "sign-in", Provider = "codex", Source = "watcher",
            ObservedAtUtc = Now, SignIn = new("codex", false, "unknown", "codex login", Now, Now)
        };
        Assert.True(EventAdmission.Valid(unknown, settings, Now));
        Assert.False(EventAdmission.Valid(unknown with { SignIn = unknown.SignIn! with { Required = true } }, settings, Now));
        using var store = new LedgerStore(Path.Combine(_directory, "sign-in.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([unknown], Now);
        var view = LedgerItemView.From(Assert.Single(await store.EvaluateAllAsync(Now)));
        Assert.Equal(("done", "sign-in-unknown", Now), (view.Verdict, view.ReasonCode, view.SignIn!.SinceUtc));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
