using System.Net;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class GitHubUsageTests : IDisposable {
    private readonly string _directory = LedgerTestPaths.NewDirectory("github-usage");
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 30, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = new(2026, 11, 18, 16, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Answer(HttpStatusCode status, int used, string resource = "core", DateTimeOffset? reset = null) {
        var response = new HttpResponseMessage(status);
        response.Headers.Add("X-RateLimit-Limit", "5000");
        response.Headers.Add("X-RateLimit-Used", used.ToString());
        response.Headers.Add("X-RateLimit-Remaining", (5000 - used).ToString());
        response.Headers.Add("X-RateLimit-Reset", (reset ?? Reset).ToUnixTimeSeconds().ToString());
        response.Headers.Add("X-RateLimit-Resource", resource);
        return response;
    }

    [Fact]
    public void MeterChargesEveryAnswerExceptNotModifiedPerResource() {
        var clock = new FakeTimeProvider(Now);
        var meter = new GitHubUsageMeter(clock);
        meter.Record("catalog", Answer(HttpStatusCode.OK, 10));
        meter.Record("catalog", Answer(HttpStatusCode.NotModified, 10));
        meter.Record("catalog", Answer(HttpStatusCode.NotFound, 11));
        meter.Record("catalog", Answer(HttpStatusCode.OK, 3, "graphql"));
        clock.Advance(TimeSpan.FromMinutes(1));
        meter.Record("catalog", Answer(HttpStatusCode.InternalServerError, 12));

        var report = Assert.Single(meter.Snapshot());
        Assert.Equal(meter.Instance, report.Instance);
        var minute = new DateTimeOffset(2026, 11, 18, 15, 30, 0, TimeSpan.Zero);
        Assert.Equal([new(minute, "core", 2, 1), new(minute, "graphql", 1, 0), new(minute.AddMinutes(1), "core", 1, 0)], report.Minutes);
        var core = Assert.Single(report.RateLimits!, r => r.Resource == "core");
        Assert.Equal((12, 4988, Reset), (core.Used, core.Remaining, core.ResetAtUtc));

        // Minutes older than the retention window are dropped, so a long-running process stays bounded.
        clock.Advance(GitHubUsageMeter.Retention - TimeSpan.FromSeconds(30));
        Assert.Equal(minute.AddMinutes(1), Assert.Single(Assert.Single(meter.Snapshot()).Minutes).MinuteUtc);
    }

    [Fact]
    public async Task HandlerCountsOnlyGitHubApiAnswers() {
        var meter = new GitHubUsageMeter(new FakeTimeProvider(Now));
        using var client = new HttpClient(new GitHubUsageHandler(meter, "reporter", new StubHandler()));
        (await client.GetAsync("https://api.github.com/user")).Dispose();
        (await client.GetAsync("https://example.com/user")).Dispose();
        Assert.Equal(1, Assert.Single(Assert.Single(meter.Snapshot()).Minutes).Charged);
    }

    [Fact]
    public async Task RepeatedReportsReplaceRunningTotalsAndRestartsAdd() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var minute = new DateTimeOffset(2026, 11, 18, 15, 29, 0, TimeSpan.Zero);
        var first = Guid.NewGuid();
        GitHubRateLimitReading Reading(int used) => new("core", 5000, used, 5000 - used, Reset, Now);
        await store.RecordGitHubUsageAsync("win-pc", [new("marquee", first, [new(minute, "core", 5)], [Reading(900)])], Now);
        await store.RecordGitHubUsageAsync("win-pc", [new("marquee", first, [new(minute, "core", 40, 7)], [Reading(950)])], Now);
        await store.RecordGitHubUsageAsync("win-pc", [new("marquee", Guid.NewGuid(), [new(minute, "core", 10)])], Now);
        await store.RecordGitHubUsageAsync("mac", [new("ledger-reporter", Guid.NewGuid(), [new(minute, "core", 25)],
            [Reading(960) with { ObservedAtUtc = Now.AddSeconds(5) }])], Now);

        var view = await store.GitHubBudgetAsync(Now);
        Assert.Equal(["win-pc/marquee", "mac/ledger-reporter"], view.Spenders.Select(s => s.Machine + "/" + s.Component));
        Assert.Equal((50, 50, 7, Reset), (view.Spenders[0].ChargedLastHour, view.Spenders[0].ChargedLast5Minutes,
            view.Spenders[0].NotModifiedLastHour, view.Spenders[0].WindowResetAtUtc!.Value));
        var window = Assert.Single(view.Windows);
        Assert.Equal((960, 75, 885, "mac/ledger-reporter"), (window.Used, window.Reported, window.Unattributed, window.ObservedBy));
        Assert.Equal(75, view.ChargedLastHour);
    }

    [Fact]
    public void ViewSeparatesWindowsOfDifferentIdentitiesAndCountsOnlyTheLastHour() {
        var other = Reset.AddMinutes(-17);
        LedgerStore.GitHubUsageRow Row(string component, int minutesAgo, int charged) =>
            new("win-pc", component, new DateTimeOffset(2026, 11, 18, 15, 30, 0, TimeSpan.Zero).AddMinutes(-minutesAgo), "core", charged, 0);
        var view = LedgerStore.GitHubBudget(Now, [Row("marquee", 2, 30), Row("marquee", 20, 100), Row("marquee", 70, 999), Row("bridge", 1, 4)], [
            ("win-pc", "marquee", new GitHubRateLimitReading("core", 5000, 2000, 3000, Reset, Now)),
            ("win-pc", "bridge", new GitHubRateLimitReading("core", 5000, 40, 4960, other, Now)),
            ("win-pc", "old", new GitHubRateLimitReading("core", 5000, 4000, 1000, Now.AddMinutes(-10), Now.AddMinutes(-40)))]);
        Assert.Equal(130, view.Spenders.Single(s => s.Component == "marquee").ChargedLastHour);
        Assert.Equal(30, view.Spenders.Single(s => s.Component == "marquee").ChargedLast5Minutes);
        Assert.Equal([(Reset, 2000, 1870), (other, 40, 36)], view.Windows.Select(w => (w.ResetAtUtc, w.Used, w.Unattributed)));
        Assert.Contains("several-core-windows:more-than-one-identity-or-token", view.Coverage);
    }

    [Fact]
    public void DropFolderForwardsValidCurrentTalliesOnly() {
        var folder = Path.Combine(_directory, "drop");
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(folder, "marquee.json"), JsonSerializer.Serialize(new GitHubUsageReport("marquee", Guid.NewGuid(),
            [new(minute.AddHours(-3), "core", 9), new(minute, "core", 12, 30)]), LedgerJson.Options));
        File.WriteAllText(Path.Combine(folder, "bad-name.json"), JsonSerializer.Serialize(new GitHubUsageReport("bad name", Guid.NewGuid(),
            [new(minute, "core", 1)]), LedgerJson.Options));
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{");
        var stale = Path.Combine(folder, "stale.json");
        File.WriteAllText(stale, JsonSerializer.Serialize(new GitHubUsageReport("stale", Guid.NewGuid(), [new(minute, "core", 1)]), LedgerJson.Options));
        File.SetLastWriteTimeUtc(stale, now.UtcDateTime.AddHours(-3));

        var report = Assert.Single(GitHubUsageDropFolder.Read(folder, now));
        Assert.Equal("marquee", report.Component);
        Assert.Equal(new GitHubUsageMinute(minute, "core", 12, 30), Assert.Single(report.Minutes));
    }

    private sealed class StubHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(HttpStatusCode.OK, 1));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
