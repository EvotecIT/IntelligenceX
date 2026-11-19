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

    // Protects health delivery: a component spending on many resources each minute still sends a report the health
    // route accepts, newest minutes and readings first, instead of getting the whole heartbeat refused.
    [Fact]
    public void SnapshotStaysWithinWhatTheHealthRouteAccepts() {
        var clock = new FakeTimeProvider(Now);
        var meter = new GitHubUsageMeter(clock);
        for (var minute = 0; minute < 85; minute++) {
            for (var resource = 0; resource < 10; resource++) meter.Record("reporter", Answer(HttpStatusCode.OK, 1, "r" + resource));
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        var report = Assert.Single(meter.Snapshot());
        Assert.Equal(GitHubUsageValidation.MaximumMinutes, report.Minutes.Length);
        Assert.Equal(GitHubUsageValidation.MaximumReadings, report.RateLimits!.Length);
        Assert.True(GitHubUsageValidation.Valid([report], clock.GetUtcNow()));
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
        // An older snapshot arriving late (two reporters forwarding one folder) doesn't lower the running total.
        await store.RecordGitHubUsageAsync("win-pc", [new("marquee", first, [new(minute, "core", 20, 3)])], Now);
        await store.RecordGitHubUsageAsync("win-pc", [new("marquee", Guid.NewGuid(), [new(minute, "core", 10)])], Now);
        await store.RecordGitHubUsageAsync("mac", [new("ledger-reporter", Guid.NewGuid(), [new(minute, "core", 25)],
            [Reading(960) with { ObservedAtUtc = Now.AddSeconds(5) }])], Now);

        var view = await store.GitHubBudgetAsync(Now);
        Assert.Equal(["win-pc/marquee", "mac/ledger-reporter"], view.Spenders.Select(s => s.Machine + "/" + s.Component));
        Assert.Equal((50, 50, 7, Reset), (view.Spenders[0].ChargedLastHour, view.Spenders[0].ChargedLast5Minutes,
            view.Spenders[0].NotModifiedLastHour, view.Spenders[0].WindowResetAtUtc!.Value));
        var window = Assert.Single(view.Windows);
        // The restarted run sent no reading yet, so its 10 requests are not placed in any window.
        Assert.Equal((960, 65, 895, "mac/ledger-reporter"), (window.Used, window.Reported, window.Unattributed, window.ObservedBy));
        Assert.Equal(75, view.ChargedLastHour);
    }

    [Fact]
    public void ViewSeparatesWindowsOfDifferentIdentitiesAndCountsOnlyTheLastHour() {
        var other = Reset.AddMinutes(-17);
        LedgerStore.GitHubUsageRow Row(string component, int minutesAgo, int charged) =>
            new("win-pc", component, component, new DateTimeOffset(2026, 11, 18, 15, 30, 0, TimeSpan.Zero).AddMinutes(-minutesAgo), "core", charged, 0);
        var view = LedgerStore.GitHubBudget(Now, [Row("marquee", 2, 30), Row("marquee", 20, 100), Row("marquee", 70, 999), Row("bridge", 1, 4)], [
            new("win-pc", "marquee", "marquee", new GitHubRateLimitReading("core", 5000, 2000, 3000, Reset, Now)),
            new("win-pc", "bridge", "bridge", new GitHubRateLimitReading("core", 5000, 40, 4960, other, Now)),
            new("win-pc", "old", "old", new GitHubRateLimitReading("core", 5000, 4000, 1000, Now.AddMinutes(-10), Now.AddMinutes(-40)))]);
        Assert.Equal(130, view.Spenders.Single(s => s.Component == "marquee").ChargedLastHour);
        Assert.Equal(134, view.ChargedLastHour);
        Assert.Equal(30, view.Spenders.Single(s => s.Component == "marquee").ChargedLast5Minutes);
        Assert.Equal([(Reset, 2000, 1870), (other, 40, 36)], view.Windows.Select(w => (w.ResetAtUtc, w.Used, w.Unattributed)));
        Assert.Contains("several-core-windows:more-than-one-identity-or-token", view.Coverage);
    }

    // Protects heartbeat delivery: forwarded usage stays within a byte budget, so large but valid tallies never push
    // the heartbeat past the service's request limit.
    [Fact]
    public void AHeartbeatCarriesOnlyTheUsageThatFits() {
        var minute = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
        var name = new string('c', 64);
        var large = Enumerable.Range(0, GitHubUsageValidation.MaximumReports).Select(index => new GitHubUsageReport(name[..60] + index.ToString("00"), Guid.NewGuid(),
            Enumerable.Range(0, GitHubUsageValidation.MaximumMinutes).Select(m => new GitHubUsageMinute(minute.AddMinutes(m), name, 99_999, 99_999)).ToArray())).ToArray();
        var fitted = GitHubUsageValidation.Fit([], large);
        Assert.InRange(fitted.Length, 1, GitHubUsageValidation.MaximumReports - 1);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(fitted, LedgerJson.Options).Length <= GitHubUsageValidation.MaximumHeartbeatBytes + 1024);
        // Later heartbeats take turns, so every report gets through in time.
        var delivered = Enumerable.Range(0, large.Length).SelectMany(turn => GitHubUsageValidation.Fit([], large, turn)).Select(r => r.Component).ToHashSet();
        Assert.Equal(large.Length, delivered.Count);
    }

    // Protects the fleet total: listing only the top spenders never understates what was reported, and says so.
    [Fact]
    public void TheHourlyTotalCountsSpendersBeyondTheListedOnes() {
        var rows = Enumerable.Range(0, 40).Select(index => new LedgerStore.GitHubUsageRow("win-pc", "tool" + index, "i" + index,
            new DateTimeOffset(2026, 11, 18, 15, 20, 0, TimeSpan.Zero), "core", 1, 0)).ToArray();
        var view = LedgerStore.GitHubBudget(Now, rows, []);
        Assert.Equal((32, 40), (view.Spenders.Length, view.ChargedLastHour));
        Assert.Contains("spenders-listed:32-of-40", view.Coverage);
    }

    [Fact]
    public void WindowsFollowEachRunsAccountAndEachResourcesLength() {
        var at = new DateTimeOffset(2026, 11, 18, 15, 30, 0, TimeSpan.Zero);
        LedgerStore.GitHubUsageRow Row(string instance, string resource, int minutesAgo, int charged) =>
            new("win-pc", "ledger-reporter", instance, at.AddMinutes(-minutesAgo), resource, charged, 0);
        var otherAccount = Reset.AddMinutes(-20);
        var view = LedgerStore.GitHubBudget(Now, [Row("old", "core", 10, 100), Row("new", "core", 1, 1),
            Row("new", "search", 20, 10), Row("new", "search", 0, 1), Row("new", "graphql", 1, 1)], [
            new("win-pc", "ledger-reporter", "old", new GitHubRateLimitReading("core", 5000, 300, 4700, otherAccount, Now.AddMinutes(-9))),
            new("win-pc", "ledger-reporter", "new", new GitHubRateLimitReading("core", 5000, 7, 4993, Reset, Now)),
            new("win-pc", "ledger-reporter", "new", new GitHubRateLimitReading("search", 30, 3, 27, Now.AddSeconds(30), Now)),
            new("win-pc", "ledger-reporter", "new", new GitHubRateLimitReading("graphql", 5000, 5, 4995, Reset, Now))]);
        Assert.Equal([(otherAccount, 100, 200), (Reset, 1, 6)],
            view.Windows.Where(w => w.Resource == "core").Select(w => (w.ResetAtUtc, w.Reported, w.Unattributed)));
        // A search in the minute just before the window belongs to the window before it.
        var adjacent = LedgerStore.GitHubBudget(Now, [Row("new", "search", 1, 20), Row("new", "search", 0, 1)], [
            new("win-pc", "ledger-reporter", "new", new GitHubRateLimitReading("search", 30, 5, 25, at.AddMinutes(1), Now))]);
        Assert.Equal((1, 4), adjacent.Windows.Select(w => (w.Reported, w.Unattributed)).Single());
        // Search resets every minute, so the search from twenty minutes ago belongs to an earlier window.
        Assert.Equal((1, 2), view.Windows.Where(w => w.Resource == "search").Select(w => (w.Reported, w.Unattributed)).Single());
        // GraphQL's Used counts points, which can't be compared with requests.
        Assert.Equal(0, view.Windows.Single(w => w.Resource == "graphql").Unattributed);
    }

    [Fact]
    public async Task AnEmptyReportStillRemovesOldTallies() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.RecordGitHubUsageAsync("win-pc", [new("marquee", Guid.NewGuid(), [new(Now.AddMinutes(-30), "core", 5)],
            [new("core", 5000, 5, 4995, Reset, Now.AddMinutes(-30))])], Now.AddMinutes(-30));
        await store.RecordGitHubUsageAsync("win-pc", [], Now.AddHours(3));
        var view = await store.GitHubBudgetAsync(Now.AddHours(-1));
        Assert.Empty(view.Spenders);
        Assert.Empty(view.Windows);
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
        // Stale files and invalid ones that sort first don't use up the limits before the valid one is read.
        for (var index = 0; index < GitHubUsageValidation.MaximumReports * 5; index++) {
            var old = Path.Combine(folder, $"0{index:000}.json");
            File.WriteAllText(old, "{");
            File.SetLastWriteTimeUtc(old, now.UtcDateTime.AddHours(-3));
        }
        for (var index = 0; index < GitHubUsageValidation.MaximumReports; index++) File.WriteAllText(Path.Combine(folder, $"a{index:00}.json"), "{");

        var report = Assert.Single(GitHubUsageDropFolder.Read(folder, now));
        Assert.Equal("marquee", report.Component);
        Assert.Equal(new GitHubUsageMinute(minute, "core", 12, 30), Assert.Single(report.Minutes));
        Assert.Empty(GitHubUsageDropFolder.Read(Path.Combine(folder, "missing"), now));

        // With more current files than one heartbeat reads, successive heartbeats reach every one.
        var many = Path.Combine(_directory, "many");
        Directory.CreateDirectory(many);
        for (var index = 0; index < GitHubUsageValidation.MaximumReports + 4; index++)
            File.WriteAllText(Path.Combine(many, $"tool{index:00}.json"), JsonSerializer.Serialize(new GitHubUsageReport($"tool{index:00}", Guid.NewGuid(),
                [new(minute, "core", 1)]), LedgerJson.Options));
        var seen = Enumerable.Range(0, GitHubUsageValidation.MaximumReports + 4).SelectMany(turn => GitHubUsageDropFolder.Read(many, now, turn))
            .Select(report => report.Component).ToHashSet();
        Assert.Equal(GitHubUsageValidation.MaximumReports + 4, seen.Count);
    }

    private sealed class StubHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(HttpStatusCode.OK, 1));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
