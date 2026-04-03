using System;
using System.Linq;
using IntelligenceX.Telemetry.Git;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestGitCodeChurnSummaryBuildsDailyWindows() {
        var output = string.Join("\n", new[] {
            "@@2026-04-03|2026-04-03T09:15:00Z|a1",
            "10\t2\tIntelligenceX/FileA.cs",
            "4\t1\tIntelligenceX/FileB.cs",
            "@@2026-04-02|2026-04-02T11:00:00Z|b2",
            "3\t5\tIntelligenceX/FileA.cs",
            "-\t-\tassets/logo.png",
            "@@2026-03-31|2026-03-31T08:00:00Z|c3",
            "2\t0\tIntelligenceX/FileC.cs",
            "@@2026-03-24|2026-03-24T08:00:00Z|d4",
            "7\t3\tIntelligenceX/FileD.cs",
            "1\t1\tIntelligenceX/FileE.cs"
        });

        var summary = GitCodeChurnSummaryService.BuildSummary(
            repositoryRootPath: @"C:\Support\GitHub\IntelligenceX",
            repositoryName: "IntelligenceX",
            gitLogOutput: output,
            now: new DateTimeOffset(2026, 04, 03, 12, 00, 00, TimeSpan.Zero));

        AssertEqual(true, summary.HasRepository, "git code churn summary repository exists");
        AssertEqual(true, summary.HasData, "git code churn summary has data");
        AssertEqual(19, summary.RecentAddedLines, "git code churn recent added");
        AssertEqual(8, summary.RecentDeletedLines, "git code churn recent deleted");
        AssertEqual(5, summary.RecentFilesModified, "git code churn recent files");
        AssertEqual(3, summary.RecentCommitCount, "git code churn recent commits");
        AssertEqual(3, summary.RecentActiveDayCount, "git code churn recent active days");
        AssertEqual(8, summary.PreviousAddedLines, "git code churn previous added");
        AssertEqual(4, summary.PreviousDeletedLines, "git code churn previous deleted");
        AssertEqual(2, summary.PreviousFilesModified, "git code churn previous files");
        AssertEqual(1, summary.PreviousCommitCount, "git code churn previous commits");
        AssertEqual(27, summary.Last30DaysAddedLines, "git code churn last30 added");
        AssertEqual(12, summary.Last30DaysDeletedLines, "git code churn last30 deleted");
        AssertEqual(7, summary.Last30DaysFilesModified, "git code churn last30 files");
        AssertEqual(4, summary.Last30DaysCommitCount, "git code churn last30 commits");
        AssertEqual(7, summary.TrendDays.Count, "git code churn trend day count");

        var aprilThird = summary.TrendDays.Single(day => day.DayUtc == new DateTime(2026, 04, 03));
        AssertEqual(14, aprilThird.AddedLines, "git code churn final day added");
        AssertEqual(3, aprilThird.DeletedLines, "git code churn final day deleted");
        AssertEqual(2, aprilThird.FilesModified, "git code churn final day files");
        AssertEqual(1, aprilThird.CommitCount, "git code churn final day commits");
        AssertEqual("2026-04-03 09:15:00 UTC", summary.LatestCommitAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), "git code churn latest commit");
        AssertEqual(new DateTime(2026, 04, 03), summary.PeakRecentDay?.DayUtc, "git code churn peak recent day");
    }
}
