using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static readonly TimeSpan GitHubUsageKept = TimeSpan.FromHours(2);

    private static async Task InitializeGitHubUsageAsync(SQLiteAsyncSession db, CancellationToken token) =>
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS github_usage(machine TEXT NOT NULL,component TEXT NOT NULL,instance TEXT NOT NULL,
              minute TEXT NOT NULL,resource TEXT NOT NULL,charged INTEGER NOT NULL,not_modified INTEGER NOT NULL,
              PRIMARY KEY(machine,component,instance,minute,resource));
            CREATE INDEX IF NOT EXISTS github_usage_minute ON github_usage(minute);
            CREATE TABLE IF NOT EXISTS github_rate(machine TEXT NOT NULL,component TEXT NOT NULL,resource TEXT NOT NULL,
              observed TEXT NOT NULL,json TEXT NOT NULL,PRIMARY KEY(machine,component,resource));
            """, cancellationToken: token);

    /// <summary>Stores running per-minute totals. A repeated report for the same process replaces its earlier counts.</summary>
    public async Task RecordGitHubUsageAsync(string machine, GitHubUsageReport[] reports, DateTimeOffset now, CancellationToken token = default) {
        if (reports.Length == 0) return;
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            await db.RunInTransactionAsync(async (tx, ct) => await RecordGitHubUsageAsync(tx, machine, reports, now, ct), token);
        } finally { _gate.Release(); }
    }

    private static async Task RecordGitHubUsageAsync(SQLiteAsyncSession db, string machine, GitHubUsageReport[] reports, DateTimeOffset now,
        CancellationToken token) {
        foreach (var report in reports) {
            foreach (var minute in report.Minutes)
                await db.ExecuteNonQueryAsync("""
                    INSERT INTO github_usage(machine,component,instance,minute,resource,charged,not_modified)
                    VALUES(@machine,@component,@instance,@minute,@resource,@charged,@free)
                    ON CONFLICT(machine,component,instance,minute,resource) DO UPDATE SET charged=excluded.charged,not_modified=excluded.not_modified
                    """, new Dictionary<string, object?> { ["machine"] = machine, ["component"] = report.Component,
                        ["instance"] = report.Instance.ToString("N"), ["minute"] = minute.MinuteUtc.ToUniversalTime().ToString("O"),
                        ["resource"] = minute.Resource, ["charged"] = minute.Charged, ["free"] = minute.NotModified }, token);
            foreach (var reading in report.RateLimits ?? [])
                await db.ExecuteNonQueryAsync("""
                    INSERT INTO github_rate(machine,component,resource,observed,json) VALUES(@machine,@component,@resource,@observed,@json)
                    ON CONFLICT(machine,component,resource) DO UPDATE SET observed=excluded.observed,json=excluded.json
                    WHERE excluded.observed>=github_rate.observed
                    """, new Dictionary<string, object?> { ["machine"] = machine, ["component"] = report.Component, ["resource"] = reading.Resource,
                        ["observed"] = reading.ObservedAtUtc.ToUniversalTime().ToString("O"), ["json"] = Json(reading) }, token);
        }
        var cutoff = (now - GitHubUsageKept).ToUniversalTime().ToString("O");
        await db.ExecuteNonQueryAsync("DELETE FROM github_usage WHERE minute<@cutoff; DELETE FROM github_rate WHERE observed<@cutoff;",
            new Dictionary<string, object?> { ["cutoff"] = cutoff }, token);
    }

    /// <summary>
    /// Charged requests per component and machine for the last hour and the last five minutes, and each live rate-limit
    /// window with the share no component reported.
    /// </summary>
    public async Task<GitHubBudgetView> GitHubBudgetAsync(DateTimeOffset now, CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var hour = now.AddHours(-1).ToUniversalTime().ToString("O");
        var minutes = await db.QueryAsListAsync("SELECT machine,component,minute,resource,SUM(charged),SUM(not_modified) FROM github_usage " +
            "WHERE minute>=@from GROUP BY machine,component,minute,resource",
            row => (Machine: row.GetString(0), Component: row.GetString(1), Minute: DateTimeOffset.Parse(row.GetString(2)), Resource: row.GetString(3),
                Charged: row.GetInt32(4), Free: row.GetInt32(5)),
            new Dictionary<string, object?> { ["from"] = now.AddHours(-2).ToUniversalTime().ToString("O") }, cancellationToken: token);
        var readings = await db.QueryAsListAsync("SELECT machine,component,json FROM github_rate",
            row => (Machine: row.GetString(0), Component: row.GetString(1), Reading: Read<GitHubRateLimitReading>(row.GetString(2))), cancellationToken: token);
        return GitHubBudget(now, minutes.Select(m => new GitHubUsageRow(m.Machine, m.Component, m.Minute, m.Resource, m.Charged, m.Free)).ToArray(),
            readings.Select(r => (r.Machine, r.Component, r.Reading)).ToArray());
    }

    internal sealed record GitHubUsageRow(string Machine, string Component, DateTimeOffset Minute, string Resource, int Charged, int NotModified);

    internal static GitHubBudgetView GitHubBudget(DateTimeOffset now, GitHubUsageRow[] minutes,
        (string Machine, string Component, GitHubRateLimitReading Reading)[] readings) {
        var hourAgo = now.AddHours(-1);
        var recent = now.AddMinutes(-5);
        DateTimeOffset? WindowOf(string machine, string component) => readings
            .Where(r => r.Machine == machine && r.Component == component && r.Reading.Resource == "core").Select(r => (DateTimeOffset?)r.Reading.ResetAtUtc).FirstOrDefault();
        var spenders = minutes.Where(m => m.Resource == "core" && m.Minute >= hourAgo).GroupBy(m => (m.Machine, m.Component))
            .Select(group => new GitHubBudgetSpender(group.Key.Machine, group.Key.Component, group.Sum(m => m.Charged),
                group.Where(m => m.Minute >= recent).Sum(m => m.Charged), group.Sum(m => m.NotModified), group.Max(m => m.Minute)) {
                WindowResetAtUtc = WindowOf(group.Key.Machine, group.Key.Component)
            })
            .OrderByDescending(s => s.ChargedLastHour).ThenBy(s => s.Machine, StringComparer.Ordinal).ThenBy(s => s.Component, StringComparer.Ordinal)
            .Take(32).ToArray();
        // One window per resource and reset time. Requests with another identity's token land in a window of their own.
        var windows = readings.Where(r => r.Reading.ResetAtUtc > now.AddMinutes(-1))
            .GroupBy(r => (r.Reading.Resource, r.Reading.ResetAtUtc))
            .Select(group => {
                var latest = group.OrderByDescending(r => r.Reading.ObservedAtUtc).ThenByDescending(r => r.Reading.Used).First();
                var start = group.Key.ResetAtUtc.AddHours(-1);
                var members = group.Select(r => (r.Machine, r.Component)).ToHashSet();
                var reported = minutes.Where(m => m.Resource == group.Key.Resource && members.Contains((m.Machine, m.Component)) &&
                    m.Minute >= start.AddMinutes(-1) && m.Minute <= latest.Reading.ObservedAtUtc).Sum(m => m.Charged);
                return new GitHubBudgetWindow(group.Key.Resource, latest.Reading.Limit, latest.Reading.Used, latest.Reading.Remaining,
                    group.Key.ResetAtUtc, latest.Reading.ObservedAtUtc, latest.Machine + "/" + latest.Component, reported,
                    Math.Max(0, latest.Reading.Used - reported),
                    members.Select(m => m.Machine + "/" + m.Component).Order(StringComparer.Ordinal).ToArray());
            })
            .OrderBy(w => w.Resource == "core" ? 0 : 1).ThenByDescending(w => w.Used).Take(8).ToArray();
        var coverage = new List<string> { "charged-excludes-304", "minute-granularity", "unattributed=window-used-minus-reported" };
        var other = minutes.Where(m => m.Resource != "core" && m.Minute >= hourAgo).GroupBy(m => m.Resource)
            .Select(group => "other-resource:" + group.Key + "=" + group.Sum(m => m.Charged));
        coverage.AddRange(other);
        if (windows.Count(w => w.Resource == "core") > 1) coverage.Add("several-core-windows:more-than-one-identity-or-token");
        if (windows.Length == 0) coverage.Add("no-rate-limit-reading");
        return new(now, windows, spenders, coverage.ToArray());
    }
}
