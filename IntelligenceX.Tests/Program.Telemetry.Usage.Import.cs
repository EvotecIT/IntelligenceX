using System;
using System.IO;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Codex;
using IntelligenceX.Telemetry.Usage.LmStudio;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryProviderRegistryReturnsCodexAdapter() {
        var registry = new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
            new CodexUsageTelemetryProviderDescriptor(),
            new LmStudioUsageTelemetryProviderDescriptor()
        });

        var adapters = registry.GetAdapters("codex");
        AssertEqual(1, adapters.Count, "codex adapter count");
        AssertEqual(CodexSessionUsageAdapter.StableAdapterId, adapters[0].AdapterId, "codex adapter id");

        var lmStudioAdapters = registry.GetAdapters("lmstudio");
        AssertEqual(1, lmStudioAdapters.Count, "lmstudio adapter count");
        AssertEqual(LmStudioConversationUsageAdapter.StableAdapterId, lmStudioAdapters[0].AdapterId, "lmstudio adapter id");
    }

    private static void TestUsageTelemetryImportCoordinatorRegistersAndImportsManualRoot() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T14-00-00-thread-manual.jsonl"),
                "thread-manual",
                "resp-manual",
                includeAuth: false,
                authRoot: tempDir);

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("codex", UsageSourceKind.RecoveredFolder, sessionsDir, accountHint: "backup");
            var result = coordinator.ImportRootAsync(root.Id, new UsageImportContext { MachineId = "machine-manual" })
                .GetAwaiter().GetResult();

            AssertEqual(true, result.Imported, "manual root imported");
            AssertEqual(1, result.AdapterIds.Count, "manual adapter count");
            AssertEqual(1, result.ArtifactsProcessed, "manual artifacts processed");
            AssertEqual(1, result.EventsRead, "manual events read");
            AssertEqual(1, result.EventsInserted, "manual events inserted");
            AssertEqual(0, result.EventsUpdated, "manual events updated");
            AssertEqual(true, rootStore.TryGet(root.Id, out var storedRoot), "manual root persisted");
            AssertEqual("backup", storedRoot.AccountHint, "manual root account hint");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "manual imported event count");
            AssertEqual("machine-manual", events[0].MachineId, "manual imported machine id");
            AssertEqual("backup", events[0].AccountLabel, "manual imported account label");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorImportsCodexDirectFileRoot() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var rolloutPath = Path.Combine(tempDir, "rollout-2026-03-11T14-10-00-thread-direct.jsonl");
            WriteCodexRolloutFile(
                rolloutPath,
                "thread-direct",
                "resp-direct",
                includeAuth: false,
                authRoot: tempDir);

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("codex", UsageSourceKind.RecoveredFolder, rolloutPath, accountHint: "direct");
            var result = coordinator.ImportRootAsync(root.Id, new UsageImportContext { MachineId = "machine-direct" })
                .GetAwaiter().GetResult();

            AssertEqual(true, result.Imported, "direct root imported");
            AssertEqual(1, result.ArtifactsProcessed, "direct root artifacts processed");
            AssertEqual(1, result.EventsRead, "direct root events read");
            AssertEqual(1, result.EventsInserted, "direct root events inserted");
            AssertEqual(0, result.EventsUpdated, "direct root events updated");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "direct imported event count");
            AssertEqual("thread-direct", events[0].SessionId, "direct imported session id");
            AssertEqual("machine-direct", events[0].MachineId, "direct imported machine id");
            AssertEqual("direct", events[0].AccountLabel, "direct imported account label");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorResolvesCodexAccountFromDirectFileRoot() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var rolloutPath = Path.Combine(tempDir, "rollout-2026-03-11T14-15-00-thread-direct-auth.jsonl");
            WriteCodexRolloutFile(
                rolloutPath,
                "thread-direct-auth",
                "resp-direct-auth",
                includeAuth: true,
                authRoot: tempDir);

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("chatgpt-codex", UsageSourceKind.RecoveredFolder, rolloutPath, accountHint: "direct-auth");
            var result = coordinator.ImportRootAsync(root.Id, new UsageImportContext { MachineId = "machine-direct-auth" })
                .GetAwaiter().GetResult();

            AssertEqual(true, result.Imported, "direct auth root imported");
            AssertEqual(1, result.EventsInserted, "direct auth root events inserted");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "direct auth imported event count");
            AssertEqual("acct_codex_import", events[0].ProviderAccountId, "direct auth imported account id");
            AssertEqual("direct-auth", events[0].AccountLabel, "direct auth imported account label preserves manual hint");
            AssertEqual("thread-direct-auth", events[0].SessionId, "direct auth imported session id");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorDiscoversCodexRootFromEnvironment() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try {
            var codexHome = Path.Combine(tempDir, ".codex");
            var sessionsDir = Path.Combine(codexHome, "sessions", "2026", "03", "11");
            Directory.CreateDirectory(sessionsDir);
            Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);

            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T15-00-00-thread-env.jsonl"),
                "thread-env",
                "resp-env",
                includeAuth: true,
                authRoot: codexHome);

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }),
                new IUsageTelemetryRootDiscovery[] {
                    new CodexDefaultSourceRootDiscovery(
                        new UsageTelemetryExternalProfileDiscovery(() => Array.Empty<UsageTelemetryExternalProfile>()))
                });

            var discovered = coordinator.DiscoverRootsAsync("codex").GetAwaiter().GetResult();
            AssertEqual(1, discovered.Count, "discovered codex roots");
            AssertEqual(codexHome, discovered[0].Path, "discovered codex root path");

            var imported = coordinator.ImportAllAsync(new UsageImportContext { MachineId = "machine-env" }, "codex")
                .GetAwaiter().GetResult();
            AssertEqual(1, imported.RootsConsidered, "batch roots considered");
            AssertEqual(1, imported.RootsImported, "batch roots imported");
            AssertEqual(1, imported.EventsRead, "batch events read");
            AssertEqual(1, imported.EventsInserted, "batch events inserted");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "env imported event count");
            AssertEqual("acct_codex_import", events[0].ProviderAccountId, "env imported account id");
            AssertEqual("codex@example.com", events[0].AccountLabel, "env imported account label");
            AssertEqual("machine-env", events[0].MachineId, "env imported machine id");
        } finally {
            Environment.SetEnvironmentVariable("CODEX_HOME", originalCodexHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestCodexDefaultSourceRootDiscoveryIncludesRecoveredAndWslProfiles() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        var originalCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try {
            var currentRoot = Path.Combine(tempDir, ".codex");
            var recoveredProfile = Path.Combine(tempDir, "Windows.old", "Users", "backup-user");
            var wslProfile = Path.Combine(tempDir, "wsl", "Ubuntu", "home", "dev");
            var recoveredRoot = Path.Combine(recoveredProfile, ".codex");
            var wslRoot = Path.Combine(wslProfile, ".codex");

            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(recoveredRoot);
            Directory.CreateDirectory(wslRoot);
            Environment.SetEnvironmentVariable("CODEX_HOME", currentRoot);

            var discovery = new CodexDefaultSourceRootDiscovery(
                new UsageTelemetryExternalProfileDiscovery(() => new[] {
                    new UsageTelemetryExternalProfile(UsageSourceKind.RecoveredFolder, recoveredProfile, "windows-old"),
                    new UsageTelemetryExternalProfile(UsageSourceKind.LocalLogs, wslProfile, "wsl", "Ubuntu")
                }));

            var roots = discovery.DiscoverRoots();

            AssertEqual(3, roots.Count, "codex supplemental discovered root count");
            AssertEqual(true, roots.Any(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(currentRoot), StringComparison.OrdinalIgnoreCase) && root.SourceKind == UsageSourceKind.LocalLogs), "codex current root discovered");
            AssertEqual(true, roots.Any(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(recoveredRoot), StringComparison.OrdinalIgnoreCase) && root.SourceKind == UsageSourceKind.RecoveredFolder), "codex recovered root discovered");

            var wslDiscovered = roots.Single(root => string.Equals(root.Path, UsageTelemetryIdentity.NormalizePath(wslRoot), StringComparison.OrdinalIgnoreCase));
            AssertEqual(UsageSourceKind.LocalLogs, wslDiscovered.SourceKind, "codex wsl source kind");
            AssertEqual("wsl", wslDiscovered.PlatformHint, "codex wsl platform hint");
            AssertEqual("Ubuntu", wslDiscovered.MachineLabel, "codex wsl machine label");
        } finally {
            Environment.SetEnvironmentVariable("CODEX_HOME", originalCodexHome);
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorSkipsUnchangedArtifactsWhenRawArtifactCacheIsPresent() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T18-00-00-thread-cache.jsonl"),
                "thread-cache",
                "resp-cache",
                includeAuth: false,
                authRoot: tempDir);

            var rootStore = new InMemorySourceRootStore();
            var rawArtifactStore = new InMemoryRawArtifactStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("codex", UsageSourceKind.RecoveredFolder, sessionsDir, accountHint: "backup");
            var first = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore
                    })
                .GetAwaiter().GetResult();
            var second = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore
                    })
                .GetAwaiter().GetResult();

            AssertEqual(1, first.EventsRead, "first cached import reads one event");
            AssertEqual(1, first.EventsInserted, "first cached import inserts one event");
            AssertEqual(0, second.EventsRead, "second cached import skips unchanged artifacts");
            AssertEqual(0, second.EventsInserted, "second cached import inserts nothing");
            AssertEqual(0, second.EventsUpdated, "second cached import updates nothing");
            AssertEqual(1, rawArtifactStore.GetAll().Count, "raw artifact cache tracks one file");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorForceReimportBypassesArtifactCache() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T18-30-00-thread-force.jsonl"),
                "thread-force",
                "resp-force",
                includeAuth: false,
                authRoot: tempDir);

            var rootStore = new InMemorySourceRootStore();
            var rawArtifactStore = new InMemoryRawArtifactStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("codex", UsageSourceKind.RecoveredFolder, sessionsDir, accountHint: "backup");
            _ = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore
                    })
                .GetAwaiter().GetResult();

            var forced = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore,
                        ForceReimport = true
                    })
                .GetAwaiter().GetResult();

            AssertEqual(1, forced.EventsRead, "forced reimport reparses unchanged artifact");
            AssertEqual(0, forced.EventsInserted, "forced reimport does not duplicate canonical event");
            AssertEqual(0, forced.EventsUpdated, "forced reimport leaves canonical event unchanged");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorCanResumeAcrossArtifactBudgetedRuns() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T18-40-00-thread-budget-a.jsonl"),
                "thread-budget-a",
                "resp-budget-a",
                includeAuth: false,
                authRoot: tempDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T18-41-00-thread-budget-b.jsonl"),
                "thread-budget-b",
                "resp-budget-b",
                includeAuth: false,
                authRoot: tempDir);

            var rootStore = new InMemorySourceRootStore();
            var rawArtifactStore = new InMemoryRawArtifactStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("codex", UsageSourceKind.RecoveredFolder, sessionsDir);
            var first = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore,
                        MaxArtifacts = 1
                    })
                .GetAwaiter().GetResult();
            var second = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore,
                        MaxArtifacts = 1
                    })
                .GetAwaiter().GetResult();

            AssertEqual(true, first.ArtifactBudgetReached, "first budgeted import reaches artifact budget");
            AssertEqual(1, first.ArtifactsProcessed, "first budgeted import processes one artifact");
            AssertEqual(1, first.EventsRead, "first budgeted import reads one event");
            AssertEqual(true, second.Imported, "second budgeted import resumes");
            AssertEqual(1, second.ArtifactsProcessed, "second budgeted import processes one artifact");
            AssertEqual(1, second.EventsRead, "second budgeted import reads next event");
            AssertEqual(2, eventStore.GetAll().Count, "budgeted imports produce two canonical events");
            AssertEqual(2, rawArtifactStore.GetAll().Count, "budgeted imports cache both artifacts");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorRecentFirstBudgetPrefersNewestArtifact() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);

            var olderPath = Path.Combine(sessionsDir, "rollout-2026-03-10T18-40-00-thread-older.jsonl");
            WriteCodexRolloutFile(
                olderPath,
                "thread-older",
                "resp-older",
                includeAuth: false,
                authRoot: tempDir);
            File.SetLastWriteTimeUtc(olderPath, new DateTime(2026, 03, 10, 18, 40, 00, DateTimeKind.Utc));

            var newerPath = Path.Combine(sessionsDir, "rollout-2026-03-11T18-41-00-thread-newer.jsonl");
            WriteCodexRolloutFile(
                newerPath,
                "thread-newer",
                "resp-newer",
                includeAuth: false,
                authRoot: tempDir);
            File.SetLastWriteTimeUtc(newerPath, new DateTime(2026, 03, 11, 18, 41, 00, DateTimeKind.Utc));

            var rootStore = new InMemorySourceRootStore();
            var rawArtifactStore = new InMemoryRawArtifactStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new CodexUsageTelemetryProviderDescriptor()
                }));

            var root = coordinator.RegisterRoot("codex", UsageSourceKind.RecoveredFolder, sessionsDir);
            var result = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore,
                        PreferRecentArtifacts = true,
                        MaxArtifacts = 1
                    })
                .GetAwaiter().GetResult();

            AssertEqual(1, result.EventsInserted, "recent-first budget inserts one event");
            var inserted = eventStore.GetAll();
            AssertEqual(1, inserted.Count, "recent-first budget event count");
            AssertEqual("thread-newer", inserted[0].SessionId, "recent-first budget picks newest artifact first");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorDoesNotCommitArtifactCacheWhenAdapterFails() {
        var rootStore = new InMemorySourceRootStore();
        var rawArtifactStore = new InMemoryRawArtifactStore();
        var eventStore = new InMemoryUsageEventStore();
        var coordinator = new UsageTelemetryImportCoordinator(
            rootStore,
            eventStore,
            new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                new ThrowingUsageTelemetryProviderDescriptor()
            }));

        var root = coordinator.RegisterRoot("throwing", UsageSourceKind.RecoveredFolder, "C:\\temp\\throwing");

        try {
            _ = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        RawArtifactStore = rawArtifactStore
                    })
                .GetAwaiter().GetResult();
            throw new Exception("Expected import failure was not thrown.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "simulated adapter failure", "failing adapter exception");
        }

        AssertEqual(0, rawArtifactStore.GetAll().Count, "failed import does not commit raw artifact cache");
        AssertEqual(0, eventStore.GetAll().Count, "failed import does not write events");
    }

    private static string CreateUsageTelemetryImportTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-usage-telemetry-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteUsageTelemetryImportTempDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }

    private static void WriteCodexRolloutFile(string rolloutPath, string threadId, string responseId, bool includeAuth, string authRoot) {
        var directory = Path.GetDirectoryName(rolloutPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        if (includeAuth) {
            File.WriteAllText(
                Path.Combine(authRoot, "auth.json"),
                "{\"tokens\":{\"account_id\":\"acct_codex_import\",\"access_token\":\"header."
                + EncodeJwtPayload(new JsonObject()
                    .Add("https://api.openai.com/auth", new JsonObject()
                        .Add("chatgpt_account_id", "acct_codex_import")
                        .Add("chatgpt_plan_type", "pro")))
                + ".sig\",\"id_token\":\"header."
                + EncodeJwtPayload(new JsonObject()
                    .Add("email", "codex@example.com")
                    .Add("https://api.openai.com/auth", new JsonObject()
                        .Add("chatgpt_account_id", "acct_codex_import")))
                + ".sig\"}}");
        }

        File.WriteAllText(
            rolloutPath,
            string.Join(
                Environment.NewLine,
                SerializeUsageTelemetryJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T15:00:00Z")
                    .Add("type", "session_meta")
                    .Add("payload", new JsonObject()
                        .Add("meta", new JsonObject()
                            .Add("id", threadId)))),
                SerializeUsageTelemetryJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T15:00:01Z")
                    .Add("type", "turn_context")
                    .Add("payload", new JsonObject()
                        .Add("model", "gpt-5.4-codex"))),
                SerializeUsageTelemetryJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T15:00:02Z")
                    .Add("type", "event_msg")
                    .Add("payload", new JsonObject()
                        .Add("type", "token_count")
                        .Add("turn_id", "turn-" + threadId)
                        .Add("response_id", responseId)
                        .Add("info", new JsonObject()
                            .Add("last_token_usage", new JsonObject()
                                .Add("input_tokens", 100L)
                                .Add("cached_input_tokens", 25L)
                                .Add("output_tokens", 40L)
                                .Add("reasoning_output_tokens", 5L)
                                .Add("total_tokens", 140L)))))) + Environment.NewLine);
    }

    private static string SerializeUsageTelemetryJsonLine(JsonObject obj) {
        return JsonLite.Serialize(JsonValue.From(obj));
    }

    private static string EncodeJwtPayload(JsonObject payload) {
        var json = JsonLite.Serialize(JsonValue.From(payload));
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class ThrowingUsageTelemetryProviderDescriptor : IUsageTelemetryProviderDescriptor {
        public string ProviderId => "throwing";

        public IReadOnlyList<IUsageTelemetryAdapter> CreateAdapters() {
            return new IUsageTelemetryAdapter[] {
                new ThrowingUsageTelemetryAdapter()
            };
        }
    }

    private sealed class ThrowingUsageTelemetryAdapter : IUsageTelemetryAdapter {
        public string AdapterId => "throwing.adapter";

        public bool CanImport(SourceRootRecord root) {
            return string.Equals(root.ProviderId, "throwing", StringComparison.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyList<UsageEventRecord>> ImportAsync(
            SourceRootRecord root,
            UsageImportContext context,
            CancellationToken cancellationToken = default(CancellationToken)) {
            context.RawArtifactStore?.Upsert(RawArtifactDescriptor.CreateFile(
                root.Id,
                AdapterId,
                "C:\\temp\\throwing\\artifact.jsonl",
                parserVersion: "throwing/v1",
                importedAtUtc: context.UtcNow()));
            throw new InvalidOperationException("simulated adapter failure");
        }
    }
}
