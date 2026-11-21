using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationProjectAccessTests {
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task AProjectTheOperatorAddsIsUsableAtOnceWithoutARestart() {
        var sessionsToken = new string('s', 32);
        var operatorToken = new string('o', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 1, 1, 4, 4096, ["macos-shell"], AllowedCreateProviders: ["codex"])]),
            Principals = [
                new("sessions", "client", sessionsToken, ["sample"], "synthetic-machine"),
                new("operator", "operator", operatorToken, ["sample"])
            ]
        }, async (endpoint, _, token) => {
            using var sessions = LedgerReadClient.ForCoordination(new(endpoint, sessionsToken));
            using var operatorClient = LedgerReadClient.ForCoordination(new(endpoint, operatorToken));
            CoordinationAsk Ask(string id) => new(id, "Check the build.", "NewRepository", "main", Commit, "codex", "requester-thread",
                "synthetic-machine");

            var refused = await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() => sessions.AskAsync(Ask("before"), token));
            Assert.Equal("project-not-authorized", refused.ErrorCode);

            var policy = await operatorClient.PolicyAsync(token);
            policy = await operatorClient.SetPolicyValueAsync(new("projects", JsonSerializer.SerializeToElement(new[] { "NewRepository" }),
                policy.Revision), token);
            Assert.Equal(["NewRepository"], policy.Projects);
            // The same running service and the same credential: the next ask in that repository is accepted.
            Assert.Equal("NewRepository", (await sessions.AskAsync(Ask("after"), token)).Request.Checkpoint.Project);

            var invalid = await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() => operatorClient.SetPolicyValueAsync(
                new("projects", JsonSerializer.SerializeToElement(new[] { "not a name" }), policy.Revision), token));
            Assert.Equal("invalid-project-policy", invalid.ErrorCode);
        });
    }

    [Fact]
    public void AddedProjectsJoinAConfiguredListWithoutRepeatsOrChangingIt() {
        var principal = new CoordinationPrincipal("sessions", "client", "unused", ["sample"], "pc");
        Assert.Same(principal, Hosting.CoordinationEndpoints.WithAddedProjects(principal, []));
        Assert.Same(principal, Hosting.CoordinationEndpoints.WithAddedProjects(principal, ["sample"]));
        Assert.Equal(["sample", "NewRepository"], Hosting.CoordinationEndpoints.WithAddedProjects(principal, ["NewRepository", "sample"]).Projects);
        Assert.Equal(["sample"], principal.Projects);
    }
}
