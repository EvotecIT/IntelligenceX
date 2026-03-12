using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Codex;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageAccountBindingResolverMatchesSourceRootAndRawLabel() {
        var store = new InMemoryUsageAccountBindingStore();
        store.Upsert(new UsageAccountBindingRecord(
            UsageAccountBindingRecord.CreateStableId("codex", "src_work", matchAccountLabel: "backup"),
            "codex") {
            SourceRootId = "src_work",
            MatchAccountLabel = "backup",
            ProviderAccountId = "acct_work",
            AccountLabel = "work",
            PersonLabel = "Przemek"
        });

        var resolver = new UsageAccountBindingResolver(store);
        var resolved = resolver.Resolve(new UsageEventRecord(
            "ev_binding_1",
            "codex",
            CodexSessionUsageAdapter.StableAdapterId,
            "src_work",
            new DateTimeOffset(2026, 3, 11, 16, 0, 0, TimeSpan.Zero)) {
            AccountLabel = "backup"
        });

        AssertEqual("acct_work", resolved.ProviderAccountId, "binding resolver provider account id");
        AssertEqual("work", resolved.AccountLabel, "binding resolver account label");
        AssertEqual("Przemek", resolved.PersonLabel, "binding resolver person label");
    }

    private static void TestUsageTelemetryImportCoordinatorReimportAppliesAccountBindingOverrides() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T17-00-00-thread-rebind.jsonl"),
                "thread-rebind",
                "resp-rebind",
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

            var initialImport = coordinator.ImportRootAsync(root.Id, new UsageImportContext())
                .GetAwaiter().GetResult();
            AssertEqual(1, initialImport.EventsInserted, "initial binding-less import inserted");
            AssertEqual("backup", eventStore.GetAll()[0].AccountLabel, "initial binding-less account label");

            var bindings = new InMemoryUsageAccountBindingStore();
            bindings.Upsert(new UsageAccountBindingRecord(
                UsageAccountBindingRecord.CreateStableId("codex", root.Id),
                "codex") {
                SourceRootId = root.Id,
                AccountLabel = "work",
                PersonLabel = "Przemek"
            });

            var reboundImport = coordinator.ImportRootAsync(
                    root.Id,
                    new UsageImportContext {
                        AccountResolver = new UsageAccountBindingResolver(bindings)
                    })
                .GetAwaiter().GetResult();

            AssertEqual(1, reboundImport.EventsUpdated, "reimport with binding updates canonical event");
            AssertEqual(0, reboundImport.EventsInserted, "reimport with binding does not insert duplicate");

            var reboundEvent = eventStore.GetAll()[0];
            AssertEqual("work", reboundEvent.AccountLabel, "binding override account label");
            AssertEqual("Przemek", reboundEvent.PersonLabel, "binding override person label");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetrySqliteAccountBindingStorePersistsAcrossReopen() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var dbPath = Path.Combine(tempDir, "usage.db");
            using (var store = new SqliteUsageAccountBindingStore(dbPath)) {
                store.Upsert(new UsageAccountBindingRecord(
                    UsageAccountBindingRecord.CreateStableId("codex", "src_abc", matchAccountLabel: "backup"),
                    "codex") {
                    SourceRootId = "src_abc",
                    MatchAccountLabel = "backup",
                    ProviderAccountId = "acct_work",
                    AccountLabel = "work",
                    PersonLabel = "Przemek"
                });
            }

            using (var reopened = new SqliteUsageAccountBindingStore(dbPath)) {
                var bindings = reopened.GetAll();
                AssertEqual(1, bindings.Count, "sqlite binding count");
                AssertEqual("codex", bindings[0].ProviderId, "sqlite binding provider");
                AssertEqual("src_abc", bindings[0].SourceRootId, "sqlite binding source root");
                AssertEqual("work", bindings[0].AccountLabel, "sqlite binding account label");
                AssertEqual("Przemek", bindings[0].PersonLabel, "sqlite binding person label");
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }
}
