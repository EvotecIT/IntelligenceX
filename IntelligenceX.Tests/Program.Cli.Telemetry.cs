using System;
using System.IO;
using System.Threading.Tasks;
#if !NET472
using IntelligenceX.Cli.Telemetry;
#endif
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
        AssertEqual(string.Empty, stderr, "telemetry usage help stderr");
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
        } finally {
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
