using System.Text.Json;
using IntelligenceX.Cli.Ledger;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class LedgerDeclarationTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-cli", Guid.NewGuid().ToString("N"));
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static WorkerDeclaration Declaration() => new() {
        Id = "synthetic-declaration", EpisodeId = "synthetic-episode", Scope = new("synthetic-task", 1, "synthetic-turn"),
        Kind = "wait", CheckpointRef = "synthetic-checkpoint", ConditionRef = "synthetic-condition",
        NextActor = "worker", Blocking = true, RecordedAtUtc = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero),
        ReviewAfterUtc = new(2026, 1, 12, 13, 0, 0, TimeSpan.Zero)
    };
    private string[] Arguments(WorkerDeclaration declaration) {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "declaration.json");
        var config = Path.Combine(_directory, "reporter.json");
        File.WriteAllText(file, JsonSerializer.Serialize(declaration, LedgerJson.Options));
        File.WriteAllText(config, JsonSerializer.Serialize(new {
            machineId = "synthetic-machine", reporterId = "synthetic-reporter", spoolDirectory = Path.Combine(_directory, "outbox")
        }));
        return ["declare", "codex", SessionId.ToString("D"), file, "--config", config];
    }

    [Fact]
    public async Task DeclarationCommandValidatesBeforeAdmissionAndRetriesOneImmutableDurableEvent() {
        var declaration = Declaration();
        var args = Arguments(declaration);
        Assert.Equal(0, await LedgerRunner.RunAsync([.. args, "--validate"]));
        Assert.False(Directory.Exists(Path.Combine(_directory, "outbox")));
        Assert.Equal(2, await LedgerRunner.RunAsync([.. Arguments(declaration with { NextActor = "synthetic actor" }), "--validate"]));
        Assert.False(Directory.Exists(Path.Combine(_directory, "outbox")));
        args = Arguments(declaration);
        Assert.Equal(0, await LedgerRunner.RunAsync(args));
        Assert.Equal(0, await LedgerRunner.RunAsync(args));
        using var spool = new ReporterSpool(Path.Combine(_directory, "outbox"), 64 * 1024 * 1024, ownsDelivery: false);
        var saved = Assert.Single(spool.Pending());
        Assert.Equal(LedgerScope.SessionItemId("synthetic-machine", "codex", SessionId.ToString("D")), saved.ItemId);
        Assert.Equal("synthetic-reporter", saved.ReporterId);
        Assert.Equal(declaration, saved.Declaration);
        Assert.Equal(declaration.RecordedAtUtc, saved.ObservedAtUtc);
        Assert.Equal(2, await LedgerRunner.RunAsync(Arguments(declaration with { ReviewAfterUtc = declaration.ReviewAfterUtc!.Value.AddHours(1) })));
        Assert.Equal(declaration.ReviewAfterUtc, Assert.Single(spool.Pending()).Declaration!.ReviewAfterUtc);
        var resolution = declaration with { Id = "synthetic-resolution", Resolved = true,
            RecordedAtUtc = declaration.RecordedAtUtc.AddHours(2) };
        Assert.Equal(0, await LedgerRunner.RunAsync(Arguments(resolution)));
        Assert.Contains(spool.Pending(), e => e.Declaration == resolution);
    }

    [Fact]
    public async Task OptionalQuestionGetsIndependentItemAndMissingTurnIdentityIsRejected() {
        var declaration = Declaration() with { Kind = "question", NextActor = "human", Blocking = false };
        Assert.Equal(0, await LedgerRunner.RunAsync(Arguments(declaration)));
        using var spool = new ReporterSpool(Path.Combine(_directory, "outbox"), 64 * 1024 * 1024, ownsDelivery: false);
        var saved = Assert.Single(spool.Pending());
        var parent = LedgerScope.SessionItemId("synthetic-machine", "codex", SessionId.ToString("D"));
        Assert.Equal(parent, saved.ParentItemId);
        Assert.NotEqual(parent, saved.ItemId);
        Assert.Equal("input", saved.ItemKind);
        Assert.Equal(2, await LedgerRunner.RunAsync(Arguments(declaration with { Id = "synthetic-other-declaration", Scope = declaration.Scope with { TurnId = null } })));
        Assert.Single(spool.Pending());
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
