using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IntelligenceX.Telemetry.Git;

#pragma warning disable CS1591

/// <summary>
/// Summarizes recent local git churn so tray and report surfaces can reuse one compact model.
/// </summary>
public sealed class GitCodeChurnSummaryService {
    private const int CommandTimeoutMilliseconds = 15000;
    private const int RecentWindowDays = 7;
    private const int LongWindowDays = 30;
    private const int DiscoveryLookbackDays = 35;

    public GitCodeChurnSummaryData Load() {
        var repositoryRoot = TryResolveRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repositoryRoot)) {
            return GitCodeChurnSummaryData.Empty;
        }

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var repositoryName = Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var output = TryReadGitLog(repositoryRoot);
        if (output is null) {
            return new GitCodeChurnSummaryData(
                repositoryRootPath: repositoryRoot,
                repositoryName: repositoryName,
                recentAddedLines: 0,
                recentDeletedLines: 0,
                recentFilesModified: 0,
                recentCommitCount: 0,
                recentActiveDayCount: 0,
                previousAddedLines: 0,
                previousDeletedLines: 0,
                previousFilesModified: 0,
                previousCommitCount: 0,
                last30DaysAddedLines: 0,
                last30DaysDeletedLines: 0,
                last30DaysFilesModified: 0,
                last30DaysCommitCount: 0,
                last30DaysActiveDayCount: 0,
                latestCommitAtUtc: null,
                trendDays: BuildEmptyTrendDays(DateTimeOffset.Now.Date.AddDays(-(RecentWindowDays - 1))));
        }

        return BuildSummary(repositoryRoot, repositoryName, output, DateTimeOffset.Now);
    }

    internal static GitCodeChurnSummaryData BuildSummary(
        string? repositoryRootPath,
        string? repositoryName,
        string? gitLogOutput,
        DateTimeOffset now) {
        var dayBuckets = ParseDailyBuckets(gitLogOutput, out var latestCommitAtUtc);
        var endDay = now.Date;
        var trendStartDay = endDay.AddDays(-(RecentWindowDays - 1));
        var previousStartDay = trendStartDay.AddDays(-RecentWindowDays);
        var previousEndDay = trendStartDay.AddDays(-1);
        var longWindowStartDay = endDay.AddDays(-(LongWindowDays - 1));

        var trendDays = BuildWindowDays(dayBuckets, trendStartDay, endDay);
        var recentSummary = SummarizeRange(dayBuckets, trendStartDay, endDay);
        var previousSummary = SummarizeRange(dayBuckets, previousStartDay, previousEndDay);
        var last30DaysSummary = SummarizeRange(dayBuckets, longWindowStartDay, endDay);

        return new GitCodeChurnSummaryData(
            repositoryRootPath: repositoryRootPath,
            repositoryName: repositoryName,
            recentAddedLines: recentSummary.AddedLines,
            recentDeletedLines: recentSummary.DeletedLines,
            recentFilesModified: recentSummary.FilesModified,
            recentCommitCount: recentSummary.CommitCount,
            recentActiveDayCount: recentSummary.ActiveDayCount,
            previousAddedLines: previousSummary.AddedLines,
            previousDeletedLines: previousSummary.DeletedLines,
            previousFilesModified: previousSummary.FilesModified,
            previousCommitCount: previousSummary.CommitCount,
            last30DaysAddedLines: last30DaysSummary.AddedLines,
            last30DaysDeletedLines: last30DaysSummary.DeletedLines,
            last30DaysFilesModified: last30DaysSummary.FilesModified,
            last30DaysCommitCount: last30DaysSummary.CommitCount,
            last30DaysActiveDayCount: last30DaysSummary.ActiveDayCount,
            latestCommitAtUtc: latestCommitAtUtc,
            trendDays: trendDays);
    }

    private static string? TryResolveRepositoryRoot() {
        foreach (var candidate in EnumerateDiscoveryCandidates()) {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate)) {
                continue;
            }

            var current = new DirectoryInfo(candidate);
            while (current is not null) {
                if (IsRepositoryRoot(current.FullName)) {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDiscoveryCandidates() {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;

        var entryAssemblyLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyLocation)) {
            yield return Path.GetDirectoryName(entryAssemblyLocation)!;
        }
    }

    private static bool IsRepositoryRoot(string path) {
        return Directory.Exists(Path.Combine(path, ".git"))
               || File.Exists(Path.Combine(path, ".git"));
    }

    private static string? TryReadGitLog(string repositoryRoot) {
        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = "git",
                Arguments = "-C " + Quote(repositoryRoot)
                            + " log --since=" + Quote(DiscoveryLookbackDays.ToString(CultureInfo.InvariantCulture) + " days ago")
                            + " --date=short --pretty=format:@@%ad|%cI|%H --numstat --no-renames -- .",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try {
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(CommandTimeoutMilliseconds)) {
                TryTerminate(process);
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        } catch {
            return null;
        }
    }

    private static void TryTerminate(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill();
                process.WaitForExit();
            }
        } catch {
            // Ignore shutdown errors when git is unavailable or already gone.
        }
    }

    private static string Quote(string value) {
        if (string.IsNullOrEmpty(value)) {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static Dictionary<DateTime, MutableDayBucket> ParseDailyBuckets(
        string? gitLogOutput,
        out DateTimeOffset? latestCommitAtUtc) {
        var buckets = new Dictionary<DateTime, MutableDayBucket>();
        latestCommitAtUtc = null;
        if (string.IsNullOrWhiteSpace(gitLogOutput)) {
            return buckets;
        }

        DateTime? currentDay = null;
        foreach (var rawLine in SplitLines(gitLogOutput!)) {
            var line = rawLine?.TrimEnd('\r') ?? string.Empty;
            if (line.Length == 0) {
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                currentDay = ParseCommitHeader(line, buckets, ref latestCommitAtUtc);
                continue;
            }

            if (!currentDay.HasValue) {
                continue;
            }

            ParseNumStatLine(line, buckets[currentDay.Value]);
        }

        return buckets;
    }

    private static IEnumerable<string> SplitLines(string value) {
        return value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    }

    private static DateTime? ParseCommitHeader(
        string line,
        IDictionary<DateTime, MutableDayBucket> buckets,
        ref DateTimeOffset? latestCommitAtUtc) {
        var parts = line.Substring(2).Split('|');
        if (parts.Length < 2) {
            return null;
        }

        if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var commitDay)) {
            return null;
        }

        if (!buckets.TryGetValue(commitDay, out var bucket)) {
            bucket = new MutableDayBucket(commitDay);
            buckets[commitDay] = bucket;
        }

        bucket.CommitCount++;
        if (DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var commitAt)) {
            var normalized = commitAt.ToUniversalTime();
            if (!latestCommitAtUtc.HasValue || normalized > latestCommitAtUtc.Value) {
                latestCommitAtUtc = normalized;
            }
        }

        return commitDay;
    }

    private static void ParseNumStatLine(string line, MutableDayBucket bucket) {
        var firstTab = line.IndexOf('\t');
        if (firstTab <= 0) {
            return;
        }

        var secondTab = line.IndexOf('\t', firstTab + 1);
        if (secondTab <= firstTab) {
            return;
        }

        var addedText = line.Substring(0, firstTab);
        var deletedText = line.Substring(firstTab + 1, secondTab - firstTab - 1);
        var path = line.Substring(secondTab + 1).Trim();
        if (int.TryParse(addedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var addedLines)) {
            bucket.AddedLines += Math.Max(0, addedLines);
        }

        if (int.TryParse(deletedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deletedLines)) {
            bucket.DeletedLines += Math.Max(0, deletedLines);
        }

        if (!string.IsNullOrWhiteSpace(path)) {
            bucket.Files.Add(path);
        }
    }

    private static GitCodeChurnDayData[] BuildWindowDays(
        IReadOnlyDictionary<DateTime, MutableDayBucket> buckets,
        DateTime startDay,
        DateTime endDay) {
        var days = new List<GitCodeChurnDayData>(RecentWindowDays);
        for (var day = startDay; day <= endDay; day = day.AddDays(1)) {
            if (buckets.TryGetValue(day, out var bucket)) {
                days.Add(bucket.ToImmutable());
            } else {
                days.Add(new GitCodeChurnDayData(day, 0, 0, 0, 0));
            }
        }

        return days.ToArray();
    }

    private static GitCodeChurnDayData[] BuildEmptyTrendDays(DateTime startDay) {
        var days = new List<GitCodeChurnDayData>(RecentWindowDays);
        for (var day = startDay; days.Count < RecentWindowDays; day = day.AddDays(1)) {
            days.Add(new GitCodeChurnDayData(day, 0, 0, 0, 0));
        }

        return days.ToArray();
    }

    private static GitCodeChurnWindowSummary SummarizeRange(
        IReadOnlyDictionary<DateTime, MutableDayBucket> buckets,
        DateTime startDay,
        DateTime endDay) {
        var summary = new GitCodeChurnWindowSummary();
        for (var day = startDay; day <= endDay; day = day.AddDays(1)) {
            if (!buckets.TryGetValue(day, out var bucket)) {
                continue;
            }

            summary.AddedLines += bucket.AddedLines;
            summary.DeletedLines += bucket.DeletedLines;
            summary.FilesModified += bucket.Files.Count;
            summary.CommitCount += bucket.CommitCount;
            if (bucket.CommitCount > 0 || bucket.AddedLines > 0 || bucket.DeletedLines > 0 || bucket.Files.Count > 0) {
                summary.ActiveDayCount++;
            }
        }

        return summary;
    }

    private sealed class MutableDayBucket {
        public MutableDayBucket(DateTime dayUtc) {
            DayUtc = dayUtc;
        }

        public DateTime DayUtc { get; }
        public int AddedLines { get; set; }
        public int DeletedLines { get; set; }
        public int CommitCount { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public GitCodeChurnDayData ToImmutable() {
            return new GitCodeChurnDayData(
                DayUtc,
                AddedLines,
                DeletedLines,
                Files.Count,
                CommitCount);
        }
    }

    private sealed class GitCodeChurnWindowSummary {
        public int AddedLines { get; set; }
        public int DeletedLines { get; set; }
        public int FilesModified { get; set; }
        public int CommitCount { get; set; }
        public int ActiveDayCount { get; set; }
    }
}

