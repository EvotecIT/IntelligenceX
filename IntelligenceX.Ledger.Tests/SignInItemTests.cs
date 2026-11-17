using System.Text.Json;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class SignInItemTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);

    private static LedgerEvent SignIn(bool required, string reason, DateTimeOffset at, long sequence = 1) => new() {
        EventId = Guid.NewGuid(), Epoch = new Guid("00000000-0000-0000-0000-000000000001"),
        Sequence = sequence, MachineId = "mac", ReporterId = "mac-watcher", ItemId = "mac:sign-in:claude", ItemKind = "sign-in",
        Provider = "claude", Source = "watcher", ObservedAtUtc = at,
        SignIn = new("claude", required, reason, "claude auth login", Now, at, Held: required ? 2 : 0)
    };

    [Fact]
    public async Task ASignInNeededOnAMachineIsANeedsYouItemUntilTheProviderIsSignedInAgain() {
        Directory.CreateDirectory(_directory);
        using var store = new LedgerStore(Path.Combine(_directory, "sign-in.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([SignIn(true, "expired", Now)], Now);
        var item = Assert.Single(await store.EvaluateAllAsync(Now));
        var view = LedgerItemView.From(item);
        Assert.Equal(("sign-in", "mac", "needs-you", "sign-in-required"), (view.Kind, view.MachineId, view.Verdict, view.ReasonCode));
        Assert.Equal(("claude", "claude auth login", 2), (view.SignIn!.Provider, view.SignIn.Command, view.SignIn.Held));
        await store.IngestAsync([SignIn(false, "signed-in", Now.AddMinutes(5), 2)], Now.AddMinutes(5));
        var cleared = LedgerItemView.From(Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(5))));
        Assert.Equal(("done", "signed-in", false), (cleared.Verdict, cleared.ReasonCode, cleared.SignIn!.Required));
    }

    [Fact]
    public void AdmissionAcceptsOnlyConsistentSignInFacts() {
        var settings = new LedgerSettings { DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["mac"] = ["mac-watcher"] } };
        var valid = SignIn(true, "signed-out", Now);
        Assert.True(EventAdmission.Valid(valid, settings, Now));
        Assert.False(EventAdmission.Valid(valid with { ItemKind = "session" }, settings, Now));
        Assert.False(EventAdmission.Valid(valid with { SignIn = valid.SignIn! with { Required = false } }, settings, Now));
        Assert.False(EventAdmission.Valid(valid with { SignIn = valid.SignIn! with { Provider = "codex" } }, settings, Now));
        Assert.False(EventAdmission.Valid(valid with { SignIn = valid.SignIn! with { Command = "claude\nlogin" } }, settings, Now));
    }

    [Theory]
    [InlineData(401, null, "signed-out")]
    [InlineData(429, null, "usage-limit")]
    [InlineData(null, "Failed to authenticate: OAuth session expired and could not be refreshed", "expired")]
    [InlineData(null, "Not logged in. Please run /login", "signed-out")]
    public void TheSenderNamesWhyTheClaudeCliCouldNotRun(int? status, string? text, string failure) {
        var result = new Dictionary<string, object?> { ["type"] = "result", ["is_error"] = true, ["result"] = text ?? "" };
        if (status is not null) result["api_error_status"] = status;
        var outcome = ClaudeSender.Parse([JsonSerializer.Serialize(result)], "haiku");
        Assert.Equal(("not-submitted", failure), (outcome.Stage, outcome.Failure));
    }

    [Fact]
    public void TheInboxHookAddsHeldMessagesAsSessionContext() {
        using var json = JsonDocument.Parse(LedgerInbox.Format("SessionStart", "Result of your ask a-1:\n\nDone."));
        var output = json.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", output.GetProperty("hookEventName").GetString());
        Assert.Contains("Done.", output.GetProperty("additionalContext").GetString());
        Assert.Equal("claude auth login", AgentService.SignInCommand("claude"));
        Assert.Equal("codex login", AgentService.SignInCommand("codex"));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
