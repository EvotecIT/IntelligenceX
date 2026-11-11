using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationVisibilityHttpTests {
    private static readonly CoordinationPrincipal Own = new("own-client", "client", new('a', 32), ["sample"]);
    private static readonly CoordinationPrincipal Other = new("other-client", "client", new('b', 32), ["sample"]);
    private static readonly CoordinationPrincipal Host = new("shared-host", "host", new('h', 32), [], "shared-host");
    private static readonly CoordinationPrincipal Operator = new("operator", "operator", new('o', 32), ["sample"]);

    private static CoordinationConfiguration Enrollment() => new() {
        InitialPolicy = new(1, [new("shared-host", 32, 32, 32, 4096, ["build"])]),
        Principals = [Own, Other, Host, Operator]
    };

    private static LedgerReadClient Client(string endpoint, CoordinationPrincipal principal) =>
        LedgerReadClient.ForCoordination(new(endpoint, principal.Token));

    private static CoordinationRequest Build(string id, CoordinationGoal goal, string branch) => new(
        id, goal.Id, "build", "build", "shared-host", null, new(BuildSlots: 1),
        new("sample", branch, new('a', 40), null, branch + "-source", "next-action"),
        [], goal.ControlRevision, goal.AuthorizationRef);

    [Fact]
    public async Task SummaryScopesPrivateRequestsBeforeCountsAndBoundsWhileRetainingMachineOccupancy() {
        await CoordinationHttpTests.WithService(Enrollment(), async (endpoint, _, token) => {
            using var own = Client(endpoint, Own);
            using var other = Client(endpoint, Other);
            using var admin = Client(endpoint, Operator);
            var summary = await own.CoordinationStatusAsync("sample", token);
            Assert.Equal(1, summary.Counts.Values.Sum());
            Assert.Equal("own-build", Assert.Single(summary.Items).Request.Id);
            Assert.False(summary.Truncated);
            Assert.Equal(18, Assert.Single(summary.Machines).Used.BuildSlots);
            Assert.DoesNotContain("foreign-", JsonSerializer.Serialize(summary));
            var foreign = await other.CoordinationStatusAsync("sample", token);
            Assert.Equal(17, foreign.Counts.Values.Sum());
            Assert.Equal(8, foreign.Items.Length);
            Assert.True(foreign.Truncated);
            Assert.All(foreign.Items, item => Assert.Equal(Other.Id, item.Requester));
            var operatorSummary = await admin.CoordinationStatusAsync("sample", token);
            Assert.Equal(18, operatorSummary.Counts.Values.Sum());
            Assert.True(operatorSummary.Truncated);
        }, (engine, _) => {
            engine.ObserveHost(new("shared-host", "boot", Now, ["build"]), Host);
            var foreignGoal = engine.SetGoal(new("foreign-goal", "", 0, false, false,
                "foreign-authorization", ["result"]), Other, 0);
            for (var i = 0; i < 17; i++) engine.Submit(Build("foreign-build-" + i, foreignGoal, "foreign-" + i), Other);
            var ownGoal = engine.SetGoal(new("own-goal", "", 0, false, false, "own-authorization", ["result"]), Own, 0);
            engine.Submit(Build("own-build", ownGoal, "own-branch"), Own);
        });
    }

    [Fact]
    public async Task GoalChangesFollowTheirOwnerAcrossQueuedPauseStopAndCollidingWorkIds() {
        await CoordinationHttpTests.WithService(Enrollment(), async (endpoint, _, token) => {
            using var own = Client(endpoint, Own);
            using var other = Client(endpoint, Other);
            using var host = Client(endpoint, Host);
            using var admin = Client(endpoint, Operator);
            var mine = await own.ChangesAsync(0, token);
            Assert.Equal(new[] { "goal-updated", "goal-paused", "goal-stopped" }, mine.Changes
                .Where(change => change.RequestId == "shared-id").Select(change => change.Stage));
            Assert.DoesNotContain(mine.Changes, change => change.RequestId == "shared-id" && change.Stage == "requested");
            Assert.Contains(mine.Changes, change => change.RequestId == "own-queued" && change.Stage == "requested");
            Assert.Contains(mine.Changes, change => change.RequestId == "policy" && change.Stage == "requested");
            Assert.DoesNotContain(mine.Changes, change => change.Stage == "policy-updated");
            var theirs = await other.ChangesAsync(0, token);
            Assert.Contains(theirs.Changes, change => change.RequestId == "shared-id" && change.Stage == "requested");
            Assert.DoesNotContain(theirs.Changes, change => change.RequestId == "shared-id" && change.Stage.StartsWith("goal-", StringComparison.Ordinal));
            var hostChanges = await host.ChangesAsync(0, token);
            Assert.Empty(hostChanges.Changes);
            Assert.Equal(mine.NextCursor, hostChanges.NextCursor);
            Assert.Equal(mine.NextCursor, theirs.NextCursor);
            Assert.Empty((await own.ChangesAsync(mine.NextCursor, token)).Changes);
            var all = await admin.ChangesAsync(0, token);
            Assert.Equal(8, all.Changes.Length);
            Assert.Contains(all.Changes, change => change.Stage == "policy-updated");
            Assert.Equal(mine.NextCursor, all.NextCursor);
        }, (engine, state) => {
            var mine = engine.SetGoal(new("shared-id", "", 0, false, false, "own-authorization", ["result"]), Own, 0);
            var other = engine.SetGoal(new("other-goal", "", 0, false, false, "other-authorization", ["result"]), Other, 0);
            engine.Submit(Build("shared-id", other, "other-branch"), Other);
            engine.Submit(Build("own-queued", mine, "own-branch"), Own);
            engine.Submit(Build("policy", mine, "policy-branch"), Own);
            var paused = engine.SetGoal(mine with { Paused = true }, Own, mine.Revision);
            engine.SetGoal(paused with { Stopped = true }, Own, paused.Revision);
            engine.ReplacePolicy(state.Policy with { Revision = state.Policy.Revision + 1 }, Operator, state.Policy.Revision);
        });
    }
}
