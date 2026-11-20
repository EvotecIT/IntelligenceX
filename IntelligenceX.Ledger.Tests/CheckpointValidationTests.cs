using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CheckpointValidationTests {
    private static readonly WorkCheckpoint Valid = new("CanopyX", "main", new('a', 40), null, "ask", "ask");

    private static string Rejection(WorkCheckpoint checkpoint) =>
        Assert.Throws<CoordinationConflictException>(() => CoordinationValidation.Checkpoint(checkpoint)).Message;

    // Protects callers from guessing: each refused value is named, instead of one code covering every field.
    [Fact]
    public void ARefusedCheckpointNamesTheField() {
        CoordinationValidation.Checkpoint(Valid);
        Assert.Equal("invalid-checkpoint-project", Rejection(Valid with { Project = "EvotecIT/CanopyX" }));
        Assert.Equal("invalid-checkpoint-branch", Rejection(Valid with { Branch = "" }));
        Assert.Equal("invalid-checkpoint-commit", Rejection(Valid with { Commit = "abc1234" }));
        Assert.Equal("invalid-checkpoint-pull-request", Rejection(Valid with { PullRequest = "http://example.com/1" }));
    }

    [Fact]
    public void AnUnpinnedAskReadsAsTheLatestOfTheDefaultBranch() {
        Assert.Equal("its default branch, latest commit", AgentService.Where(Valid with { Branch = "HEAD", Commit = new('0', 40) }));
        Assert.Equal("branch main, commit " + new string('a', 40), AgentService.Where(Valid));
    }
}