public sealed class GitCodeChurnSummaryData {
    public static GitCodeChurnSummaryData Empty { get; } = new(
        repositoryRootPath: null,
        repositoryName: null,
        recentAddedLines: 0,
        recentDeletedLines: 0,
        recentFilesModified: 0,
        recentCommitCount: 0,
        recentActiveDayCount: 0,
        previousAddedLines: 0,
        previousDeletedLines: 0,
        previousFilesModified: 0,
        previousCommitCount: 0,
        last30DaysAddedLines: 0,
        last30DaysDeletedLines: 0,
        last30DaysFilesModified: 0,
        last30DaysCommitCount: 0,
        last30DaysActiveDayCount: 0,
        latestCommitAtUtc: null,
        trendDays: Array.Empty<GitCodeChurnDayData>());

    public GitCodeChurnSummaryData(
        string? repositoryRootPath,
        string? repositoryName,
        int recentAddedLines,
        int recentDeletedLines,
        int recentFilesModified,
        int recentCommitCount,
        int recentActiveDayCount,
        int previousAddedLines,
        int previousDeletedLines,
        int previousFilesModified,
        int previousCommitCount,
        int last30DaysAddedLines,
        int last30DaysDeletedLines,
        int last30DaysFilesModified,
        int last30DaysCommitCount,
        int last30DaysActiveDayCount,
        DateTimeOffset? latestCommitAtUtc,
        IReadOnlyList<GitCodeChurnDayData> trendDays) {
        RepositoryRootPath = repositoryRootPath;
        RepositoryName = repositoryName;
        RecentAddedLines = Math.Max(0, recentAddedLines);
        RecentDeletedLines = Math.Max(0, recentDeletedLines);
        RecentFilesModified = Math.Max(0, recentFilesModified);
        RecentCommitCount = Math.Max(0, recentCommitCount);
        RecentActiveDayCount = Math.Max(0, recentActiveDayCount);
        PreviousAddedLines = Math.Max(0, previousAddedLines);
        PreviousDeletedLines = Math.Max(0, previousDeletedLines);
        PreviousFilesModified = Math.Max(0, previousFilesModified);
        PreviousCommitCount = Math.Max(0, previousCommitCount);
        Last30DaysAddedLines = Math.Max(0, last30DaysAddedLines);
        Last30DaysDeletedLines = Math.Max(0, last30DaysDeletedLines);
        Last30DaysFilesModified = Math.Max(0, last30DaysFilesModified);
        Last30DaysCommitCount = Math.Max(0, last30DaysCommitCount);
        Last30DaysActiveDayCount = Math.Max(0, last30DaysActiveDayCount);
        LatestCommitAtUtc = latestCommitAtUtc?.ToUniversalTime();
        TrendDays = trendDays ?? Array.Empty<GitCodeChurnDayData>();
    }

