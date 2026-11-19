using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static readonly TimeSpan GitHubUsageKept = TimeSpan.FromHours(2);

    // Readings are kept per process: a restart under another account keeps its own window instead of replacing the old one.
    private static async Task InitializeGitHubUsageAsync(SQLiteAsyncSession db, CancellationToken token) =>
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS github_usage(machine TEXT NOT NULL,component TEXT NOT NULL,instance TEXT NOT NULL,
              minute TEXT NOT NULL,resource TEXT NOT NULL,charged INTEGER NOT NULL,not_modified INTEGER NOT NULL,
              PRIMARY KEY(machine,component,instance,minute,resource));
            CREATE INDEX IF NOT EXISTS github_usage_minute ON github_usage(minute);
            DROP TABLE IF EXISTS github_rate;
            CREATE TABLE IF NOT EXISTS github_rate_instance(machine TEXT NOT NULL,component TEXT NOT NULL,instance TEXT NOT NULL,
              resource TEXT NOT NULL,observed TEXT NOT NULL,json TEXT NOT NULL,PRIMARY KEY(machine,component,instance,resource));
            """, cancellationToken: token);

    /// <summary>
    /// Stores running per-minute totals. A repeated report for the same process keeps the larger count, since totals only
    /// grow and an older snapshot may arrive late (two reporters forwarding one drop folder). An empty
    /// report still removes tallies older than two hours.
    /// </summary>
    public async Task RecordGitHubUsageAsync(string machine, GitHubUsageReport[] reports, DateTimeOffset now, CancellationToken token = default) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            await db.RunInTransactionAsync(async (tx, ct) => await RecordGitHubUsageAsync(tx, machine, reports, now, ct), token);
        } finally { _gate.Release(); }
    }

    private static async Task RecordGitHubUsageAsync(SQLiteAsyncSession db, string machine, GitHubUsageReport[] reports, DateTimeOffset now,
        CancellationToken token) {
        foreach (var report in reports) {
            var instance = report.Instance.ToString("N");
            foreach (var minute in report.Minutes)
                await db.ExecuteNonQueryAsync("""
                    INSERT INTO github_usage(machine,component,instance,minute,resource,charged,not_modified)
                    VALUES(@machine,@component,@instance,@minute,@resource,@charged,@free)
                    ON CONFLICT(machine,component,instance,minute,resource) DO UPDATE SET charged=MAX(charged,excluded.charged),not_modified=MAX(not_modified,excluded.not_modified)
                    """, new Dictionary<string, object?> { ["machine"] = machine, ["component"] = report.Component,
                        ["instance"] = instance, ["minute"] = minute.MinuteUtc.ToUniversalTime().ToString("O"),
                        ["resource"] = minute.Resource, ["charged"] = minute.Charged, ["free"] = minute.NotModified }, token);
            foreach (var reading in report.RateLimits ?? [])
                await db.ExecuteNonQueryAsync("""
                    INSERT INTO github_rate_instance(machine,component,instance,resource,observed,json)
                    VALUES(@machine,@component,@instance,@resource,@observed,@json)
                    ON CONFLICT(machine,component,instance,resource) DO UPDATE SET observed=excluded.observed,json=excluded.json
                    WHERE excluded.observed>=github_rate_instance.observed
                    """, new Dictionary<string, object?> { ["machine"] = machine, ["component"] = report.Component, ["instance"] = instance,
                        ["resource"] = reading.Resource, ["observed"] = reading.ObservedAtUtc.ToUniversalTime().ToString("O"), ["json"] = Json(reading) }, token);
        }
        var cutoff = (now - GitHubUsageKept).ToUniversalTime().ToString("O");
        await db.ExecuteNonQueryAsync("DELETE FROM github_usage WHERE minute<@cutoff; DELETE FROM github_rate_instance WHERE observed<@cutoff;",
            new Dictionary<string, object?> { ["cutoff"] = cutoff }, token);
    }

    /// <summary>
    /// Charged requests per component and machine for the last hour and the last five minutes, and each live rate-limit
    /// window with the share no component reported.
    /// </summary>
    public async Task<GitHubBudgetView> GitHubBudgetAsync(DateTimeOffset now, CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        // One snapshot, so a report committed between the two reads can't pair old counts with new readings.
        var (minutes, readings) = await db.RunInTransactionAsync(SQLiteTransactionMode.Deferred, async (tx, ct) => (
            await tx.QueryAsListAsync("SELECT machine,component,instance,minute,resource,charged,not_modified FROM github_usage WHERE minute>=@from",
                row => new GitHubUsageRow(row.GetString(0), row.GetString(1), row.GetString(2), DateTimeOffset.Parse(row.GetString(3)),
                    row.GetString(4), row.GetInt32(5), row.GetInt32(6)),
                new Dictionary<string, object?> { ["from"] = now.AddHours(-2).ToUniversalTime().ToString("O") }, cancellationToken: ct),
            await tx.QueryAsListAsync("SELECT machine,component,instance,json FROM github_rate_instance",
                row => new GitHubRateRow(row.GetString(0), row.GetString(1), row.GetString(2), Read<GitHubRateLimitReading>(row.GetString(3))),
                cancellationToken: ct)), token);
        return GitHubBudget(now, minutes.ToArray(), readings.ToArray());
    }

    internal sealed record GitHubUsageRow(string Machine, string Component, string Instance, DateTimeOffset Minute, string Resource, int Charged, int NotModified);

    internal sealed record GitHubRateRow(string Machine, string Component, string Instance, GitHubRateLimitReading Reading);

    /// <summary>
    /// How long one window of a resource lasts. Search limits reset every minute; core and the rest reset hourly.
    /// </summary>
    internal static TimeSpan GitHubWindowLength(string resource) => resource is "search" or "code_search" ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);

    internal static GitHubBudgetView GitHubBudget(DateTimeOffset now, GitHubUsageRow[] minutes, GitHubRateRow[] readings) {
        var hourAgo = now.AddHours(-1);
        var recent = now.AddMinutes(-5);
        DateTimeOffset? WindowOf(string machine, string component) => readings
            .Where(r => r.Machine == machine && r.Component == component && r.Reading.Resource == "core")
            .OrderByDescending(r => r.Reading.ObservedAtUtc).Select(r => (DateTimeOffset?)r.Reading.ResetAtUtc).FirstOrDefault();
        var hourly = minutes.Where(m => m.Resource == "core" && m.Minute >= hourAgo).ToArray();
        var spenders = hourly.GroupBy(m => (m.Machine, m.Component))
            .Select(group => new GitHubBudgetSpender(group.Key.Machine, group.Key.Component, group.Sum(m => m.Charged),
                group.Where(m => m.Minute >= recent).Sum(m => m.Charged), group.Sum(m => m.NotModified), group.Max(m => m.Minute)) {
                WindowResetAtUtc = WindowOf(group.Key.Machine, group.Key.Component)
            })
            .OrderByDescending(s => s.ChargedLastHour).ThenBy(s => s.Machine, StringComparer.Ordinal).ThenBy(s => s.Component, StringComparer.Ordinal)
            .ToArray();
        var listed = spenders.Take(32).ToArray();
        // One window per resource and reset time. Each process's requests count toward the window its own readings name,
        // so requests with another identity's token land in a window of their own.
        var windows = readings.Where(r => r.Reading.ResetAtUtc > now.AddMinutes(-1))
            .GroupBy(r => (r.Reading.Resource, r.Reading.ResetAtUtc))
            .Select(group => {
                var latest = group.OrderByDescending(r => r.Reading.ObservedAtUtc).ThenByDescending(r => r.Reading.Used).First();
                var start = group.Key.ResetAtUtc - GitHubWindowLength(group.Key.Resource);
                // The minute bucket holding the window's start; buckets wholly before it belong to an earlier window.
                var first = new DateTimeOffset(start.UtcDateTime.Ticks - start.UtcDateTime.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
                var members = group.Select(r => (r.Machine, r.Component, r.Instance)).ToHashSet();
                var reported = minutes.Where(m => m.Resource == group.Key.Resource && members.Contains((m.Machine, m.Component, m.Instance)) &&
                    m.Minute >= first && m.Minute <= latest.Reading.ObservedAtUtc).Sum(m => m.Charged);
                // GraphQL's Used counts points, not requests, so the two can't be subtracted.
                var unattributed = group.Key.Resource == "graphql" ? 0 : Math.Max(0, latest.Reading.Used - reported);
                return new GitHubBudgetWindow(group.Key.Resource, latest.Reading.Limit, latest.Reading.Used, latest.Reading.Remaining,
                    group.Key.ResetAtUtc, latest.Reading.ObservedAtUtc, latest.Machine + "/" + latest.Component, reported, unattributed,
                    members.Select(m => m.Machine + "/" + m.Component).Distinct().Order(StringComparer.Ordinal).ToArray());
            })
            .OrderBy(w => w.Resource == "core" ? 0 : 1).ThenByDescending(w => w.Used).Take(8).ToArray();
        var coverage = new List<string> { "charged-excludes-304", "minute-granularity", "unattributed=window-used-minus-reported" };
        if (windows.Any(w => w.Resource == "graphql")) coverage.Add("graphql-used-is-points:unattributed-not-computed");
        var other = minutes.Where(m => m.Resource != "core" && m.Minute >= hourAgo).GroupBy(m => m.Resource)
            .Select(group => "other-resource:" + group.Key + "=" + group.Sum(m => m.Charged));
        coverage.AddRange(other);
        if (windows.Count(w => w.Resource == "core") > 1) coverage.Add("several-core-windows:more-than-one-identity-or-token");
        if (windows.Length == 0) coverage.Add("no-rate-limit-reading");
        if (listed.Length < spenders.Length) coverage.Add("spenders-listed:" + listed.Length + "-of-" + spenders.Length);
        return new(now, windows, listed, coverage.ToArray()) { ChargedLastHour = hourly.Sum(m => m.Charged) };
    }
}
