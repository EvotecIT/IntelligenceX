using System.Net;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>
/// Counts this process's GitHub REST answers per component, minute and rate-limit resource, and keeps the last
/// rate-limit headers each component saw. GitHub gives no per-client breakdown, so each caller reports its own share.
/// </summary>
internal sealed class GitHubUsageMeter(TimeProvider? clock = null) {
    internal static readonly TimeSpan Retention = TimeSpan.FromMinutes(90);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<(DateTimeOffset Minute, string Resource), (int Charged, int NotModified)>> _minutes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, GitHubRateLimitReading>> _readings = new(StringComparer.Ordinal);
    internal Guid Instance { get; } = Guid.NewGuid();

    /// <summary>Counts one answer. A 304 is free; every other answer from api.github.com is charged.</summary>
    internal void Record(string component, HttpResponseMessage response) {
        var now = _clock.GetUtcNow();
        var reading = Reading(response, now);
        var resource = reading?.Resource ?? "core";
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var free = response.StatusCode == HttpStatusCode.NotModified;
        lock (_gate) {
            if (!_minutes.TryGetValue(component, out var minutes)) _minutes[component] = minutes = [];
            minutes.TryGetValue((minute, resource), out var count);
            minutes[(minute, resource)] = free ? (count.Charged, count.NotModified + 1) : (count.Charged + 1, count.NotModified);
            if (reading is not null) {
                if (!_readings.TryGetValue(component, out var readings)) _readings[component] = readings = new(StringComparer.Ordinal);
                readings[reading.Resource] = reading;
            }
        }
    }

    /// <summary>Each component's running totals for the retained minutes, ready to send or save.</summary>
    internal GitHubUsageReport[] Snapshot() {
        var cutoff = _clock.GetUtcNow() - Retention;
        lock (_gate) {
            foreach (var minutes in _minutes.Values)
                foreach (var key in minutes.Keys.Where(key => key.Minute < cutoff).ToArray()) minutes.Remove(key);
            return _minutes.Where(pair => pair.Value.Count > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new GitHubUsageReport(
                pair.Key, Instance,
                // Within what the health route accepts, so a busy component can never get a whole health report refused.
                pair.Value.OrderBy(entry => entry.Key.Minute).ThenBy(entry => entry.Key.Resource, StringComparer.Ordinal)
                    .TakeLast(GitHubUsageValidation.MaximumMinutes)
                    .Select(entry => new GitHubUsageMinute(entry.Key.Minute, entry.Key.Resource, entry.Value.Charged, entry.Value.NotModified)).ToArray(),
                _readings.TryGetValue(pair.Key, out var readings) ? readings.Values.Where(r => r.ObservedAtUtc >= cutoff)
                    .OrderByDescending(r => r.ObservedAtUtc).Take(GitHubUsageValidation.MaximumReadings)
                    .OrderBy(r => r.Resource, StringComparer.Ordinal).ToArray() : null)).ToArray();
        }
    }

    internal static GitHubRateLimitReading? Reading(HttpResponseMessage response, DateTimeOffset now) {
        static string? Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        if (!int.TryParse(Header(response, "X-RateLimit-Limit"), out var limit) ||
            !int.TryParse(Header(response, "X-RateLimit-Remaining"), out var remaining) ||
            !long.TryParse(Header(response, "X-RateLimit-Reset"), out var reset)) return null;
        var used = int.TryParse(Header(response, "X-RateLimit-Used"), out var value) ? value : Math.Max(0, limit - remaining);
        var resource = Header(response, "X-RateLimit-Resource") is { Length: > 0 and <= 40 } named && GitHubUsageValidation.Name(named) ? named : "core";
        return new(resource, limit, used, remaining, DateTimeOffset.FromUnixTimeSeconds(reset), now);
    }
}

/// <summary>Counts every answer from api.github.com that passes through the client it wraps.</summary>
internal sealed class GitHubUsageHandler(GitHubUsageMeter meter, string component, HttpMessageHandler inner) : DelegatingHandler(inner) {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var response = await base.SendAsync(request, cancellationToken);
        // Requests signed as a GitHub App spend the App's budget, so they are counted apart, with the App's own readings.
        var signedByApp = request.Options.TryGetValue(IntelligenceX.Telemetry.GitHub.GitHubAppAuthHandler.SignedByApp, out var app) && app;
        if (request.RequestUri is { Host: "api.github.com" }) meter.Record(signedByApp ? component + "-app" : component, response);
        return response;
    }
}

/// <summary>Bounds every tally before it is stored, whoever sent it.</summary>
internal static class GitHubUsageValidation {
    internal const int MaximumReports = 16;
    internal const int MaximumMinutes = 400;
    internal const int MaximumReadings = 8;

    /// <summary>The most usage a heartbeat carries, well inside the service's request limit, so it never costs the heartbeat.</summary>
    internal const int MaximumHeartbeatBytes = 256 * 1024;

