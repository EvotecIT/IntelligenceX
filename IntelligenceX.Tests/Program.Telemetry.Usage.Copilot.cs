using System;
using System.IO;
using System.Linq;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Copilot;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestCopilotSessionUsageAdapterImportsCliTurnActivity() {
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
                    "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"session-a\",\"producer\":\"copilot-agent\",\"copilotVersion\":\"1.0.4\"},\"id\":\"evt-start\",\"timestamp\":\"2026-03-13T22:29:10.480Z\"}",
                    "{\"type\":\"assistant.turn_start\",\"data\":{\"turnId\":\"0\",\"interactionId\":\"i-1\"},\"id\":\"evt-turn-start\",\"timestamp\":\"2026-03-13T22:29:28.586Z\"}",
                    "{\"type\":\"assistant.turn_end\",\"data\":{\"turnId\":\"0\"},\"id\":\"evt-turn-end\",\"timestamp\":\"2026-03-13T22:29:29.596Z\"}",
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

            AssertEqual(1, imported.Count, "copilot imported event count");
            AssertEqual("session-a", imported[0].SessionId, "copilot session id");
            AssertEqual("0", imported[0].TurnId, "copilot turn id");
            AssertEqual("evt-turn-end", imported[0].ResponseId, "copilot response id");
            AssertEqual("octocat", imported[0].ProviderAccountId, "copilot provider account id");
            AssertEqual("cli-box", imported[0].MachineId, "copilot machine id");
            AssertEqual("cli", imported[0].Surface, "copilot surface");
            AssertEqual("copilot-agent/1.0.4", imported[0].Model, "copilot model label");
            AssertEqual(1010L, imported[0].DurationMs, "copilot turn duration");
            AssertEqual(UsageTruthLevel.Inferred, imported[0].TruthLevel, "copilot truth level");
            AssertEqual(null, imported[0].TotalTokens, "copilot total tokens remain unknown");
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
}
