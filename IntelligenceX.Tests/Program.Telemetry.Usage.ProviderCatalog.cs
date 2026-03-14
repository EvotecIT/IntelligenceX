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

    private static void TestUsageTelemetryProviderRegistrySupportsAliasLookups() {
        var registry = new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
            new CodexUsageTelemetryProviderDescriptor()
        });

        var aliasAdapters = registry.GetAdapters("chatgpt-codex");
        AssertEqual(1, aliasAdapters.Count, "provider registry alias adapter count");
        AssertEqual(CodexSessionUsageAdapter.StableAdapterId, aliasAdapters[0].AdapterId, "provider registry alias adapter id");
    }
}