    /// <summary>
    /// The reports a heartbeat can carry: at most <see cref="MaximumReports"/>, within <see cref="MaximumHeartbeatBytes"/>.
    /// The reporter's own come first; the forwarded ones start at <paramref name="turn"/>, so when they don't all fit,
    /// successive heartbeats take turns and every report that fits on its own still gets through.
    /// </summary>
    internal static GitHubUsageReport[] Fit(IReadOnlyList<GitHubUsageReport> own, IReadOnlyList<GitHubUsageReport> forwarded, int turn = 0) {
        var start = forwarded.Count == 0 ? 0 : (int)((uint)turn % (uint)forwarded.Count);
        var ordered = own.Concat(forwarded.Skip(start)).Concat(forwarded.Take(start));
        var fitted = new List<GitHubUsageReport>();
        var bytes = 0;
        foreach (var report in ordered) {
            if (fitted.Count == MaximumReports) break;
            var size = JsonSerializer.SerializeToUtf8Bytes(report, LedgerJson.Options).Length;
            if (bytes + size > MaximumHeartbeatBytes) continue;
            bytes += size;
            fitted.Add(report);
        }
        return fitted.ToArray();
    }

    internal static bool Name(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    internal static bool Valid(GitHubUsageReport[]? reports, DateTimeOffset now) => reports is null ||
        reports.Length <= MaximumReports && reports.All(report => report is not null && Name(report.Component) &&
            report.Instance != Guid.Empty && report.Minutes is { Length: <= MaximumMinutes } &&
            report.Minutes.All(minute => minute is not null && Name(minute.Resource) && minute.Charged is >= 0 and <= 100_000 &&
                minute.NotModified is >= 0 and <= 100_000 && minute.MinuteUtc.Offset == TimeSpan.Zero &&
                minute.MinuteUtc.Second == 0 && minute.MinuteUtc.Millisecond == 0 && minute.MinuteUtc <= now.AddMinutes(2)) &&
            (report.RateLimits is null || report.RateLimits.Length <= MaximumReadings && report.RateLimits.All(reading => reading is not null &&
                Name(reading.Resource) && reading.Limit is > 0 and <= 1_000_000 && reading.Used is >= 0 and <= 1_000_000 &&
                reading.Remaining is >= 0 and <= 1_000_000 && reading.ObservedAtUtc <= now.AddMinutes(2))));
}

/// <summary>
/// Local components that are not part of the Ledger (Marquee, scripts) save their tally as
/// <c>&lt;component&gt;.json</c> in one machine-local folder, in the <see cref="GitHubUsageReport"/> shape. The reporter
/// forwards those files with its heartbeat, so the components need no Ledger credentials of their own.
/// </summary>
internal static class GitHubUsageDropFolder {
    private const int MaximumFileBytes = 128 * 1024;

    internal static string DefaultPath() => Environment.GetEnvironmentVariable("IX_LEDGER_GITHUB_USAGE_DIR") is { Length: > 0 } configured
        ? configured
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelligenceX", "Ledger", "github-usage");

    /// <summary>Reads the current tallies. Unreadable, oversized or stale files are skipped, never fatal.</summary>
    /// <param name="turn">Moves the starting file each heartbeat, so with more current files than one heartbeat reads,
    /// every file still gets its turn.</param>
    internal static GitHubUsageReport[] Read(string folder, DateTimeOffset now, int turn = 0) {
        string[] files;
        // The folder is optional input: one that vanishes or can't be listed counts as empty and never stops the reporter.
        try { files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
        var reports = new List<GitHubUsageReport>();
        // Stale files are skipped before the scan limit, so old leftovers can never hide a current tally.
        bool Current(FileInfo info) {
            try { return info.Exists && info.Length <= MaximumFileBytes && now - info.LastWriteTimeUtc <= GitHubUsageMeter.Retention; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }
        var current = files.Select(file => new FileInfo(file)).Where(Current).OrderBy(info => info.Name, StringComparer.Ordinal).ToArray();
        var start = current.Length == 0 ? 0 : (int)((uint)turn % (uint)current.Length);
        var fresh = current.Skip(start).Concat(current.Take(start)).Take(GitHubUsageValidation.MaximumReports * 4);
        foreach (var info in fresh) {
            if (reports.Count == GitHubUsageValidation.MaximumReports) break;
            var file = info.FullName;
            try {
                var report = JsonSerializer.Deserialize<GitHubUsageReport>(File.ReadAllBytes(file), LedgerJson.Options);
                if (report is null) continue;
                var cutoff = now - GitHubUsageMeter.Retention;
                report = report with { Minutes = (report.Minutes ?? []).Where(minute => minute?.MinuteUtc >= cutoff).TakeLast(GitHubUsageValidation.MaximumMinutes).ToArray() };
                if (GitHubUsageValidation.Valid([report], now)) reports.Add(report);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        }
        return reports.ToArray();
    }
}

/// <summary>Saves the Ledger service's own GitHub tally once a minute, under the host's machine name.</summary>
internal sealed class GitHubUsageRecorder(GitHubUsageMeter meter, LedgerStore store) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try { await store.RecordGitHubUsageAsync(AccountLimits.LocalMachine, meter.Snapshot(), DateTimeOffset.UtcNow, stoppingToken); }
            catch (Exception error) when (error is not OperationCanceledException) {
                Console.Error.WriteLine("github-usage-record-failed:" + error.GetType().Name);
            }
        }
    }
}
