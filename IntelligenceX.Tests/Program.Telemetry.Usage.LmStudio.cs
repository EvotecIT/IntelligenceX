using System;
using System.IO;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.LmStudio;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestLmStudioConversationUsageAdapterImportsSelectedAssistantGenerations() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var lmStudioRoot = Path.Combine(tempDir, ".lmstudio");
            var conversationsDir = Path.Combine(lmStudioRoot, "conversations");
            Directory.CreateDirectory(conversationsDir);

            var conversationPath = Path.Combine(conversationsDir, "1772555052644.conversation.json");
            File.WriteAllText(conversationPath, SerializeLmStudioConversation(
                createdAt: 1772555052644L,
                assistantLastMessagedAt: 1772608820717L));

            var adapter = new LmStudioConversationUsageAdapter();
            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId("lmstudio", UsageSourceKind.LocalLogs, lmStudioRoot),
                "lmstudio",
                UsageSourceKind.LocalLogs,
                lmStudioRoot) {
                AccountHint = "local-lab"
            };

            var imported = adapter.ImportAsync(root, new UsageImportContext {
                MachineId = "lm-box"
            }).GetAwaiter().GetResult();

            AssertEqual(1, imported.Count, "lmstudio imported event count");
            AssertEqual("1772555052644", imported[0].SessionId, "lmstudio session id");
            AssertEqual("1772608817846-0.6775234814535578", imported[0].ResponseId, "lmstudio response id");
            AssertEqual("message-2", imported[0].TurnId, "lmstudio turn id");
            AssertEqual("qwen/qwen3.5-9b", imported[0].Model, "lmstudio model");
            AssertEqual("chat", imported[0].Surface, "lmstudio surface");
            AssertEqual("local-lab", imported[0].AccountLabel, "lmstudio account label");
            AssertEqual("lm-box", imported[0].MachineId, "lmstudio machine id");
            AssertEqual(12L, imported[0].InputTokens, "lmstudio input tokens");
            AssertEqual(243L, imported[0].OutputTokens, "lmstudio output tokens");
            AssertEqual(255L, imported[0].TotalTokens, "lmstudio total tokens");
            AssertEqual(40397L, imported[0].DurationMs, "lmstudio duration");
            AssertEqual(new DateTimeOffset(2026, 03, 04, 07, 20, 17, 846, TimeSpan.Zero), imported[0].TimestampUtc, "lmstudio timestamp");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestLmStudioDefaultSourceRootDiscoveryUsesEnvironmentRoot() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalLmStudioHome = Environment.GetEnvironmentVariable("LMSTUDIO_HOME");
        try {
            var lmStudioRoot = Path.Combine(tempDir, ".lmstudio");
            Directory.CreateDirectory(Path.Combine(lmStudioRoot, "conversations"));
            Environment.SetEnvironmentVariable("LMSTUDIO_HOME", lmStudioRoot);

            var discovery = new LmStudioDefaultSourceRootDiscovery(
                new UsageTelemetryExternalProfileDiscovery(() => Array.Empty<UsageTelemetryExternalProfile>()));
            var roots = discovery.DiscoverRoots();

            AssertEqual(1, roots.Count, "lmstudio discovered root count");
            AssertEqual("lmstudio", roots[0].ProviderId, "lmstudio discovered provider");
            AssertEqual(UsageTelemetryIdentity.NormalizePath(lmStudioRoot), roots[0].Path, "lmstudio discovered path");
        } finally {
            Environment.SetEnvironmentVariable("LMSTUDIO_HOME", originalLmStudioHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestLmStudioDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalLmStudioHome = Environment.GetEnvironmentVariable("LMSTUDIO_HOME");
        var originalLmStudioHomeAlias = Environment.GetEnvironmentVariable("LM_STUDIO_HOME");
        try {
            var recoveredProfile = Path.Combine(tempDir, "Windows.old", "Users", "backup-user");
            var wslProfile = Path.Combine(tempDir, "wsl", "Ubuntu", "home", "dev");
            var recoveredRoot = Path.Combine(recoveredProfile, ".lmstudio");
            var wslRoot = Path.Combine(wslProfile, ".lmstudio");
            var overrideRoot = Path.Combine(tempDir, "provider-override");

            Directory.CreateDirectory(Path.Combine(recoveredRoot, "conversations"));
            Directory.CreateDirectory(Path.Combine(wslRoot, "conversations"));
            Directory.CreateDirectory(overrideRoot);
            Environment.SetEnvironmentVariable("LMSTUDIO_HOME", overrideRoot);
            Environment.SetEnvironmentVariable("LM_STUDIO_HOME", null);

            var discovery = new LmStudioDefaultSourceRootDiscovery(
                new UsageTelemetryExternalProfileDiscovery(() => new[] {
                    new UsageTelemetryExternalProfile(UsageSourceKind.RecoveredFolder, recoveredProfile, "windows-old"),
                    new UsageTelemetryExternalProfile(UsageSourceKind.LocalLogs, wslProfile, "wsl", "Ubuntu")
                }));

            var roots = discovery.DiscoverRoots();

            AssertEqual(2, roots.Count, "lmstudio supplemental discovered root count");
            AssertEqual(true, roots.Any(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(recoveredRoot), StringComparison.OrdinalIgnoreCase) && root.SourceKind == UsageSourceKind.RecoveredFolder), "lmstudio recovered root discovered");

            var wslDiscovered = roots.Single(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(wslRoot), StringComparison.OrdinalIgnoreCase));
            AssertEqual(UsageSourceKind.LocalLogs, wslDiscovered.SourceKind, "lmstudio wsl source kind");
            AssertEqual("wsl", wslDiscovered.PlatformHint, "lmstudio wsl platform hint");
            AssertEqual("Ubuntu", wslDiscovered.MachineLabel, "lmstudio wsl machine label");
        } finally {
            Environment.SetEnvironmentVariable("LMSTUDIO_HOME", originalLmStudioHome);
            Environment.SetEnvironmentVariable("LM_STUDIO_HOME", originalLmStudioHomeAlias);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorDiscoversLmStudioRootFromEnvironment() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalLmStudioHome = Environment.GetEnvironmentVariable("LMSTUDIO_HOME");
        try {
            var lmStudioRoot = Path.Combine(tempDir, ".lmstudio");
            var conversationsDir = Path.Combine(lmStudioRoot, "conversations");
            Directory.CreateDirectory(conversationsDir);
            Environment.SetEnvironmentVariable("LMSTUDIO_HOME", lmStudioRoot);

            File.WriteAllText(
                Path.Combine(conversationsDir, "1772555052644.conversation.json"),
                SerializeLmStudioConversation(
                    createdAt: 1772555052644L,
                    assistantLastMessagedAt: 1772608820717L));

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new LmStudioUsageTelemetryProviderDescriptor()
                }),
                new IUsageTelemetryRootDiscovery[] {
                    new LmStudioDefaultSourceRootDiscovery(
                        new UsageTelemetryExternalProfileDiscovery(() => Array.Empty<UsageTelemetryExternalProfile>()))
                });

            var discovered = coordinator.DiscoverRootsAsync("lmstudio").GetAwaiter().GetResult();
            AssertEqual(1, discovered.Count, "discovered lmstudio roots");
            AssertEqual(UsageTelemetryIdentity.NormalizePath(lmStudioRoot), discovered[0].Path, "discovered lmstudio root path");

            var imported = coordinator.ImportAllAsync(new UsageImportContext { MachineId = "lmstudio-box" }, "lmstudio")
                .GetAwaiter().GetResult();
            AssertEqual(1, imported.RootsConsidered, "lmstudio batch roots considered");
            AssertEqual(1, imported.RootsImported, "lmstudio batch roots imported");
            AssertEqual(1, imported.EventsRead, "lmstudio batch events read");
            AssertEqual(1, imported.EventsInserted, "lmstudio batch events inserted");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "lmstudio imported event count");
            AssertEqual("lmstudio-box", events[0].MachineId, "lmstudio imported machine id");
            AssertEqual(255L, events[0].TotalTokens, "lmstudio imported total tokens");
        } finally {
            Environment.SetEnvironmentVariable("LMSTUDIO_HOME", originalLmStudioHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryQuickReportScannerSupportsLmStudioAliasProviderFilters() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var lmStudioRoot = Path.Combine(tempDir, ".lmstudio");
            var conversationsDir = Path.Combine(lmStudioRoot, "conversations");
            Directory.CreateDirectory(conversationsDir);

            File.WriteAllText(
                Path.Combine(conversationsDir, "1772555052644.conversation.json"),
                SerializeLmStudioConversation(
                    createdAt: 1772555052644L,
                    assistantLastMessagedAt: 1772608820717L));

            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId("lm-studio", UsageSourceKind.LocalLogs, lmStudioRoot),
                "lm-studio",
                UsageSourceKind.LocalLogs,
                lmStudioRoot);

            var result = new UsageTelemetryQuickReportScanner().ScanAsync(
                new[] { root },
                new UsageTelemetryQuickReportOptions { ProviderId = "lmstudio" })
                .GetAwaiter()
                .GetResult();

            AssertEqual(1, result.RootsConsidered, "lmstudio quick report alias roots considered");
            AssertEqual(1, result.ArtifactsParsed, "lmstudio quick report alias artifacts parsed");
            AssertEqual(1, result.Events.Count, "lmstudio quick report alias events count");
            AssertEqual("lmstudio", result.Events[0].ProviderId, "lmstudio quick report canonical provider id");
            AssertEqual(255L, result.Events[0].TotalTokens, "lmstudio quick report alias total tokens");
            AssertEqual("chat", result.Events[0].Surface, "lmstudio quick report alias surface");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    internal static string SerializeLmStudioConversation(long createdAt, long assistantLastMessagedAt) {
        var assistantStep = new JsonObject()
            .Add("type", "contentBlock")
            .Add("stepIdentifier", "1772608817846-0.6775234814535578")
            .Add("genInfo", new JsonObject()
                .Add("identifier", "qwen/qwen3.5-9b")
                .Add("indexedModelIdentifier", "qwen/qwen3.5-9b")
                .Add("stats", new JsonObject()
                    .Add("promptTokensCount", 12L)
                    .Add("predictedTokensCount", 243L)
                    .Add("totalTokensCount", 255L)
                    .Add("totalTimeSec", 40.397d)));

        var assistantVersion = new JsonObject()
            .Add("role", "assistant")
            .Add("senderInfo", new JsonObject()
                .Add("senderName", "qwen/qwen3.5-9b"))
            .Add("steps", new JsonArray()
                .Add(assistantStep));

        var userVersion = new JsonObject()
            .Add("role", "user")
            .Add("type", "singleStep");

        var conversation = new JsonObject()
            .Add("name", "Greeting Exchange")
            .Add("createdAt", createdAt)
            .Add("assistantLastMessagedAt", assistantLastMessagedAt)
            .Add("lastUsedModel", new JsonObject()
                .Add("identifier", "qwen/qwen3.5-9b"))
            .Add("messages", new JsonArray()
                .Add(new JsonObject()
                    .Add("currentlySelected", 0L)
                    .Add("versions", new JsonArray()
                        .Add(userVersion)))
                .Add(new JsonObject()
                    .Add("currentlySelected", 0L)
                    .Add("versions", new JsonArray()
                        .Add(assistantVersion))));

        return JsonLite.Serialize(JsonValue.From(conversation));
    }
}
