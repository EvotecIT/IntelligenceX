using System;
using System.IO;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Claude;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestClaudeSessionUsageAdapterDeduplicatesStreamingChunks() {
        var tempDir = CreateClaudeUsageTelemetryTempDirectory();
        try {
            var projectsDir = Path.Combine(tempDir, ".claude", "projects", "workspace-a");
            Directory.CreateDirectory(projectsDir);

            var sessionPath = Path.Combine(projectsDir, "session-a.jsonl");
            File.WriteAllText(
                sessionPath,
                string.Join(
                    Environment.NewLine,
                    SerializeClaudeUsageJsonLine(new JsonObject()
                        .Add("type", "assistant")
                        .Add("sessionId", "claude-session-1")
                        .Add("requestId", "req-1")
                        .Add("timestamp", "2026-03-11T10:00:00Z")
                        .Add("message", new JsonObject()
                            .Add("id", "msg-1")
                            .Add("model", "claude-sonnet-4-5")
                            .Add("usage", new JsonObject()
                                .Add("input_tokens", 100L)
                                .Add("cache_creation_input_tokens", 20L)
                                .Add("cache_read_input_tokens", 30L)
                                .Add("output_tokens", 40L)))),
                    SerializeClaudeUsageJsonLine(new JsonObject()
                        .Add("type", "assistant")
                        .Add("sessionId", "claude-session-1")
                        .Add("requestId", "req-1")
                        .Add("timestamp", "2026-03-11T10:00:01Z")
                        .Add("message", new JsonObject()
                            .Add("id", "msg-1")
                            .Add("model", "claude-sonnet-4-5")
                            .Add("usage", new JsonObject()
                                .Add("input_tokens", 120L)
                                .Add("cache_creation_input_tokens", 25L)
                                .Add("cache_read_input_tokens", 35L)
                                .Add("output_tokens", 50L)))),
                    SerializeClaudeUsageJsonLine(new JsonObject()
                        .Add("type", "assistant")
                        .Add("sessionId", "claude-session-1")
                        .Add("requestId", "req-2")
                        .Add("timestamp", "2026-03-11T10:00:05Z")
                        .Add("message", new JsonObject()
                            .Add("id", "msg-2")
                            .Add("model", "claude-sonnet-4-5")
                            .Add("usage", new JsonObject()
                                .Add("input_tokens", 10L)
                                .Add("cache_read_input_tokens", 5L)
                                .Add("output_tokens", 7L))))) + Environment.NewLine);

            var adapter = new ClaudeSessionUsageAdapter();
            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId("claude", UsageSourceKind.LocalLogs, Path.Combine(tempDir, ".claude")),
                "claude",
                UsageSourceKind.LocalLogs,
                Path.Combine(tempDir, ".claude"));

            var imported = adapter.ImportAsync(root, new UsageImportContext()).GetAwaiter().GetResult();
            AssertEqual(2, imported.Count, "claude imported event count");

            var first = imported[0];
            AssertEqual("claude-session-1", first.SessionId, "claude first session id");
            AssertEqual("msg-1", first.TurnId, "claude first turn id");
            AssertEqual("req-1", first.ResponseId, "claude first response id");
            AssertEqual(145L, first.InputTokens, "claude first input tokens");
            AssertEqual(35L, first.CachedInputTokens, "claude first cached input tokens");
            AssertEqual(50L, first.OutputTokens, "claude first output tokens");
            AssertEqual(230L, first.TotalTokens, "claude first total tokens");
            AssertEqual("claude-sonnet-4-5", first.Model, "claude first model");

            var second = imported[1];
            AssertEqual("msg-2", second.TurnId, "claude second turn id");
            AssertEqual("req-2", second.ResponseId, "claude second response id");
            AssertEqual(10L, second.InputTokens, "claude second input tokens");
            AssertEqual(5L, second.CachedInputTokens, "claude second cached input tokens");
            AssertEqual(7L, second.OutputTokens, "claude second output tokens");
            AssertEqual(22L, second.TotalTokens, "claude second total tokens");
        } finally {
            TryDeleteClaudeUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestClaudeDefaultSourceRootDiscoveryUsesEnvironmentProjectsRoot() {
        var tempDir = CreateClaudeUsageTelemetryTempDirectory();
        var originalClaudeConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        try {
            var configRoot = Path.Combine(tempDir, "claude-config");
            var projectsRoot = Path.Combine(configRoot, "projects");
            Directory.CreateDirectory(projectsRoot);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

            var discovery = new ClaudeDefaultSourceRootDiscovery();
            var roots = discovery.DiscoverRoots();

            AssertEqual(1, roots.Count, "claude discovered root count");
            AssertEqual("claude", roots[0].ProviderId, "claude discovered provider");
            AssertEqual(UsageTelemetryIdentity.NormalizePath(projectsRoot), roots[0].Path, "claude discovered path");
        } finally {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalClaudeConfigDir);
            TryDeleteClaudeUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetryImportCoordinatorDiscoversClaudeRootFromEnvironment() {
        var tempDir = CreateClaudeUsageTelemetryTempDirectory();
        var originalClaudeConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        try {
            var configRoot = Path.Combine(tempDir, "claude-config");
            var projectsRoot = Path.Combine(configRoot, "projects", "workspace-b");
            Directory.CreateDirectory(projectsRoot);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

            File.WriteAllText(
                Path.Combine(projectsRoot, "session-b.jsonl"),
                SerializeClaudeUsageJsonLine(new JsonObject()
                    .Add("type", "assistant")
                    .Add("sessionId", "claude-session-2")
                    .Add("requestId", "req-claude-import")
                    .Add("timestamp", "2026-03-11T11:00:00Z")
                    .Add("message", new JsonObject()
                        .Add("id", "msg-claude-import")
                        .Add("model", "claude-haiku-4-5")
                        .Add("usage", new JsonObject()
                            .Add("input_tokens", 7L)
                            .Add("cache_creation_input_tokens", 3L)
                            .Add("cache_read_input_tokens", 2L)
                            .Add("output_tokens", 11L)))) + Environment.NewLine);

            var rootStore = new InMemorySourceRootStore();
            var eventStore = new InMemoryUsageEventStore();
            var coordinator = new UsageTelemetryImportCoordinator(
                rootStore,
                eventStore,
                new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                    new ClaudeUsageTelemetryProviderDescriptor()
                }),
                new IUsageTelemetryRootDiscovery[] {
                    new ClaudeDefaultSourceRootDiscovery()
                });

            var discovered = coordinator.DiscoverRootsAsync("claude").GetAwaiter().GetResult();
            AssertEqual(1, discovered.Count, "discovered claude roots");
            AssertEqual(UsageTelemetryIdentity.NormalizePath(Path.Combine(configRoot, "projects")), discovered[0].Path, "discovered claude root path");

            var imported = coordinator.ImportAllAsync(new UsageImportContext { MachineId = "machine-claude" }, "claude")
                .GetAwaiter().GetResult();
            AssertEqual(1, imported.RootsConsidered, "claude batch roots considered");
            AssertEqual(1, imported.RootsImported, "claude batch roots imported");
            AssertEqual(1, imported.EventsRead, "claude batch events read");
            AssertEqual(1, imported.EventsInserted, "claude batch events inserted");

            var events = eventStore.GetAll();
            AssertEqual(1, events.Count, "claude imported event count");
            AssertEqual("claude-session-2", events[0].SessionId, "claude imported session id");
            AssertEqual("msg-claude-import", events[0].TurnId, "claude imported turn id");
            AssertEqual("req-claude-import", events[0].ResponseId, "claude imported response id");
            AssertEqual("machine-claude", events[0].MachineId, "claude imported machine id");
            AssertEqual(23L, events[0].TotalTokens, "claude imported total tokens");
        } finally {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalClaudeConfigDir);
            TryDeleteClaudeUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static string CreateClaudeUsageTelemetryTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-usage-telemetry-claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteClaudeUsageTelemetryTempDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }

    private static string SerializeClaudeUsageJsonLine(JsonObject obj) {
        return JsonLite.Serialize(JsonValue.From(obj));
    }
}
