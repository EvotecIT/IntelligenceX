using System.IO;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Codex;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryProviderCatalogResolvesAliasesAndSortOrder() {
        AssertEqual(true, UsageTelemetryProviderCatalog.IsProvider("chatgpt-codex", "codex"), "provider catalog codex alias");
        AssertEqual(true, UsageTelemetryProviderCatalog.IsProvider("claude-code", "claude"), "provider catalog claude alias");
        AssertEqual(true, UsageTelemetryProviderCatalog.IsProvider("copilot-cli", "copilot"), "provider catalog copilot alias");
        AssertEqual("codex", UsageTelemetryProviderCatalog.ResolveCanonicalProviderId("chatgpt-codex"), "provider catalog codex canonical id");
        AssertEqual("claude", UsageTelemetryProviderCatalog.ResolveCanonicalProviderId("claude-code"), "provider catalog claude canonical id");
        AssertEqual("copilot", UsageTelemetryProviderCatalog.ResolveCanonicalProviderId("github-copilot"), "provider catalog copilot canonical id");
        AssertEqual("LM Studio", UsageTelemetryProviderCatalog.ResolveDisplayTitle("lmstudio"), "provider catalog LM Studio display title");
        AssertEqual("GitHub Copilot", UsageTelemetryProviderCatalog.ResolveDisplayTitle("copilot"), "provider catalog copilot display title");
        AssertEqual("Claude Code", UsageTelemetryProviderCatalog.ResolveSectionTitle("claude"), "provider catalog claude section title");
        AssertEqual(5, UsageTelemetryProviderCatalog.ResolveSortOrder("copilot"), "provider catalog copilot sort order");
        AssertEqual(3, UsageTelemetryProviderCatalog.ResolveSortOrder("github"), "provider catalog github sort order");
    }

    private static void TestUsageTelemetryProviderCatalogInfersProviderFromPath() {
        AssertEqual(
            "codex",
            UsageTelemetryProviderCatalog.InferProviderIdFromPath(@"C:\Users\test\.codex\sessions"),
            "provider catalog infers codex from path");
        AssertEqual(
            "claude",
            UsageTelemetryProviderCatalog.InferProviderIdFromPath(@"C:\Users\test\.claude\projects"),
            "provider catalog infers claude from path");
        AssertEqual(
            "lmstudio",
            UsageTelemetryProviderCatalog.InferProviderIdFromPath(@"C:\Users\test\.lmstudio\conversations\session.conversation.json"),
            "provider catalog infers LM Studio from path");
        AssertEqual(
            "copilot",
            UsageTelemetryProviderCatalog.InferProviderIdFromPath(@"C:\Users\test\.copilot\session-state\session-a\events.jsonl"),
            "provider catalog infers Copilot from path");
    }

    private static void TestUsageTelemetryQuickReportScannerSupportsAliasProviderFilters() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            var rolloutPath = Path.Combine(sessionsDir, "rollout-2026-03-11T18-55-00-thread-alias.jsonl");
            WriteCodexRolloutFile(
                rolloutPath,
                "thread-alias",
                "resp-alias",
                includeAuth: false,
                authRoot: tempDir);

            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId("chatgpt-codex", UsageSourceKind.LocalLogs, sessionsDir),
                "chatgpt-codex",
                UsageSourceKind.LocalLogs,
                sessionsDir);

            var result = new UsageTelemetryQuickReportScanner().ScanAsync(
                new[] { root },
                new UsageTelemetryQuickReportOptions { ProviderId = "codex" })
                .GetAwaiter()
                .GetResult();

            AssertEqual(1, result.RootsConsidered, "quick report alias roots considered");
            AssertEqual(1, result.ArtifactsParsed, "quick report alias artifacts parsed");
            AssertEqual(1, result.Events.Count, "quick report alias events count");
            AssertEqual("codex", result.Events[0].ProviderId, "quick report alias canonical provider id");
            AssertEqual(140L, result.Events[0].TotalTokens, "quick report alias total tokens");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryQuickReportScannerDeduplicatesCodexSessionCopiesAcrossRoots() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var currentCodexHome = Path.Combine(tempDir, ".codex-current");
            var recoveredCodexHome = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex");
            var currentSessionsDir = Path.Combine(currentCodexHome, "sessions", "2026", "03", "11");
            var recoveredSessionsDir = Path.Combine(recoveredCodexHome, "sessions", "2026", "03", "11");
            Directory.CreateDirectory(currentSessionsDir);
            Directory.CreateDirectory(recoveredSessionsDir);

            WriteCodexRolloutFile(
                Path.Combine(currentSessionsDir, "rollout-2026-03-11T18-55-00-thread-dupe.jsonl"),
                "thread-dupe",
                "resp-dupe",
                includeAuth: false,
                authRoot: currentCodexHome);
            WriteCodexRolloutFile(
                Path.Combine(recoveredSessionsDir, "rollout-2026-03-11T18-55-00-thread-dupe-copy.jsonl"),
                "thread-dupe",
                "resp-dupe",
                includeAuth: false,
                authRoot: recoveredCodexHome);

            var roots = new[] {
                new SourceRootRecord(
                    SourceRootRecord.CreateStableId("codex", UsageSourceKind.LocalLogs, currentCodexHome),
                    "codex",
                    UsageSourceKind.LocalLogs,
                    currentCodexHome),
                new SourceRootRecord(
                    SourceRootRecord.CreateStableId("codex", UsageSourceKind.RecoveredFolder, recoveredCodexHome),
                    "codex",
                    UsageSourceKind.RecoveredFolder,
                    recoveredCodexHome)
            };

            var result = new UsageTelemetryQuickReportScanner().ScanAsync(
                roots,
                new UsageTelemetryQuickReportOptions { ProviderId = "codex" })
                .GetAwaiter()
                .GetResult();

            AssertEqual(2, result.RootsConsidered, "quick report codex dedupe roots considered");
            AssertEqual(2, result.ArtifactsParsed, "quick report codex dedupe artifacts parsed");
            AssertEqual(2, result.RawEventsCollected, "quick report codex dedupe raw event count");
            AssertEqual(1, result.DuplicateRecordsCollapsed, "quick report codex dedupe collapsed count");
            AssertEqual(1, result.ProviderDiagnostics.Count, "quick report codex dedupe provider diagnostics count");
            AssertEqual("codex", result.ProviderDiagnostics[0].ProviderId, "quick report codex dedupe provider id");
            AssertEqual(2, result.ProviderDiagnostics[0].RootsConsidered, "quick report codex dedupe provider roots");
            AssertEqual(2, result.ProviderDiagnostics[0].ArtifactsParsed, "quick report codex dedupe provider parsed");
            AssertEqual(2, result.ProviderDiagnostics[0].RawEventsCollected, "quick report codex dedupe provider raw");
            AssertEqual(1, result.ProviderDiagnostics[0].UniqueEventsRetained, "quick report codex dedupe provider unique");
            AssertEqual(1, result.ProviderDiagnostics[0].DuplicateRecordsCollapsed, "quick report codex dedupe provider collapsed");
            AssertEqual(1, result.Events.Count, "quick report codex dedupe events count");
            AssertEqual(140L, result.Events[0].TotalTokens, "quick report codex dedupe total tokens");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryQuickReportScannerReusesCachedCodexDuplicatesSafely() {
        var tempDir = CreateUsageTelemetryImportTempDirectory();
        try {
            var currentCodexHome = Path.Combine(tempDir, ".codex-current");
            var recoveredCodexHome = Path.Combine(tempDir, "Windows.old", "Users", "me", ".codex");
            var currentSessionsDir = Path.Combine(currentCodexHome, "sessions", "2026", "03", "11");
            var recoveredSessionsDir = Path.Combine(recoveredCodexHome, "sessions", "2026", "03", "11");
            Directory.CreateDirectory(currentSessionsDir);
            Directory.CreateDirectory(recoveredSessionsDir);

            WriteCodexRolloutFile(
                Path.Combine(currentSessionsDir, "rollout-2026-03-11T18-55-00-thread-cache-dupe.jsonl"),
                "thread-cache-dupe",
                "resp-cache-dupe",
                includeAuth: false,
                authRoot: currentCodexHome);
            WriteCodexRolloutFile(
                Path.Combine(recoveredSessionsDir, "rollout-2026-03-11T18-55-00-thread-cache-dupe-copy.jsonl"),
                "thread-cache-dupe",
                "resp-cache-dupe",
                includeAuth: false,
                authRoot: recoveredCodexHome);

            var roots = new[] {
                new SourceRootRecord(
                    SourceRootRecord.CreateStableId("codex", UsageSourceKind.LocalLogs, currentCodexHome),
                    "codex",
                    UsageSourceKind.LocalLogs,
                    currentCodexHome),
                new SourceRootRecord(
                    SourceRootRecord.CreateStableId("codex", UsageSourceKind.RecoveredFolder, recoveredCodexHome),
                    "codex",
                    UsageSourceKind.RecoveredFolder,
                    recoveredCodexHome)
            };
            var rawArtifactStore = new InMemoryRawArtifactStore();
            var scanner = new UsageTelemetryQuickReportScanner();

            var first = scanner.ScanAsync(
                    roots,
                    new UsageTelemetryQuickReportOptions {
                        ProviderId = "codex",
                        RawArtifactStore = rawArtifactStore
                    })
                .GetAwaiter()
                .GetResult();
            var second = scanner.ScanAsync(
                    roots,
                    new UsageTelemetryQuickReportOptions {
                        ProviderId = "codex",
                        RawArtifactStore = rawArtifactStore
                    })
                .GetAwaiter()
                .GetResult();

            AssertEqual(2, first.ArtifactsParsed, "quick report cached dedupe first parsed");
            AssertEqual(0, first.ArtifactsReused, "quick report cached dedupe first reused");
            AssertEqual(2, first.RawEventsCollected, "quick report cached dedupe first raw count");
            AssertEqual(1, first.DuplicateRecordsCollapsed, "quick report cached dedupe first collapsed count");
            AssertEqual(1, first.ProviderDiagnostics.Count, "quick report cached dedupe first provider diagnostics count");
            AssertEqual(2, first.ProviderDiagnostics[0].ArtifactsParsed, "quick report cached dedupe first provider parsed");
            AssertEqual(0, first.ProviderDiagnostics[0].ArtifactsReused, "quick report cached dedupe first provider reused");
            AssertEqual(2, first.ProviderDiagnostics[0].RawEventsCollected, "quick report cached dedupe first provider raw");
            AssertEqual(1, first.ProviderDiagnostics[0].UniqueEventsRetained, "quick report cached dedupe first provider unique");
            AssertEqual(1, first.ProviderDiagnostics[0].DuplicateRecordsCollapsed, "quick report cached dedupe first provider collapsed");
            AssertEqual(1, first.Events.Count, "quick report cached dedupe first event count");
            AssertEqual(140L, first.Events[0].TotalTokens, "quick report cached dedupe first total");

            AssertEqual(0, second.ArtifactsParsed, "quick report cached dedupe second parsed");
            AssertEqual(2, second.ArtifactsReused, "quick report cached dedupe second reused");
            AssertEqual(2, second.RawEventsCollected, "quick report cached dedupe second raw count");
            AssertEqual(1, second.DuplicateRecordsCollapsed, "quick report cached dedupe second collapsed count");
            AssertEqual(1, second.ProviderDiagnostics.Count, "quick report cached dedupe second provider diagnostics count");
            AssertEqual(0, second.ProviderDiagnostics[0].ArtifactsParsed, "quick report cached dedupe second provider parsed");
            AssertEqual(2, second.ProviderDiagnostics[0].ArtifactsReused, "quick report cached dedupe second provider reused");
            AssertEqual(2, second.ProviderDiagnostics[0].RawEventsCollected, "quick report cached dedupe second provider raw");
            AssertEqual(1, second.ProviderDiagnostics[0].UniqueEventsRetained, "quick report cached dedupe second provider unique");
            AssertEqual(1, second.ProviderDiagnostics[0].DuplicateRecordsCollapsed, "quick report cached dedupe second provider collapsed");
            AssertEqual(1, second.Events.Count, "quick report cached dedupe second event count");
            AssertEqual(140L, second.Events[0].TotalTokens, "quick report cached dedupe second total");
        } finally {
            TryDeleteUsageTelemetryImportTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryProviderRegistrySupportsAliasLookups() {
        var registry = new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
            new CodexUsageTelemetryProviderDescriptor()
        });

        var aliasAdapters = registry.GetAdapters("chatgpt-codex");
        AssertEqual(1, aliasAdapters.Count, "provider registry alias adapter count");
        AssertEqual(CodexSessionUsageAdapter.StableAdapterId, aliasAdapters[0].AdapterId, "provider registry alias adapter id");
    }
}