    public string? RepositoryRootPath { get; }
    public string? RepositoryName { get; }
    public int RecentAddedLines { get; }
    public int RecentDeletedLines { get; }
    public int RecentFilesModified { get; }
    public int RecentCommitCount { get; }
    public int RecentActiveDayCount { get; }
    public int PreviousAddedLines { get; }
    public int PreviousDeletedLines { get; }
    public int PreviousFilesModified { get; }
    public int PreviousCommitCount { get; }
    public int Last30DaysAddedLines { get; }
    public int Last30DaysDeletedLines { get; }
    public int Last30DaysFilesModified { get; }
    public int Last30DaysCommitCount { get; }
    public int Last30DaysActiveDayCount { get; }
    public DateTimeOffset? LatestCommitAtUtc { get; }
    public IReadOnlyList<GitCodeChurnDayData> TrendDays { get; }
    public int RecentNetLines => RecentAddedLines - RecentDeletedLines;
    public bool HasRepository => !string.IsNullOrWhiteSpace(RepositoryRootPath);
    public bool HasData => RecentCommitCount > 0
                           || PreviousCommitCount > 0
                           || Last30DaysCommitCount > 0
                           || TrendDays.Any(static day => day.HasActivity);
    public GitCodeChurnDayData? PeakRecentDay => TrendDays
        .Where(static day => day.HasActivity)
        .OrderByDescending(static day => day.TotalChangedLines)
        .ThenByDescending(static day => day.CommitCount)
        .FirstOrDefault();
}

public sealed class GitCodeChurnDayData {
    public GitCodeChurnDayData(
        DateTime dayUtc,
        int addedLines,
        int deletedLines,
        int filesModified,
        int commitCount) {
        DayUtc = dayUtc;
        AddedLines = Math.Max(0, addedLines);
        DeletedLines = Math.Max(0, deletedLines);
        FilesModified = Math.Max(0, filesModified);
        CommitCount = Math.Max(0, commitCount);
    }

    public DateTime DayUtc { get; }
    public int AddedLines { get; }
    public int DeletedLines { get; }
    public int FilesModified { get; }
    public int CommitCount { get; }
    public int NetLines => AddedLines - DeletedLines;
    public int TotalChangedLines => AddedLines + DeletedLines;
    public bool HasActivity => CommitCount > 0 || AddedLines > 0 || DeletedLines > 0 || FilesModified > 0;
}
