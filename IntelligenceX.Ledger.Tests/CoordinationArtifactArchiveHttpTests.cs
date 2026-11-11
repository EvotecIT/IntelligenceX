using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationArtifactArchiveHttpTests {
    [Fact]
    public async Task ResultPublicationUnderPressureArchivesCompletedHelperAndKeepsOldHttpReplays() {
        var requesterToken = new string('c', 32);
        var helperToken = new string('h', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = Policy,
            Principals = [Requester with { Token = requesterToken }, HelperHost with { Token = helperToken }]
        }, async (endpoint, settings, token) => {
            using var requester = LedgerReadClient.ForCoordination(new(endpoint, requesterToken));
            using var helper = LedgerReadClient.ForCoordination(new(endpoint, helperToken));
            var work = await requester.SubmitAsync(Help(), true, token);
            var binding = new CoordinationSessionBinding("help", work.Generation, work.GrantId!, work.Assignee!,
                work.Machine!, "provider-a", "helper-session", "helper-workspace", Candidate with {
                    CheckpointRef = "helper-recovery-checkpoint", NextActionRef = "helper-follow-up",
                    PullRequest = "https://github.com/example/project/pull/42"
                });
            await helper.BindSessionAsync(binding, token);
            await helper.ReceiptAsync("help", "submitted", Execution(work), token);
            var upload = new CoordinationArtifactCommand(work.Generation, work.GrantId!, "boot", "Completed HTTP answer.");
            var artifact = await helper.PublishResultAsync("help", upload, token);
            await helper.ReceiptAsync("help", "terminal", Execution(work, artifact.ResultRef), token);
            await requester.ConsumeResultAsync("help", new(work.Generation, artifact.ResultRef, Candidate), token);
            foreach (var stage in new[] { "source-preserved", "descendants-resolved", "processes-released", "archived", "workspace-released", "verified" })
                await helper.RetireAsync(new(binding, stage, "proof-" + stage, false, false), token);

            await helper.WorkerAsync(new("next-worker", "helper-host", "provider-a", true, null, DateTimeOffset.UtcNow,
                "next-session", "next-workspace", Candidate, "boot", TemporaryWorker: true), token);
            var next = await requester.SubmitAsync(Help("next") with { WorkerId = "next-worker", ReadOnly = true }, true, token);
            await helper.BindSessionAsync(new("next", next.Generation, next.GrantId!, "next-worker", "helper-host",
                "provider-a", "next-session", "next-workspace", Candidate), token);
            var root = Path.Combine(settings.DataDirectory, "coordination-results");
            CoordinationArtifactArchiveTests.FillActiveBudget(root);
            var published = await helper.PublishResultAsync("next", new(next.Generation, next.GrantId!, "boot", "New HTTP answer."), token);
            Assert.Equal(4096, Directory.GetFiles(root).Length);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "archive")).Length);
            Assert.Equal(artifact, await requester.ResultContentAsync("help", token));
            Assert.Equal(artifact, await helper.PublishResultAsync("help", upload, token));
            Assert.Equal("succeeded", (await helper.ReceiptAsync("help", "terminal", Execution(work, artifact.ResultRef), token)).State);
            await helper.ReceiptAsync("next", "terminal", Execution(next, published.ResultRef), token);
            Assert.Equal(published, await requester.ResultContentAsync("next", token));
        }, Initialize);
    }
}
