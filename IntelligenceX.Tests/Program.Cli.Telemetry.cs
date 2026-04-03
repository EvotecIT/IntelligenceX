using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
#if !NET472
using IntelligenceX.Cli.GitHub;
using IntelligenceX.Cli.Telemetry;
#endif
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestTelemetryHelpRoutes() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "telemetry", "--help" },
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(0, exit, "telemetry help exit");
        AssertContainsText(stdout, "intelligencex telemetry usage", "telemetry help usage");
        AssertContainsText(stdout, "intelligencex telemetry github", "telemetry help github usage");
        AssertEqual(string.Empty, stderr, "telemetry help stderr");
    }

    private static void TestTelemetryUsageHelpRoutes() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "telemetry", "usage", "--help" },
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(0, exit, "telemetry usage help exit");
        AssertContainsText(stdout, "telemetry usage roots list", "telemetry usage roots help");
        AssertContainsText(stdout, "telemetry usage accounts list", "telemetry usage accounts help");
        AssertContainsText(stdout, "telemetry usage import", "telemetry usage import help");
        AssertContainsText(stdout, "telemetry usage report", "telemetry usage report help");
        AssertContainsText(stdout, "telemetry usage overview", "telemetry usage overview help");
        AssertContainsText(stdout, "telemetry usage stats", "telemetry usage stats help");
        AssertContainsText(stdout, "--force", "telemetry usage help force import option");
        AssertContainsText(stdout, "--recent-first", "telemetry usage help recent first option");
        AssertContainsText(stdout, "--max-artifacts", "telemetry usage help max artifacts option");
        AssertContainsText(stdout, "--paths-only", "telemetry usage help paths-only option");
        AssertEqual(string.Empty, stderr, "telemetry usage help stderr");
    }

    private static void TestTelemetryGitHubHelpRoutes() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "telemetry", "github", "--help" },
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(0, exit, "telemetry github help exit");
        AssertContainsText(stdout, "telemetry github watches list", "telemetry github help watches list");
        AssertContainsText(stdout, "telemetry github watches add", "telemetry github help watches add");
        AssertContainsText(stdout, "telemetry github watches sync", "telemetry github help watches sync");
        AssertContainsText(stdout, "telemetry github snapshots list", "telemetry github help snapshots list");
        AssertContainsText(stdout, "telemetry github forks discover", "telemetry github help forks discover");
        AssertContainsText(stdout, "telemetry github forks history", "telemetry github help forks history");
        AssertContainsText(stdout, "telemetry github stargazers capture", "telemetry github help stargazers capture");
        AssertContainsText(stdout, "telemetry github stargazers list", "telemetry github help stargazers list");
        AssertContainsText(stdout, "telemetry github dashboard", "telemetry github help dashboard");
        AssertContainsText(stdout, "--forks", "telemetry github help watch sync forks option");
        AssertContainsText(stdout, "--fork-limit", "telemetry github help watch sync fork limit option");
        AssertContainsText(stdout, "--stargazers", "telemetry github help watch sync stargazers option");
        AssertContainsText(stdout, "--stargazer-limit", "telemetry github help watch sync stargazer limit option");
        AssertEqual(string.Empty, stderr, "telemetry github help stderr");
    }

    private static void TestTelemetryGitHubWatchesAddAndListJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-watches-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (addExit, addStdout, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "github", "watches", "add",
                    "--db", dbPath,
                    "--repo", "EvotecIT/IntelligenceX",
                    "--display-name", "IntelligenceX",
                    "--category", "tray",
                    "--notes", "Primary watch",
                    "--json"
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, addExit, "telemetry github watches add exit");
            AssertContainsText(addStdout, "\"repositoryNameWithOwner\":\"EvotecIT/IntelligenceX\"", "telemetry github watches add repo");
            AssertContainsText(addStdout, "\"category\":\"tray\"", "telemetry github watches add category");
            AssertEqual(string.Empty, addStderr, "telemetry github watches add stderr");

            var (listExit, listStdout, listStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "watches", "list", "--db", dbPath, "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, listExit, "telemetry github watches list exit");
            AssertContainsText(listStdout, "\"repository\":\"IntelligenceX\"", "telemetry github watches list repository");
            AssertContainsText(listStdout, "\"owner\":\"EvotecIT\"", "telemetry github watches list owner");
            AssertEqual(string.Empty, listStderr, "telemetry github watches list stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubWatchesSyncAndSnapshotsListJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (addExit, _, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "github", "watches", "add",
                    "--db", dbPath,
                    "--repo", "EvotecIT/IntelligenceX"
                },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, addExit, "telemetry github sync setup add exit");
            AssertEqual(string.Empty, addStderr, "telemetry github sync setup add stderr");

            var firstSummary = CreateGitHubRepositoryImpactSummary(126, 21, 15, 5);
            var secondSummary = CreateGitHubRepositoryImpactSummary(130, 22, 18, 4);

            var (syncFirstExit, syncFirstStdout, syncFirstStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", "2026-03-16T10:05:00Z", "--json" },
                _ => Task.FromResult(firstSummary));
            AssertEqual(0, syncFirstExit, "telemetry github first sync exit");
            AssertContainsText(syncFirstStdout, "\"syncedCount\":1", "telemetry github first sync count");
            AssertContainsText(syncFirstStdout, "\"watchers\":15", "telemetry github first sync watchers");
            AssertContainsText(syncFirstStdout, "\"starDelta\":126", "telemetry github first sync baseline delta");
            AssertEqual(string.Empty, syncFirstStderr, "telemetry github first sync stderr");

            var (syncSecondExit, syncSecondStdout, syncSecondStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", "2026-03-17T10:05:00Z", "--json" },
                _ => Task.FromResult(secondSummary));
            AssertEqual(0, syncSecondExit, "telemetry github second sync exit");
            AssertContainsText(syncSecondStdout, "\"starDelta\":4", "telemetry github second sync star delta");
            AssertContainsText(syncSecondStdout, "\"forkDelta\":1", "telemetry github second sync fork delta");
            AssertContainsText(syncSecondStdout, "\"watcherDelta\":3", "telemetry github second sync watcher delta");
            AssertEqual(string.Empty, syncSecondStderr, "telemetry github second sync stderr");

            var (listExit, listStdout, listStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "snapshots", "list", "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, listExit, "telemetry github snapshots list exit");
            AssertContainsText(listStdout, "\"capturedAtUtc\":\"2026-03-16T10:05:00.0000000+00:00\"", "telemetry github snapshots first capture");
            AssertContainsText(listStdout, "\"capturedAtUtc\":\"2026-03-17T10:05:00.0000000+00:00\"", "telemetry github snapshots second capture");
            AssertContainsText(listStdout, "\"watchers\":18", "telemetry github snapshots latest watchers");
            AssertEqual(string.Empty, listStderr, "telemetry github snapshots list stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubWatchesSyncCanRecordForksJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-sync-forks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (addExit, _, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "github", "watches", "add",
                    "--db", dbPath,
                    "--repo", "EvotecIT/IntelligenceX"
                },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, addExit, "telemetry github sync forks setup add exit");
            AssertEqual(string.Empty, addStderr, "telemetry github sync forks setup add stderr");

            var (syncExit, syncStdout, syncStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", "2026-03-17T10:05:00Z", "--forks", "--fork-limit", "2", "--json" },
                _ => Task.FromResult(CreateGitHubRepositoryImpactSummary(130, 22, 18, 4)),
                (_, _) => Task.FromResult(CreateForkInsightsSetB()));

            AssertEqual(0, syncExit, "telemetry github sync forks exit");
            AssertContainsText(syncStdout, "\"forksIncluded\":true", "telemetry github sync forks included");
            AssertContainsText(syncStdout, "\"forks\":[", "telemetry github sync forks payload");
            AssertContainsText(syncStdout, "\"recordedCount\":2", "telemetry github sync forks recorded count");
            AssertContainsText(syncStdout, "\"forkRepositoryNameWithOwner\":\"someone/IntelligenceX\"", "telemetry github sync forks existing fork");
            AssertContainsText(syncStdout, "\"forkRepositoryNameWithOwner\":\"newperson/IntelligenceX\"", "telemetry github sync forks new fork");
            AssertEqual(string.Empty, syncStderr, "telemetry github sync forks stderr");

            var (historyExit, historyStdout, historyStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "forks", "history", "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--json" },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, historyExit, "telemetry github sync forks history exit");
            AssertContainsText(historyStdout, "\"forkRepositoryNameWithOwner\":\"someone/IntelligenceX\"", "telemetry github sync forks history existing fork");
            AssertContainsText(historyStdout, "\"status\":\"new\"", "telemetry github sync forks history new baseline status");
            AssertEqual(string.Empty, historyStderr, "telemetry github sync forks history stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubWatchesSyncCanRecordStargazersJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-sync-stargazers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (addExit, _, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "github", "watches", "add",
                    "--db", dbPath,
                    "--repo", "EvotecIT/IntelligenceX"
                },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, addExit, "telemetry github sync stargazers setup add exit");
            AssertEqual(string.Empty, addStderr, "telemetry github sync stargazers setup add stderr");

            var (syncExit, syncStdout, syncStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", "2026-03-17T10:05:00Z", "--stargazers", "--stargazer-limit", "2", "--json" },
                _ => Task.FromResult(CreateGitHubRepositoryImpactSummary(130, 22, 18, 4)),
                discoverStargazersAsync: (_, _) => Task.FromResult(CreateStargazerRecordsSetA()));

            AssertEqual(0, syncExit, "telemetry github sync stargazers exit");
            AssertContainsText(syncStdout, "\"stargazersIncluded\":true", "telemetry github sync stargazers included");
            AssertContainsText(syncStdout, "\"stargazers\":[", "telemetry github sync stargazers payload");
            AssertContainsText(syncStdout, "\"recordedCount\":2", "telemetry github sync stargazers recorded count");
            AssertContainsText(syncStdout, "\"stargazerLogin\":\"alice\"", "telemetry github sync stargazers alice");
            AssertContainsText(syncStdout, "\"stargazerLogin\":\"bob\"", "telemetry github sync stargazers bob");
            AssertEqual(string.Empty, syncStderr, "telemetry github sync stargazers stderr");

            var (listExit, listStdout, listStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "stargazers", "list", "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--json" },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, listExit, "telemetry github sync stargazers list exit");
            AssertContainsText(listStdout, "\"stargazerLogin\":\"alice\"", "telemetry github sync stargazers list alice");
            AssertContainsText(listStdout, "\"stargazerLogin\":\"bob\"", "telemetry github sync stargazers list bob");
            AssertEqual(string.Empty, listStderr, "telemetry github sync stargazers list stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubWatchesSyncMarksEmptyForkAndStargazerCapturesFresh() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-sync-empty-captures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        const string repo = "EvotecIT/IntelligenceX";
        const string capturedAtText = "2026-03-17T10:05:00Z";
        try {
            var (addExit, _, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "github", "watches", "add",
                    "--db", dbPath,
                    "--repo", repo
                },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, addExit, "telemetry github sync empty captures setup add exit");
            AssertEqual(string.Empty, addStderr, "telemetry github sync empty captures setup add stderr");

            var (syncExit, syncStdout, syncStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", capturedAtText, "--forks", "--stargazers", "--json" },
                _ => Task.FromResult(CreateGitHubRepositoryImpactSummary(130, 22, 18, 4)),
                (_, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryForkInsight>>(Array.Empty<GitHubRepositoryForkInsight>()),
                (_, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryStargazerRecord>>(Array.Empty<GitHubRepositoryStargazerRecord>()));

            AssertEqual(0, syncExit, "telemetry github sync empty captures exit");
            AssertContainsText(syncStdout, "\"forksIncluded\":true", "telemetry github sync empty captures forks included");
            AssertContainsText(syncStdout, "\"stargazersIncluded\":true", "telemetry github sync empty captures stargazers included");
            AssertContainsText(syncStdout, "\"recordedCount\":0", "telemetry github sync empty captures recorded zero");
            AssertEqual(string.Empty, syncStderr, "telemetry github sync empty captures stderr");

            using var forkStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
            using var stargazerStore = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);
            var expectedCaptureAtUtc = DateTimeOffset.Parse(capturedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            AssertEqual(expectedCaptureAtUtc, forkStore.GetLatestCaptureAtUtcByParentRepository(repo), "telemetry github sync empty captures persists fork watermark");
            AssertEqual(expectedCaptureAtUtc, stargazerStore.GetLatestCaptureAtUtcByRepository(repo), "telemetry github sync empty captures persists stargazer watermark");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubForksDiscoverJson() {
        var (exit, stdout, stderr) = RunGitHubTelemetryForksDiscoverWithCapturedOutput(
            new[] { "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--json" },
            (_, _) => Task.FromResult(CreateForkInsightsSetA()));

        AssertEqual(0, exit, "telemetry github forks discover exit");
        AssertContainsText(stdout, "\"repositoryNameWithOwner\":\"EvotecIT/IntelligenceX\"", "telemetry github forks discover repository");
        AssertContainsText(stdout, "\"tier\":\"high\"", "telemetry github forks discover top tier");
        AssertContainsText(stdout, "\"score\":72.5", "telemetry github forks discover top score");
        AssertContainsText(stdout, "\"repositoryNameWithOwner\":\"someone/IntelligenceX\"", "telemetry github forks discover top fork");
        AssertEqual(string.Empty, stderr, "telemetry github forks discover stderr");
    }

    private static void TestTelemetryGitHubForksRecordAndHistoryJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-forks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (firstExit, firstStdout, firstStderr) = RunGitHubTelemetryForksDiscoverWithCapturedOutput(
                new[] { "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--captured-at", "2026-03-16T10:05:00Z", "--record", "--json" },
                (_, _) => Task.FromResult(CreateForkInsightsSetA()));
            AssertEqual(0, firstExit, "telemetry github forks first record exit");
            AssertContainsText(firstStdout, "\"recorded\":true", "telemetry github forks first record flag");
            AssertContainsText(firstStdout, "\"recordedSnapshots\":[", "telemetry github forks first recorded snapshots");
            AssertEqual(string.Empty, firstStderr, "telemetry github forks first record stderr");

            var (secondExit, secondStdout, secondStderr) = RunGitHubTelemetryForksDiscoverWithCapturedOutput(
                new[] { "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--captured-at", "2026-03-17T10:05:00Z", "--record", "--json" },
                (_, _) => Task.FromResult(CreateForkInsightsSetB()));
            AssertEqual(0, secondExit, "telemetry github forks second record exit");
            AssertContainsText(secondStdout, "\"capturedAtUtc\":\"2026-03-17T10:05:00.0000000+00:00\"", "telemetry github forks second capture");
            AssertEqual(string.Empty, secondStderr, "telemetry github forks second record stderr");

            var (historyExit, historyStdout, historyStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "forks", "history", "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, historyExit, "telemetry github forks history exit");
            AssertContainsText(historyStdout, "\"forkRepositoryNameWithOwner\":\"someone/IntelligenceX\"", "telemetry github forks history existing fork");
            AssertContainsText(historyStdout, "\"status\":\"rising\"", "telemetry github forks history rising status");
            AssertContainsText(historyStdout, "\"scoreDelta\":9.5", "telemetry github forks history score delta");
            AssertContainsText(historyStdout, "\"forkRepositoryNameWithOwner\":\"newperson/IntelligenceX\"", "telemetry github forks history new fork");
            AssertContainsText(historyStdout, "\"status\":\"new\"", "telemetry github forks history new status");
            AssertEqual(string.Empty, historyStderr, "telemetry github forks history stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubStargazersCaptureAndListJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-stargazers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (captureExit, captureStdout, captureStderr) = RunGitHubTelemetryStargazersCaptureWithCapturedOutput(
                new[] { "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--captured-at", "2026-03-17T10:05:00Z", "--record", "--json" },
                (_, _) => Task.FromResult(CreateStargazerRecordsSetA()));
            AssertEqual(0, captureExit, "telemetry github stargazers capture exit");
            AssertContainsText(captureStdout, "\"recorded\":true", "telemetry github stargazers capture recorded flag");
            AssertContainsText(captureStdout, "\"stargazers\":[", "telemetry github stargazers capture stargazers payload");
            AssertContainsText(captureStdout, "\"login\":\"alice\"", "telemetry github stargazers capture alice");
            AssertContainsText(captureStdout, "\"recordedSnapshots\":[", "telemetry github stargazers capture recorded snapshots");
            AssertEqual(string.Empty, captureStderr, "telemetry github stargazers capture stderr");

            var (listExit, listStdout, listStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "stargazers", "list", "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--json" },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, listExit, "telemetry github stargazers list exit");
            AssertContainsText(listStdout, "\"stargazerLogin\":\"alice\"", "telemetry github stargazers list alice login");
            AssertContainsText(listStdout, "\"stargazerLogin\":\"bob\"", "telemetry github stargazers list bob login");
            AssertEqual(string.Empty, listStderr, "telemetry github stargazers list stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryGitHubDashboardJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-github-dashboard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (addExit, _, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "github", "watches", "add",
                    "--db", dbPath,
                    "--repo", "EvotecIT/IntelligenceX",
                    "--display-name", "IntelligenceX",
                    "--json"
                },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, addExit, "telemetry github dashboard setup add exit");
            AssertEqual(string.Empty, addStderr, "telemetry github dashboard setup add stderr");

            var firstSummary = CreateGitHubRepositoryImpactSummary(126, 21, 15, 5);
            var secondSummary = CreateGitHubRepositoryImpactSummary(130, 22, 18, 4);
            var (syncFirstExit, _, syncFirstStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", "2026-03-16T10:05:00Z", "--json" },
                _ => Task.FromResult(firstSummary));
            AssertEqual(0, syncFirstExit, "telemetry github dashboard first sync exit");
            AssertEqual(string.Empty, syncFirstStderr, "telemetry github dashboard first sync stderr");

            var (syncSecondExit, _, syncSecondStderr) = RunGitHubTelemetrySyncWithCapturedOutput(
                new[] { "--db", dbPath, "--captured-at", "2026-03-17T10:05:00Z", "--json" },
                _ => Task.FromResult(secondSummary));
            AssertEqual(0, syncSecondExit, "telemetry github dashboard second sync exit");
            AssertEqual(string.Empty, syncSecondStderr, "telemetry github dashboard second sync stderr");

            var (forkFirstExit, _, forkFirstStderr) = RunGitHubTelemetryForksDiscoverWithCapturedOutput(
                new[] { "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--captured-at", "2026-03-16T10:05:00Z", "--record", "--json" },
                (_, _) => Task.FromResult(CreateForkInsightsSetA()));
            AssertEqual(0, forkFirstExit, "telemetry github dashboard first fork record exit");
            AssertEqual(string.Empty, forkFirstStderr, "telemetry github dashboard first fork record stderr");

            var (forkSecondExit, _, forkSecondStderr) = RunGitHubTelemetryForksDiscoverWithCapturedOutput(
                new[] { "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--captured-at", "2026-03-17T10:05:00Z", "--record", "--json" },
                (_, _) => Task.FromResult(CreateForkInsightsSetB()));
            AssertEqual(0, forkSecondExit, "telemetry github dashboard second fork record exit");
            AssertEqual(string.Empty, forkSecondStderr, "telemetry github dashboard second fork record stderr");

            var (dashboardExit, dashboardStdout, dashboardStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "github", "dashboard", "--db", dbPath, "--repo", "EvotecIT/IntelligenceX", "--limit", "2", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, dashboardExit, "telemetry github dashboard exit");
            AssertContainsText(dashboardStdout, "\"repositories\":[", "telemetry github dashboard repositories");
            AssertContainsText(dashboardStdout, "\"repositoryNameWithOwner\":\"EvotecIT/IntelligenceX\"", "telemetry github dashboard repository");
            AssertContainsText(dashboardStdout, "\"starDelta\":4", "telemetry github dashboard star delta");
            AssertContainsText(dashboardStdout, "\"watcherDelta\":3", "telemetry github dashboard watcher delta");
            AssertContainsText(dashboardStdout, "\"forkChanges\":[", "telemetry github dashboard fork changes");
            AssertContainsText(dashboardStdout, "\"forkRepositoryNameWithOwner\":\"someone/IntelligenceX\"", "telemetry github dashboard rising fork");
            AssertContainsText(dashboardStdout, "\"status\":\"rising\"", "telemetry github dashboard rising status");
            AssertContainsText(dashboardStdout, "\"forkRepositoryNameWithOwner\":\"newperson/IntelligenceX\"", "telemetry github dashboard new fork");
            AssertEqual(string.Empty, dashboardStderr, "telemetry github dashboard stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryUsageRootsAddAndListJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-roots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var sourcePath = Path.Combine(temp, "windows.old", ".codex", "sessions");
            Directory.CreateDirectory(sourcePath);

            var (addExit, addStdout, addStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "roots", "add",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--path", sourcePath,
                    "--source-kind", "recovered",
                    "--platform", "windows",
                    "--machine", "backup-laptop",
                    "--account-hint", "personal",
                    "--json"
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, addExit, "telemetry roots add exit");
            AssertContainsText(addStdout, "\"providerId\":\"codex\"", "telemetry roots add provider");
            AssertContainsText(addStdout, "\"sourceKind\":\"RecoveredFolder\"", "telemetry roots add source kind");
            AssertContainsText(addStdout, "\"accountHint\":\"personal\"", "telemetry roots add account hint");
            AssertEqual(string.Empty, addStderr, "telemetry roots add stderr");

            var (listExit, listStdout, listStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "roots", "list", "--db", dbPath, "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, listExit, "telemetry roots list exit");
            AssertContainsText(listStdout, "\"providerId\":\"codex\"", "telemetry roots list provider");
            AssertContainsText(listStdout, "\"machineLabel\":\"backup-laptop\"", "telemetry roots list machine");
            AssertContainsText(listStdout, "\"path\":\"", "telemetry roots list path");
            AssertEqual(string.Empty, listStderr, "telemetry roots list stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGitHubTelemetrySyncWithCapturedOutput(
        string[] args,
        Func<IReadOnlyList<string>, Task<GitHubRepositoryImpactSummary>> fetchRepositoryImpactAsync,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryForkInsight>>>? discoverForksAsync = null,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryStargazerRecord>>>? discoverStargazersAsync = null) {
        lock (CliDispatchConsoleLock) {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            try {
                var exit = GitHubTelemetryCliRunner.RunSyncAsyncForTest(args, fetchRepositoryImpactAsync, discoverForksAsync, discoverStargazersAsync)
                    .GetAwaiter()
                    .GetResult();
                return (exit, outWriter.ToString(), errWriter.ToString());
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGitHubTelemetryForksDiscoverWithCapturedOutput(
        string[] args,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryForkInsight>>> discoverForksAsync) {
        lock (CliDispatchConsoleLock) {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            try {
                var exit = GitHubTelemetryCliRunner.RunForksDiscoverAsyncForTest(args, discoverForksAsync)
                    .GetAwaiter()
                    .GetResult();
                return (exit, outWriter.ToString(), errWriter.ToString());
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGitHubTelemetryStargazersCaptureWithCapturedOutput(
        string[] args,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryStargazerRecord>>> discoverStargazersAsync) {
        lock (CliDispatchConsoleLock) {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            try {
                var exit = GitHubTelemetryCliRunner.RunStargazersCaptureAsyncForTest(args, discoverStargazersAsync)
                    .GetAwaiter()
                    .GetResult();
                return (exit, outWriter.ToString(), errWriter.ToString());
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    private static IReadOnlyList<GitHubRepositoryForkInsight> CreateForkInsightsSetA() {
        return new[] {
            new GitHubRepositoryForkInsight(
                new GitHubRepositoryForkRecord(
                    "someone/IntelligenceX",
                    "https://github.com/someone/IntelligenceX",
                    18,
                    3,
                    9,
                    2,
                    "High-signal fork",
                    "C#",
                    "2026-03-14T12:00:00Z",
                    "2026-03-14T12:00:00Z",
                    "2025-11-01T00:00:00Z",
                    false),
                72.5,
                "high",
                new[] { "18 stars", "updated within 14 days" }),
            new GitHubRepositoryForkInsight(
                new GitHubRepositoryForkRecord(
                    "another/IntelligenceX",
                    "https://github.com/another/IntelligenceX",
                    7,
                    1,
                    2,
                    0,
                    "Focused fork",
                    "C#",
                    "2026-02-20T12:00:00Z",
                    "2026-02-20T12:00:00Z",
                    "2025-10-01T00:00:00Z",
                    false),
                41.0,
                "medium",
                new[] { "7 stars", "updated within 45 days" })
        };
    }

    private static IReadOnlyList<GitHubRepositoryStargazerRecord> CreateStargazerRecordsSetA() {
        return new[] {
            new GitHubRepositoryStargazerRecord(
                "alice",
                "https://github.com/alice",
                "https://avatars.githubusercontent.com/u/1?v=4",
                "2026-03-16T12:00:00Z"),
            new GitHubRepositoryStargazerRecord(
                "bob",
                "https://github.com/bob",
                "https://avatars.githubusercontent.com/u/2?v=4",
                "2026-03-17T09:00:00Z")
        };
    }

    private static IReadOnlyList<GitHubRepositoryForkInsight> CreateForkInsightsSetB() {
        return new[] {
            new GitHubRepositoryForkInsight(
                new GitHubRepositoryForkRecord(
                    "someone/IntelligenceX",
                    "https://github.com/someone/IntelligenceX",
                    22,
                    4,
                    11,
                    2,
                    "High-signal fork",
                    "C#",
                    "2026-03-16T12:00:00Z",
                    "2026-03-16T12:00:00Z",
                    "2025-11-01T00:00:00Z",
                    false),
                82.0,
                "high",
                new[] { "22 stars", "updated within 14 days" }),
            new GitHubRepositoryForkInsight(
                new GitHubRepositoryForkRecord(
                    "newperson/IntelligenceX",
                    "https://github.com/newperson/IntelligenceX",
                    5,
                    1,
                    3,
                    0,
                    "Fresh fork",
                    "C#",
                    "2026-03-16T15:00:00Z",
                    "2026-03-16T15:00:00Z",
                    "2026-03-16T10:00:00Z",
                    false),
                33.0,
                "low",
                new[] { "5 stars", "updated within 14 days" })
        };
    }

    private static GitHubRepositoryImpactSummary CreateGitHubRepositoryImpactSummary(int stars, int forks, int watchers, int openIssues) {
        var repository = new GitHubRepositoryImpactRepository(
            "EvotecIT/IntelligenceX",
            "https://github.com/EvotecIT/IntelligenceX",
            stars,
            forks,
            "C#",
            "#178600",
            "2026-03-16T09:55:00Z",
            watchers: watchers,
            openIssues: openIssues,
            description: "Unified intelligence workspace",
            isArchived: false,
            isFork: false);
        return new GitHubRepositoryImpactSummary(
            new[] {
                new GitHubRepositoryOwnerImpact(
                    "EvotecIT",
                    1,
                    stars,
                    forks,
                    new[] { repository },
                    repository)
            },
            new[] { repository });
    }

    private static void TestTelemetryUsageImportAndStatsJson() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T14-00-00-thread-cli.jsonl"),
                "thread-cli",
                "resp-cli",
                includeAuth: false,
                authRoot: tempDir);

            var dbPath = Path.Combine(tempDir, "usage.db");
            var (addExit, _, addErr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "roots", "add",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--path", sessionsDir,
                    "--source-kind", "recovered",
                    "--account-hint", "archive"
                },
                () => false,
                _ => Task.FromResult(0));
            AssertEqual(0, addExit, "telemetry import setup add exit");
            AssertEqual(string.Empty, addErr, "telemetry import setup add stderr");

            var (importExit, importStdout, importStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "import", "--db", dbPath, "--provider", "codex", "--machine", "archive-box", "--max-artifacts", "1", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, importExit, "telemetry import exit");
            AssertContainsText(importStdout, "\"artifactsProcessed\":1", "telemetry import artifacts processed");
            AssertContainsText(importStdout, "\"eventsInserted\":1", "telemetry import inserted");
            AssertContainsText(importStdout, "\"providerId\":\"codex\"", "telemetry import provider");
            AssertEqual(string.Empty, importStderr, "telemetry import stderr");

            var (statsExit, statsStdout, statsStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "stats", "--db", dbPath, "--provider", "codex", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, statsExit, "telemetry stats exit");
            AssertContainsText(statsStdout, "\"bindingCount\":0", "telemetry stats binding count");
            AssertContainsText(statsStdout, "\"eventCount\":1", "telemetry stats event count");
            AssertContainsText(statsStdout, "\"accountCount\":1", "telemetry stats account count");
            AssertContainsText(statsStdout, "\"providerId\":\"codex\"", "telemetry stats provider");
            AssertContainsText(statsStdout, "\"totalTokens\":140", "telemetry stats total tokens");
            AssertEqual(string.Empty, statsStderr, "telemetry stats stderr");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageImportSupportsPathsOnlyRecoveredPath() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try {
            var currentCodexHome = Path.Combine(tempDir, ".codex");
            var currentSessionsRoot = Path.Combine(currentCodexHome, "sessions");
            var recoveredRoot = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex", "sessions");
            var dbPath = Path.Combine(tempDir, "usage-import-paths-only.db");
            Directory.CreateDirectory(currentSessionsRoot);
            Directory.CreateDirectory(recoveredRoot);

            WriteCodexRolloutFile(
                Path.Combine(currentSessionsRoot, "rollout-2026-03-11T13-00-00-thread-local.jsonl"),
                "thread-local",
                "resp-local",
                includeAuth: false,
                authRoot: currentCodexHome);
            WriteCodexRolloutFile(
                Path.Combine(recoveredRoot, "rollout-2026-03-11T14-00-00-thread-recovered.jsonl"),
                "thread-recovered",
                "resp-recovered",
                includeAuth: false,
                authRoot: Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex"));

            Environment.SetEnvironmentVariable("CODEX_HOME", currentCodexHome);

            var (importExit, importStdout, importStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "import",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--discover",
                    "--path", recoveredRoot,
                    "--paths-only",
                    "--json"
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, importExit, "telemetry import paths-only exit");
            AssertContainsText(importStdout, "\"rootsConsidered\":1", "telemetry import paths-only roots considered");
            AssertContainsText(importStdout, "\"rootsImported\":1", "telemetry import paths-only roots imported");
            AssertContainsText(importStdout, "\"eventsInserted\":1", "telemetry import paths-only inserted");
            AssertEqual(string.Empty, importStderr, "telemetry import paths-only stderr");

            var (statsExit, statsStdout, statsStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "stats", "--db", dbPath, "--provider", "codex", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, statsExit, "telemetry stats paths-only exit");
            AssertContainsText(statsStdout, "\"eventCount\":1", "telemetry stats paths-only event count");
            AssertContainsText(statsStdout, "\"totalTokens\":140", "telemetry stats paths-only total tokens");
            AssertEqual(string.Empty, statsStderr, "telemetry stats paths-only stderr");
        } finally {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageAccountsBindAndListJson() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var (bindExit, bindStdout, bindStderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "accounts", "bind",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--source-root", "src_windows_old",
                    "--match-account-label", "backup",
                    "--account-label", "work",
                    "--person-label", "Przemek",
                    "--json"
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, bindExit, "telemetry accounts bind exit");
            AssertContainsText(bindStdout, "\"providerId\":\"codex\"", "telemetry accounts bind provider");
            AssertContainsText(bindStdout, "\"accountLabel\":\"work\"", "telemetry accounts bind account label");
            AssertContainsText(bindStdout, "\"personLabel\":\"Przemek\"", "telemetry accounts bind person label");
            AssertEqual(string.Empty, bindStderr, "telemetry accounts bind stderr");

            var (listExit, listStdout, listStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "accounts", "list", "--db", dbPath, "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, listExit, "telemetry accounts list exit");
            AssertContainsText(listStdout, "\"sourceRootId\":\"src_windows_old\"", "telemetry accounts list source root");
            AssertContainsText(listStdout, "\"matchAccountLabel\":\"backup\"", "telemetry accounts list match label");
            AssertContainsText(listStdout, "\"accountLabel\":\"work\"", "telemetry accounts list account label");
            AssertContainsText(listStdout, "\"personLabel\":\"Przemek\"", "telemetry accounts list person label");
            AssertEqual(string.Empty, listStderr, "telemetry accounts list stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryUsageOverviewJsonAndExport() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-cli-telemetry-overview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        var exportDir = Path.Combine(temp, "export");

        try {
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                eventStore.Upsert(new UsageEventRecord(
                    eventId: "ev_codex_1",
                    providerId: "codex",
                    adapterId: "codex.logs",
                    sourceRootId: "src_codex",
                    timestampUtc: new DateTimeOffset(2026, 03, 10, 12, 0, 0, TimeSpan.Zero)) {
                    AccountLabel = "work",
                    PersonLabel = "Przemek",
                    Surface = "cli",
                    Model = "gpt-5-codex",
                    InputTokens = 900,
                    OutputTokens = 300,
                    TotalTokens = 1200,
                    TruthLevel = UsageTruthLevel.Exact
                });
                eventStore.Upsert(new UsageEventRecord(
                    eventId: "ev_ix_1",
                    providerId: "ix",
                    adapterId: "ix.client-turn",
                    sourceRootId: "src_ix",
                    timestampUtc: new DateTimeOffset(2026, 03, 11, 12, 0, 0, TimeSpan.Zero)) {
                    AccountLabel = "work",
                    PersonLabel = "Przemek",
                    Surface = "reviewer",
                    Model = "gpt-5.4",
                    InputTokens = 640,
                    OutputTokens = 160,
                    TotalTokens = 800,
                    TruthLevel = UsageTruthLevel.Exact
                });
            }

            var (jsonExit, jsonStdout, jsonStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "overview", "--db", dbPath, "--person", "Przemek", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, jsonExit, "telemetry overview json exit");
            AssertContainsText(jsonStdout, "\"title\":\"Usage Overview\"", "telemetry overview json title");
            AssertContainsText(jsonStdout, "\"subtitle\":\"person: Przemek · 2000 tokens · 2 active days · peak 2026-03-10 (1200)\"", "telemetry overview json subtitle");
            AssertContainsText(jsonStdout, "\"key\":\"total\"", "telemetry overview json card");
            AssertContainsText(jsonStdout, "\"key\":\"surface\"", "telemetry overview json heatmap");
            AssertContainsText(jsonStdout, "\"providerSections\":[", "telemetry overview json provider sections");
            AssertContainsText(jsonStdout, "\"title\":\"Codex\"", "telemetry overview json codex section");
            AssertEqual(string.Empty, jsonStderr, "telemetry overview json stderr");

            var (exportExit, exportStdout, exportStderr) = RunCliDispatchWithCapturedOutput(
                new[] { "telemetry", "usage", "overview", "--db", dbPath, "--person", "Przemek", "--out-dir", exportDir },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exportExit, "telemetry overview export exit");
            AssertContainsText(exportStdout, exportDir, "telemetry overview export output");
            AssertEqual(string.Empty, exportStderr, "telemetry overview export stderr");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "overview.json")), "telemetry overview export overview json");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "index.html")), "telemetry overview export html");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "surface.svg")), "telemetry overview export surface svg");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "provider-codex.svg")), "telemetry overview export provider codex svg");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "surface.svg")), "<svg", "telemetry overview export svg content");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "index.html")), "<img src=\"surface.light.svg\"", "telemetry overview export html surface image");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "index.html")), "INPUT TOKENS", "telemetry overview export html input tokens");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "index.html")), "MOST USED MODEL", "telemetry overview export html most used model");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "index.html")), "<img src=\"provider-codex.light.svg\"", "telemetry overview export html provider image");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestTelemetryUsageReportAutoImportsAndExports() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var codexHome = Path.Combine(tempDir, ".codex");
            var sessionsDir = Path.Combine(codexHome, "sessions");
            var dbPath = Path.Combine(tempDir, "usage-report.db");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T14-00-00-thread-cli.jsonl"),
                "thread-cli",
                "resp-cli",
                includeAuth: false,
                authRoot: codexHome);

            var exportDir = Path.Combine(tempDir, "report");
            var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);

            try {
                var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                    new[] {
                        "telemetry", "usage", "report",
                        "--db", dbPath,
                        "--provider", "codex",
                        "--max-artifacts", "10",
                        "--out-dir", exportDir
                    },
                    () => false,
                    _ => Task.FromResult(0));

                AssertEqual(0, exit, "telemetry report exit");
                AssertContainsText(stdout, exportDir, "telemetry report output");
                AssertEqual(string.Empty, stderr, "telemetry report stderr");
                AssertEqual(true, File.Exists(Path.Combine(exportDir, "index.html")), "telemetry report export html");
                AssertEqual(true, File.Exists(Path.Combine(exportDir, "provider-codex.svg")), "telemetry report provider codex svg");
                AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "index.html")), "INPUT TOKENS", "telemetry report html input tokens");
                AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "overview.json")), "quick scan", "telemetry report quick scan subtitle");
            } finally {
                Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            }
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageReportSupportsAdHocRecoveredPath() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var recoveredRoot = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex", "sessions");
            var dbPath = Path.Combine(tempDir, "usage-report-recovered.db");
            Directory.CreateDirectory(recoveredRoot);
            WriteCodexRolloutFile(
                Path.Combine(recoveredRoot, "rollout-2026-03-11T14-00-00-thread-cli.jsonl"),
                "thread-cli",
                "resp-cli",
                includeAuth: false,
                authRoot: recoveredRoot);

            var exportDir = Path.Combine(tempDir, "report-recovered");
            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "report",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--path", recoveredRoot,
                    "--max-artifacts", "10",
                    "--out-dir", exportDir
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "telemetry report recovered path exit");
            AssertContainsText(stdout, exportDir, "telemetry report recovered path output");
            AssertEqual(string.Empty, stderr, "telemetry report recovered path stderr");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "index.html")), "telemetry report recovered path html");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "index.html")), "Codex", "telemetry report recovered path codex section");
            var overviewJson = File.ReadAllText(Path.Combine(exportDir, "overview.json"));
            AssertContainsText(overviewJson, "roots:", "telemetry report recovered path subtitle has scope");
            AssertContainsText(overviewJson, "recovered", "telemetry report recovered path subtitle includes recovered roots");
            AssertContainsText(overviewJson, "\"key\":\"source-roots\"", "telemetry report recovered path source roots insight key");
            AssertContainsText(overviewJson, "\"title\":\"Scanned roots\"", "telemetry report recovered path source roots insight title");
            AssertContainsText(overviewJson, "\"label\":\"Recovered\"", "telemetry report recovered path source roots recovered label");
            AssertContainsText(overviewJson, "\"scanContext\":", "telemetry report recovered path scan context metadata");
            AssertContainsText(overviewJson, "\"mode\":\"quick-report\"", "telemetry report recovered path scan context mode");
            AssertContainsText(overviewJson, "\"roots\":[", "telemetry report recovered path scan context roots");
            AssertContainsText(overviewJson, "\"sourceKind\":\"RecoveredFolder\"", "telemetry report recovered path scan context source kind");
            AssertContainsText(overviewJson, "\"scope\":\"recovered\"", "telemetry report recovered path scan context scope");
            AssertContainsText(overviewJson, "\"rawEventsCollected\":", "telemetry report recovered path scan context raw event count");
            AssertContainsText(overviewJson, "\"duplicateRecordsCollapsed\":", "telemetry report recovered path scan context collapsed count");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageReportSupportsPathsOnlyRecoveredPath() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try {
            var currentCodexHome = Path.Combine(tempDir, ".codex");
            var currentSessionsRoot = Path.Combine(currentCodexHome, "sessions");
            var recoveredRoot = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex", "sessions");
            var dbPath = Path.Combine(tempDir, "usage-report-recovered-paths-only.db");
            Directory.CreateDirectory(currentSessionsRoot);
            Directory.CreateDirectory(recoveredRoot);
            WriteCodexRolloutFile(
                Path.Combine(currentSessionsRoot, "rollout-2026-03-11T13-00-00-thread-local.jsonl"),
                "thread-local",
                "resp-local",
                includeAuth: false,
                authRoot: currentCodexHome);
            WriteCodexRolloutFile(
                Path.Combine(recoveredRoot, "rollout-2026-03-11T14-00-00-thread-recovered.jsonl"),
                "thread-recovered",
                "resp-recovered",
                includeAuth: false,
                authRoot: Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex"));

            Environment.SetEnvironmentVariable("CODEX_HOME", currentCodexHome);

            var exportDir = Path.Combine(tempDir, "report-recovered-paths-only");
            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "report",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--path", recoveredRoot,
                    "--paths-only",
                    "--max-artifacts", "10",
                    "--out-dir", exportDir
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "telemetry report recovered paths-only exit");
            AssertContainsText(stdout, exportDir, "telemetry report recovered paths-only output");
            AssertEqual(string.Empty, stderr, "telemetry report recovered paths-only stderr");
            var overviewJson = File.ReadAllText(Path.Combine(exportDir, "overview.json"));
            AssertContainsText(overviewJson, "paths-only", "telemetry report recovered paths-only subtitle");
            AssertContainsText(overviewJson, "roots: 1 recovered", "telemetry report recovered paths-only scope");
            AssertContainsText(overviewJson, "\"pathsOnly\":true", "telemetry report recovered paths-only metadata");
            AssertContainsText(overviewJson, "\"scope\":\"recovered\"", "telemetry report recovered paths-only recovered scope");
            AssertEqual(false, overviewJson.Contains("\"scope\":\"local\"", StringComparison.Ordinal), "telemetry report recovered paths-only excludes local scope");
            AssertEqual(false, overviewJson.Contains("\"scope\":\"wsl\"", StringComparison.Ordinal), "telemetry report recovered paths-only excludes wsl scope");
            AssertContainsText(overviewJson, "Includes only explicitly requested --path roots for this report run.", "telemetry report recovered paths-only source roots note");
            AssertContainsText(overviewJson, "\"totalValue\":140", "telemetry report recovered paths-only total");
        } finally {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageReportHighlightsQuickScanDuplicateCollapse() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try {
            var currentCodexHome = Path.Combine(tempDir, ".codex");
            var currentSessionsRoot = Path.Combine(currentCodexHome, "sessions");
            var recoveredSessionsRoot = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex", "sessions");
            var dbPath = Path.Combine(tempDir, "usage-report-dedupe.db");
            Directory.CreateDirectory(currentSessionsRoot);
            Directory.CreateDirectory(recoveredSessionsRoot);

            WriteCodexRolloutFile(
                Path.Combine(currentSessionsRoot, "rollout-2026-03-11T14-00-00-thread-dupe.jsonl"),
                "thread-dedupe",
                "resp-dedupe",
                includeAuth: false,
                authRoot: currentCodexHome);
            WriteCodexRolloutFile(
                Path.Combine(recoveredSessionsRoot, "rollout-2026-03-11T14-00-00-thread-dupe-copy.jsonl"),
                "thread-dedupe",
                "resp-dedupe",
                includeAuth: false,
                authRoot: Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex"));

            Environment.SetEnvironmentVariable("CODEX_HOME", currentCodexHome);

            var exportDir = Path.Combine(tempDir, "report-dedupe");
            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "report",
                    "--db", dbPath,
                    "--provider", "codex",
                    "--path", recoveredSessionsRoot,
                    "--max-artifacts", "10",
                    "--out-dir", exportDir
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "telemetry report duplicate collapse exit");
            AssertContainsText(stdout, exportDir, "telemetry report duplicate collapse output");
            AssertEqual(string.Empty, stderr, "telemetry report duplicate collapse stderr");
            var overviewJson = File.ReadAllText(Path.Combine(exportDir, "overview.json"));
            AssertContainsText(overviewJson, "\"key\":\"quick-scan-dedupe\"", "telemetry report duplicate collapse insight key");
            AssertContainsText(overviewJson, "\"title\":\"Quick-scan dedupe\"", "telemetry report duplicate collapse insight title");
            AssertContainsText(overviewJson, "\"label\":\"Duplicates collapsed\"", "telemetry report duplicate collapse insight label");
            AssertContainsText(overviewJson, "\"label\":\"Unique retained\"", "telemetry report duplicate collapse unique label");
            AssertContainsText(overviewJson, "\"duplicateRecordsCollapsed\":1", "telemetry report duplicate collapse metadata count");
            AssertContainsText(overviewJson, "\"providerDiagnostics\":[", "telemetry report duplicate collapse provider diagnostics");
        } finally {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageReportSupportsAdHocLmStudioPath() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var lmStudioRoot = Path.Combine(tempDir, ".lmstudio");
            var conversationsDir = Path.Combine(lmStudioRoot, "conversations");
            var dbPath = Path.Combine(tempDir, "usage-report-lmstudio.db");
            Directory.CreateDirectory(conversationsDir);

            File.WriteAllText(
                Path.Combine(conversationsDir, "1772555052644.conversation.json"),
                SerializeLmStudioConversation(
                    createdAt: 1772555052644L,
                    assistantLastMessagedAt: 1772608820717L));

            var exportDir = Path.Combine(tempDir, "report-lmstudio");
            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "report",
                    "--db", dbPath,
                    "--provider", "lmstudio",
                    "--path", lmStudioRoot,
                    "--max-artifacts", "10",
                    "--out-dir", exportDir
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "telemetry report lmstudio path exit");
            AssertContainsText(stdout, exportDir, "telemetry report lmstudio path output");
            AssertEqual(string.Empty, stderr, "telemetry report lmstudio path stderr");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "index.html")), "telemetry report lmstudio path html");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "overview.json")), "\"providerId\":\"lmstudio\"", "telemetry report lmstudio provider");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "overview.json")), "\"title\":\"LM Studio\"", "telemetry report lmstudio title");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageReportSupportsAdHocCopilotPath() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var copilotRoot = Path.Combine(tempDir, ".copilot");
            var sessionDir = Path.Combine(copilotRoot, "session-state", "session-a");
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(
                Path.Combine(copilotRoot, "config.json"),
                "{\n  \"last_logged_in_user\": { \"login\": \"octocat\" }\n}");
            File.WriteAllText(
                Path.Combine(sessionDir, "events.jsonl"),
                string.Join(
                    Environment.NewLine,
                    "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"session-a\",\"producer\":\"copilot-agent\",\"copilotVersion\":\"1.0.4\",\"selectedModel\":\"gpt-5.4\"},\"id\":\"evt-start\",\"timestamp\":\"2026-03-13T22:29:10.480Z\"}",
                    "{\"type\":\"assistant.turn_start\",\"data\":{\"turnId\":\"0\"},\"id\":\"evt-turn-start\",\"timestamp\":\"2026-03-13T22:29:28.586Z\"}",
                    "{\"type\":\"assistant.turn_end\",\"data\":{\"turnId\":\"0\"},\"id\":\"evt-turn-end\",\"timestamp\":\"2026-03-13T22:29:29.596Z\"}",
                    "{\"type\":\"session.shutdown\",\"data\":{\"totalApiDurationMs\":3210,\"currentModel\":\"gpt-5.4\",\"modelMetrics\":{\"gpt-5.4\":{\"usage\":{\"inputTokens\":1200,\"outputTokens\":300,\"cacheReadTokens\":100,\"cacheWriteTokens\":50}}}},\"id\":\"evt-shutdown\",\"timestamp\":\"2026-03-13T22:31:00.000Z\"}"));

            var exportDir = Path.Combine(tempDir, "report-copilot");
            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "report",
                    "--path", copilotRoot,
                    "--provider", "copilot",
                    "--max-artifacts", "10",
                    "--out-dir", exportDir
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "telemetry report copilot path exit");
            AssertContainsText(stdout, exportDir, "telemetry report copilot path output");
            AssertEqual(string.Empty, stderr, "telemetry report copilot path stderr");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "index.html")), "telemetry report copilot path html");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "overview.json")), "\"providerId\":\"copilot\"", "telemetry report copilot provider");
            AssertContainsText(File.ReadAllText(Path.Combine(exportDir, "overview.json")), "\"title\":\"GitHub Copilot\"", "telemetry report copilot title");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageReportFullImportSupportsAdHocRecoveredPath() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try {
            var currentCodexHome = Path.Combine(tempDir, ".codex");
            var currentSessionsRoot = Path.Combine(currentCodexHome, "sessions");
            var recoveredRoot = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex", "sessions");
            var dbPath = Path.Combine(tempDir, "usage-report-full-recovered.db");
            Directory.CreateDirectory(currentSessionsRoot);
            Directory.CreateDirectory(recoveredRoot);
            WriteCodexRolloutFile(
                Path.Combine(currentSessionsRoot, "rollout-2026-03-11T13-00-00-thread-local.jsonl"),
                "thread-local",
                "resp-local",
                includeAuth: false,
                authRoot: currentCodexHome);
            WriteCodexRolloutFile(
                Path.Combine(recoveredRoot, "rollout-2026-03-11T14-00-00-thread-cli.jsonl"),
                "thread-cli",
                "resp-cli",
                includeAuth: false,
                authRoot: recoveredRoot);

            Environment.SetEnvironmentVariable("CODEX_HOME", currentCodexHome);

            var exportDir = Path.Combine(tempDir, "report-full-recovered");
            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] {
                    "telemetry", "usage", "report",
                    "--db", dbPath,
                    "--path", recoveredRoot,
                    "--paths-only",
                    "--provider", "codex",
                    "--full-import",
                    "--out-dir", exportDir
                },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "telemetry report full-import recovered path exit");
            AssertContainsText(stdout, exportDir, "telemetry report full-import recovered path output");
            AssertEqual(string.Empty, stderr, "telemetry report full-import recovered path stderr");
            AssertEqual(true, File.Exists(Path.Combine(exportDir, "index.html")), "telemetry report full-import recovered path html");
            var overviewJson = File.ReadAllText(Path.Combine(exportDir, "overview.json"));
            AssertContainsText(overviewJson, "\"providerId\":\"codex\"", "telemetry report full-import recovered path provider");
            AssertContainsText(overviewJson, "paths-only", "telemetry report full-import recovered path subtitle");
            AssertContainsText(overviewJson, "\"totalValue\":140", "telemetry report full-import recovered path total");
        } finally {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestTelemetryUsageBuildGitHubSectionRequestsSupportsOwnerOnlyRuns() {
        var requests = UsageTelemetryCliRunner.BuildGitHubSectionRequests(
            Array.Empty<string>(),
            new[] { "EvotecIT", "evotecit", "  " });

        AssertEqual(1, requests.Count, "telemetry github owner-only request count");
        AssertEqual(null, requests[0].Login, "telemetry github owner-only login");
        AssertEqual(1, requests[0].Owners.Count, "telemetry github owner-only owners count");
        AssertEqual("EvotecIT", requests[0].Owners[0], "telemetry github owner-only owner");
    }
#endif
}
