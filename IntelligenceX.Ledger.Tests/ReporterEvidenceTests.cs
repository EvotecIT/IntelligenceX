using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ReporterEvidenceTests {
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
