using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Cli.Telemetry;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Copilot;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestCopilotSessionUsageAdapterImportsCliTurnActivity() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var copilotRoot = Path.Combine(tempDir, ".copilot");
            var sessionDir = Path.Combine(copilotRoot, "session-state", "session-a");
            var logsDir = Path.Combine(copilotRoot, "logs");
            Directory.CreateDirectory(sessionDir);
            Directory.CreateDirectory(logsDir);
            File.WriteAllText(
                Path.Combine(copilotRoot, "config.json"),
                "{\n  \"last_logged_in_user\": { \"login\": \"octocat\" }\n}");
            File.WriteAllText(
                Path.Combine(logsDir, "process-1.log"),
                string.Join(
                    Environment.NewLine,
                    "2026-03-13T22:29:10.481Z [INFO] Registering foreground session: session-a",
                    "2026-03-13T22:29:11.720Z [INFO] Using default model: claude-sonnet-4.6"));
            File.WriteAllText(
                Path.Combine(sessionDir, "events.jsonl"),
                string.Join(
                    Environment.NewLine,
                    "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"session-a\",\"producer\":\"copilot-agent\",\"copilotVersion\":\"1.0.4\"},\"id\":\"evt-start\",\"timestamp\":\"2026-03-13T22:29:10.480Z\"}",
                    "{\"type\":\"assistant.turn_start\",\"data\":{\"turnId\":\"0\",\"interactionId\":\"i-1\"},\"id\":\"evt-turn-start\",\"timestamp\":\"2026-03-13T22:29:28.586Z\"}",
                    "{\"type\":\"assistant.turn_end\",\"data\":{\"turnId\":\"0\"},\"id\":\"evt-turn-end\",\"timestamp\":\"2026-03-13T22:29:29.596Z\"}",
                    "{\"type\":\"session.error\",\"data\":{\"errorType\":\"quota\",\"message\":\"402 You have no quota\",\"statusCode\":402},\"id\":\"evt-error\",\"timestamp\":\"2026-03-13T22:29:29.597Z\"}",
                    "{\"type\":\"session.info\",\"data\":{\"infoType\":\"authentication\",\"message\":\"Signed in successfully as octocat!\"},\"id\":\"evt-auth\",\"timestamp\":\"2026-03-13T22:29:57.585Z\"}"));

            var adapter = new CopilotSessionUsageAdapter();
            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId("copilot", UsageSourceKind.LocalLogs, copilotRoot),
                "copilot",
                UsageSourceKind.LocalLogs,
                copilotRoot);

            var imported = adapter.ImportAsync(root, new UsageImportContext {
                MachineId = "cli-box"
            }).GetAwaiter().GetResult();

            AssertEqual(2, imported.Count, "copilot imported event count");
            var turn = imported.Single(item => string.Equals(item.Surface, "cli", StringComparison.OrdinalIgnoreCase));
            var error = imported.Single(item => string.Equals(item.Surface, "cli-error", StringComparison.OrdinalIgnoreCase));

            AssertEqual("session-a", turn.SessionId, "copilot session id");
            AssertEqual("0", turn.TurnId, "copilot turn id");
            AssertEqual("evt-turn-end", turn.ResponseId, "copilot response id");
            AssertEqual("octocat", turn.ProviderAccountId, "copilot provider account id");
            AssertEqual("cli-box", turn.MachineId, "copilot machine id");
            AssertEqual("cli", turn.Surface, "copilot surface");
            AssertEqual("claude-sonnet-4.6", turn.Model, "copilot model label");
            AssertEqual(1010L, turn.DurationMs, "copilot turn duration");
            AssertEqual(UsageTruthLevel.Inferred, turn.TruthLevel, "copilot truth level");
            AssertEqual(null, turn.TotalTokens, "copilot total tokens remain unknown");
            AssertEqual("claude-sonnet-4.6", error.Model, "copilot error record model label");
            AssertEqual("evt-error", error.ResponseId, "copilot error response id");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestCopilotSessionUsageAdapterImportsSessionShutdownUsageMetrics() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var copilotRoot = Path.Combine(tempDir, ".copilot");
            var sessionDir = Path.Combine(copilotRoot, "session-state", "session-c");
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(
                Path.Combine(copilotRoot, "config.json"),
                "{\n  \"last_logged_in_user\": { \"login\": \"octocat\" }\n}");
            File.WriteAllText(
                Path.Combine(sessionDir, "events.jsonl"),
                string.Join(
                    Environment.NewLine,
                    "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"session-c\",\"producer\":\"copilot-agent\",\"copilotVersion\":\"1.0.4\",\"selectedModel\":\"gpt-5.4\"},\"id\":\"evt-start\",\"timestamp\":\"2026-03-13T22:29:10.480Z\"}",
                    "{\"type\":\"assistant.turn_start\",\"data\":{\"turnId\":\"0\",\"interactionId\":\"i-1\"},\"id\":\"evt-turn-start\",\"timestamp\":\"2026-03-13T22:29:28.586Z\"}",
                    "{\"type\":\"assistant.turn_end\",\"data\":{\"turnId\":\"0\"},\"id\":\"evt-turn-end\",\"timestamp\":\"2026-03-13T22:29:29.596Z\"}",
                    "{\"type\":\"session.shutdown\",\"data\":{\"totalPremiumRequests\":2,\"totalApiDurationMs\":3210,\"currentModel\":\"gpt-5.4\",\"modelMetrics\":{\"gpt-5.4\":{\"requests\":{\"count\":2,\"cost\":2},\"usage\":{\"inputTokens\":1200,\"outputTokens\":300,\"cacheReadTokens\":100,\"cacheWriteTokens\":50}}}},\"id\":\"evt-shutdown\",\"timestamp\":\"2026-03-13T22:31:00.000Z\"}"));

            var adapter = new CopilotSessionUsageAdapter();
            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId("copilot", UsageSourceKind.LocalLogs, copilotRoot),
                "copilot",
                UsageSourceKind.LocalLogs,
                copilotRoot);

            var imported = adapter.ImportAsync(root, new UsageImportContext {
                MachineId = "cli-box"
            }).GetAwaiter().GetResult();

            AssertEqual(2, imported.Count, "copilot imported turn plus shutdown summary count");
            var summary = imported.Single(item => string.Equals(item.Surface, "cli-session-summary", StringComparison.OrdinalIgnoreCase));
            AssertEqual("gpt-5.4", summary.Model, "copilot shutdown usage model");
            AssertEqual(1200L, summary.InputTokens, "copilot shutdown input tokens");
            AssertEqual(100L, summary.CachedInputTokens, "copilot shutdown cached input tokens");
            AssertEqual(300L, summary.OutputTokens, "copilot shutdown output tokens");
            AssertEqual(1650L, summary.TotalTokens, "copilot shutdown total tokens");
            AssertEqual(3210L, summary.DurationMs, "copilot shutdown total api duration");
            AssertEqual(UsageTruthLevel.Exact, summary.TruthLevel, "copilot shutdown truth level");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestCopilotDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalCopilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        try {
            var recoveredProfile = Path.Combine(tempDir, "Windows.old", "Users", "backup-user");
            var wslProfile = Path.Combine(tempDir, "wsl", "Ubuntu", "home", "dev");
            var recoveredRoot = Path.Combine(recoveredProfile, ".copilot");
            var wslRoot = Path.Combine(wslProfile, ".copilot");
            Environment.SetEnvironmentVariable("COPILOT_HOME", Path.Combine(tempDir, "no-current-profile-root"));

            Directory.CreateDirectory(Path.Combine(recoveredRoot, "session-state"));
            Directory.CreateDirectory(Path.Combine(wslRoot, "session-state"));

            var discovery = new CopilotDefaultSourceRootDiscovery(
                new UsageTelemetryExternalProfileDiscovery(() => new[] {
                    new UsageTelemetryExternalProfile(UsageSourceKind.RecoveredFolder, recoveredProfile, "windows-old"),
                    new UsageTelemetryExternalProfile(UsageSourceKind.LocalLogs, wslProfile, "wsl", "Ubuntu")
                }));

            var roots = discovery.DiscoverRoots();

            AssertEqual(2, roots.Count, "copilot supplemental discovered root count");
            AssertEqual(true, roots.Any(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(recoveredRoot), StringComparison.OrdinalIgnoreCase) && root.SourceKind == UsageSourceKind.RecoveredFolder), "copilot recovered root discovered");

            var wslDiscovered = roots.Single(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(wslRoot), StringComparison.OrdinalIgnoreCase));
            AssertEqual(UsageSourceKind.LocalLogs, wslDiscovered.SourceKind, "copilot wsl source kind");
            AssertEqual("wsl", wslDiscovered.PlatformHint, "copilot wsl platform hint");
            AssertEqual("Ubuntu", wslDiscovered.MachineLabel, "copilot wsl machine label");
        } finally {
            Environment.SetEnvironmentVariable("COPILOT_HOME", originalCopilotHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorDiscoversCopilotRootFromCurrentProfile() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalCopilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        try {
            var copilotRoot = Path.Combine(tempDir, ".copilot");
            Environment.SetEnvironmentVariable("COPILOT_HOME", copilotRoot);
            var sessionDir = Path.Combine(copilotRoot, "session-state", "session-b");
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(
                Path.Combine(copilotRoot, "config.json"),
                "{\n  \"last_logged_in_user\": { \"login\": \"octocat\" }\n}");
            File.WriteAllText(
                Path.Combine(sessionDir, "events.jsonl"),
                string.Join(
                    Environment.NewLine,
                    "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"session-b\",\"producer\":\"copilot-agent\",\"copilotVersion\":\"1.0.4\"},\"id\":\"evt-start\",\"timestamp\":\"2026-03-13T22:29:10.480Z\"}",
                    "{\"type\":\"assistant.turn_start\",\"data\":{\"turnId\":\"1\",\"interactionId\":\"i-2\"},\"id\":\"evt-turn-start\",\"timestamp\":\"2026-03-13T22:29:28.000Z\"}",
                    "{\"type\":\"assistant.turn_end\",\"data\":{\"turnId\":\"1\"},\"id\":\"evt-turn-end\",\"timestamp\":\"2026-03-13T22:29:30.000Z\"}"));

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CopilotUsageTelemetryProviderDescriptor()
                }),
                new IUsageTelemetryRootDiscovery[] {
                    new CopilotDefaultSourceRootDiscovery(
                        new UsageTelemetryExternalProfileDiscovery(() => Array.Empty<UsageTelemetryExternalProfile>()))
                });

            var discovered = coordinator.DiscoverRootsAsync("copilot").GetAwaiter().GetResult();
            AssertEqual(1, discovered.Count, "discovered copilot roots");
            AssertEqual(UsageTelemetryIdentity.NormalizePath(copilotRoot), discovered[0].Path, "discovered copilot root path");

            var imported = coordinator.ImportAllAsync(new UsageImportContext { MachineId = "copilot-box" }, "copilot")
                .GetAwaiter().GetResult();
            AssertEqual(1, imported.RootsConsidered, "copilot batch roots considered");
            AssertEqual(1, imported.RootsImported, "copilot batch roots imported");
            AssertEqual(1, imported.EventsRead, "copilot batch events read");
            AssertEqual(1, imported.EventsInserted, "copilot batch events inserted");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "copilot imported event count");
            AssertEqual("copilot-box", events[0].MachineId, "copilot imported machine id");
            AssertEqual(2000L, events[0].DurationMs, "copilot imported duration");
            AssertEqual("octocat", events[0].ProviderAccountId, "copilot imported account");
        } finally {
            Environment.SetEnvironmentVariable("COPILOT_HOME", originalCopilotHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestCopilotQuotaSnapshotClientParsesDirectQuotaSnapshots() {
        using var json = JsonDocument.Parse("""
            {
              "copilot_plan": "pro",
              "assigned_date": "2026-03-01T00:00:00Z",
              "quota_reset_date": "2026-04-01T00:00:00Z",
              "quota_snapshots": {
                "premium_interactions": {
                  "entitlement": 300,
                  "remaining": 0,
                  "percent_remaining": 0,
                  "quota_id": "premium_interactions"
                },
                "chat": {
                  "entitlement": 500,
                  "remaining": 125,
                  "percent_remaining": 25,
                  "quota_id": "chat"
                }
              }
            }
            """);

        var snapshot = CopilotQuotaSnapshotClient.ParseSnapshot(json.RootElement);

        AssertEqual("pro", snapshot?.Plan ?? string.Empty, "copilot quota direct plan");
        AssertEqual("premium_interactions", snapshot?.PremiumInteractions?.QuotaId ?? string.Empty, "copilot quota direct premium id");
        AssertEqual(300d, snapshot?.PremiumInteractions?.Entitlement ?? 0d, "copilot quota direct premium entitlement");
        AssertEqual(0d, snapshot?.PremiumInteractions?.Remaining ?? -1d, "copilot quota direct premium remaining");
        AssertEqual(100d, snapshot?.PremiumInteractions?.UsedPercent ?? 0d, "copilot quota direct premium used percent");
        AssertEqual(25d, snapshot?.Chat?.PercentRemaining ?? 0d, "copilot quota direct chat remaining percent");
        AssertEqual("2026-04-01T00:00:00Z", snapshot?.QuotaResetDateRaw ?? string.Empty, "copilot quota direct reset raw");
        AssertEqual(true, snapshot?.QuotaResetDate.HasValue ?? false, "copilot quota direct reset parsed");
    }

    private static void TestCopilotQuotaSnapshotClientParsesMonthlyQuotaFallback() {
        using var json = JsonDocument.Parse("""
            {
              "copilot_plan": "free",
              "monthly_quotas": {
                "chat": 200,
                "completions": 300
              },
              "limited_user_quotas": {
                "chat": 75,
                "completions": 60
              }
            }
            """);

        var snapshot = CopilotQuotaSnapshotClient.ParseSnapshot(json.RootElement);

        AssertEqual("free", snapshot?.Plan ?? string.Empty, "copilot quota fallback plan");
        AssertEqual("completions", snapshot?.PremiumInteractions?.QuotaId ?? string.Empty, "copilot quota fallback premium id");
        AssertEqual(300d, snapshot?.PremiumInteractions?.Entitlement ?? 0d, "copilot quota fallback premium entitlement");
        AssertEqual(60d, snapshot?.PremiumInteractions?.Remaining ?? 0d, "copilot quota fallback premium remaining");
        AssertEqual(20d, snapshot?.PremiumInteractions?.PercentRemaining ?? 0d, "copilot quota fallback premium percent");
        AssertEqual("chat", snapshot?.Chat?.QuotaId ?? string.Empty, "copilot quota fallback chat id");
        AssertEqual(75d, snapshot?.Chat?.Remaining ?? 0d, "copilot quota fallback chat remaining");
        AssertEqual(37.5d, snapshot?.Chat?.PercentRemaining ?? 0d, "copilot quota fallback chat percent");
    }

    private static void TestUsageTelemetryCliRunnerAppendsCopilotQuotaInsight() {
        var overview = new UsageTelemetryOverviewBuilder().Build(
            new[] {
                new UsageEventRecord("evt-1", "copilot", "copilot.session-state", "src-1",
                    new DateTimeOffset(2026, 03, 13, 22, 29, 29, TimeSpan.Zero)) {
                    ProviderAccountId = "octocat",
                    Surface = "cli",
                    SessionId = "session-a",
                    ThreadId = "session-a",
                    TurnId = "0",
                    Model = "claude-sonnet-4.6",
                    DurationMs = 1010,
                    TruthLevel = UsageTruthLevel.Inferred
                }
            },
            new UsageTelemetryOverviewOptions {
                Title = "Copilot Usage"
            });

        var enriched = UsageTelemetryCliRunner.ApplyCopilotQuotaSnapshot(
            overview,
            new CopilotQuotaSnapshot(
                "pro",
                null,
                null,
                "2026-04-01T00:00:00Z",
                new DateTimeOffset(2026, 04, 01, 0, 0, 0, TimeSpan.Zero),
                new CopilotQuotaWindow("premium_interactions", 300, 0, 0, true),
                new CopilotQuotaWindow("chat", 500, 125, 25, true)));

        var section = enriched.ProviderSections.Single();
        AssertEqual(2, section.AdditionalInsights.Count, "copilot enriched insight count");
        AssertEqual("copilot-github-quota", section.AdditionalInsights[0].Key, "copilot quota insight key");
        AssertContainsText(section.AdditionalInsights[0].Headline ?? string.Empty, "GitHub Copilot Pro", "copilot quota headline includes plan");
        AssertContainsText(section.AdditionalInsights[0].Headline ?? string.Empty, "100", "copilot quota headline includes premium usage");
        AssertContainsText(section.Note ?? string.Empty, "Premium remaining 0/300", "copilot section note includes premium remaining");
        AssertContainsText(section.AdditionalInsights[0].Rows[1].Subtitle ?? string.Empty, "% used", "copilot premium row subtitle includes used percent");
    }
}
