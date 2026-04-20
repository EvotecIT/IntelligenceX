using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Json;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetrySqliteStoresPersistAcrossReopen() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var dbPath = Path.Combine(tempDir, "usage.db");
            using (var rootStore = new SqliteSourceRootStore(dbPath))
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                rootStore.Upsert(new SourceRootRecord("src_a", "codex", UsageSourceKind.LocalLogs, Path.Combine(tempDir, "sessions")));

                var first = new UsageEventRecord(
                    eventId: "ev_sqlite_a",
                    providerId: "codex",
                    adapterId: "codex.session-log",
                    sourceRootId: "src_a",
                    timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero)) {
                    ResponseId = "resp_sqlite_shared",
                    InputTokens = 120,
                    TruthLevel = UsageTruthLevel.Exact,
                };
                var second = new UsageEventRecord(
                    eventId: "ev_sqlite_b",
                    providerId: "codex",
                    adapterId: "codex.session-log",
                    sourceRootId: "src_a",
                    timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 1, TimeSpan.Zero)) {
                    ResponseId = "resp_sqlite_shared",
                    OutputTokens = 45,
                    TotalTokens = 165,
                    TruthLevel = UsageTruthLevel.Inferred,
                };

                var firstUpsert = eventStore.Upsert(first);
                var secondUpsert = eventStore.Upsert(second);

                AssertEqual(true, firstUpsert.Inserted, "sqlite first insert");
                AssertEqual(false, firstUpsert.Updated, "sqlite first update");
                AssertEqual(false, secondUpsert.Inserted, "sqlite second insert");
                AssertEqual(true, secondUpsert.Updated, "sqlite second merge");
                AssertEqual("ev_sqlite_a", secondUpsert.CanonicalEventId, "sqlite canonical event id");
            }

            using (var reopenedRootStore = new SqliteSourceRootStore(dbPath))
            using (var reopenedEventStore = new SqliteUsageEventStore(dbPath)) {
                AssertEqual(true, reopenedRootStore.TryGet("src_a", out var root), "sqlite root persisted across reopen");
                AssertEqual("codex", root.ProviderId, "sqlite reopened root provider");

                var events = reopenedEventStore.GetAll();
                AssertEqual(1, events.Count, "sqlite merged event count");
                AssertEqual(120L, events[0].InputTokens, "sqlite merged input tokens");
                AssertEqual(45L, events[0].OutputTokens, "sqlite merged output tokens");
                AssertEqual(165L, events[0].TotalTokens, "sqlite merged total tokens");
                AssertEqual(UsageTruthLevel.Exact, events[0].TruthLevel, "sqlite strongest truth level wins");
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestUsageTelemetrySqliteRawArtifactStorePersistsAcrossReopen() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var dbPath = Path.Combine(tempDir, "usage.db");
            using (var store = new SqliteRawArtifactStore(dbPath)) {
                store.Upsert(new RawArtifactDescriptor(
                    "src_a",
                    CodexSessionUsageAdapter.StableAdapterId,
                    Path.Combine(tempDir, "sessions", "rollout-a.jsonl"),
                    "fingerprint-a") {
                    ParserVersion = "codex.session-log/v1",
                    SizeBytes = 1234,
                    LastWriteTimeUtc = new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
                    ImportedAtUtc = new DateTimeOffset(2026, 3, 11, 12, 1, 0, TimeSpan.Zero)
                });
            }

            using (var reopened = new SqliteRawArtifactStore(dbPath)) {
                AssertEqual(
                    true,
                    reopened.TryGet(
                        "src_a",
                        CodexSessionUsageAdapter.StableAdapterId,
                        Path.Combine(tempDir, "sessions", "rollout-a.jsonl"),
                        out var artifact),
                    "sqlite raw artifact persisted");
                AssertEqual("fingerprint-a", artifact.Fingerprint, "sqlite raw artifact fingerprint");
                AssertEqual("codex.session-log/v1", artifact.ParserVersion, "sqlite raw artifact parser version");
                AssertEqual(1234L, artifact.SizeBytes, "sqlite raw artifact size");
                AssertEqual(
                    new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
                    artifact.LastWriteTimeUtc,
                    "sqlite raw artifact last write");
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestCodexSessionUsageAdapterParsesLastTokenUsageEvent() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var codexHome = Path.Combine(tempDir, ".codex");
            var sessionsDir = Path.Combine(codexHome, "sessions", "2026", "03", "11");
            Directory.CreateDirectory(sessionsDir);
            var rolloutPath = Path.Combine(sessionsDir, "rollout-2026-03-11T12-00-00-thread-123.jsonl");
            File.WriteAllText(rolloutPath, string.Join(
                Environment.NewLine,
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:00:00Z")
                    .Add("type", "session_meta")
                    .Add("payload", new JsonObject()
                        .Add("meta", new JsonObject()
                            .Add("id", "thread-123")
                            .Add("model_provider", "gpt-5.4-codex")))),
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:00:01Z")
                    .Add("type", "turn_context")
                    .Add("payload", new JsonObject()
                        .Add("model", "gpt-5.4-codex"))),
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:00:01.500Z")
                    .Add("type", "event_msg")
                    .Add("payload", new JsonObject()
                        .Add("type", "context_compacted"))),
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:00:02Z")
                    .Add("type", "event_msg")
                    .Add("payload", new JsonObject()
                        .Add("type", "token_count")
                        .Add("turn_id", "turn-1")
                        .Add("response_id", "resp-1")
                        .Add("info", new JsonObject()
                            .Add("last_token_usage", new JsonObject()
                                .Add("input_tokens", 100L)
                                .Add("cached_input_tokens", 25L)
                                .Add("output_tokens", 40L)
                                .Add("reasoning_output_tokens", 5L)
                                .Add("total_tokens", 140L)))))) + Environment.NewLine);

            var adapter = new CodexSessionUsageAdapter();
            var root = new SourceRootRecord("src_codex_home", "codex", UsageSourceKind.LocalLogs, codexHome);
            var records = adapter.ImportAsync(root, new UsageImportContext { MachineId = "machine-a" }).GetAwaiter().GetResult();

            AssertEqual(1, records.Count, "codex last-usage record count");
            AssertEqual("codex", records[0].ProviderId, "codex provider id");
            AssertEqual("thread-123", records[0].SessionId, "codex session id");
            AssertEqual("turn-1", records[0].TurnId, "codex turn id");
            AssertEqual("resp-1", records[0].ResponseId, "codex response id");
            AssertEqual("gpt-5.4-codex", records[0].Model, "codex model");
            AssertEqual("cli", records[0].Surface, "codex surface");
            AssertEqual(100L, records[0].InputTokens, "codex input tokens");
            AssertEqual(25L, records[0].CachedInputTokens, "codex cached input tokens");
            AssertEqual(40L, records[0].OutputTokens, "codex output tokens");
            AssertEqual(5L, records[0].ReasoningTokens, "codex reasoning tokens");
            AssertEqual(140L, records[0].TotalTokens, "codex total tokens");
            AssertEqual(1, records[0].CompactCount, "codex compact count");
            AssertEqual(UsageTruthLevel.Exact, records[0].TruthLevel, "codex truth level");
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestCodexSessionUsageAdapterFallsBackToTotalUsageDeltaWhenLastUsageMissing() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            var rolloutPath = Path.Combine(sessionsDir, "rollout-2026-03-11T12-30-00-thread-789.jsonl");
            File.WriteAllText(rolloutPath, string.Join(
                Environment.NewLine,
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:30:00Z")
                    .Add("type", "session_meta")
                    .Add("payload", new JsonObject()
                        .Add("meta", new JsonObject()
                            .Add("id", "thread-789")))),
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:30:01Z")
                    .Add("type", "event_msg")
                    .Add("payload", new JsonObject()
                        .Add("type", "token_count")
                        .Add("turn_id", "turn-a")
                        .Add("info", new JsonObject()
                            .Add("total_token_usage", new JsonObject()
                                .Add("input_tokens", 40L)
                                .Add("output_tokens", 10L)
                                .Add("total_tokens", 50L))))),
                SerializeJsonLine(new JsonObject()
                    .Add("timestamp", "2026-03-11T12:30:02Z")
                    .Add("type", "event_msg")
                    .Add("payload", new JsonObject()
                        .Add("type", "token_count")
                        .Add("turn_id", "turn-b")
                        .Add("info", new JsonObject()
                            .Add("total_token_usage", new JsonObject()
                                .Add("input_tokens", 65L)
                                .Add("output_tokens", 15L)
                                .Add("total_tokens", 80L)))))) + Environment.NewLine);

            var adapter = new CodexSessionUsageAdapter();
            var root = new SourceRootRecord("src_sessions", "openai-codex", UsageSourceKind.RecoveredFolder, sessionsDir);
            var records = adapter.ImportAsync(root, new UsageImportContext()).GetAwaiter().GetResult();

            AssertEqual(2, records.Count, "codex delta record count");
            AssertEqual(50L, records[0].TotalTokens, "codex first total fallback");
            AssertEqual(25L, records[1].InputTokens, "codex second input delta");
            AssertEqual(5L, records[1].OutputTokens, "codex second output delta");
            AssertEqual(30L, records[1].TotalTokens, "codex second total delta");
            AssertEqual("turn-b", records[1].TurnId, "codex second turn id");
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestCodexSessionUsageAdapterSkipsLockedFiles() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            var lockedPath = Path.Combine(sessionsDir, "rollout-2026-03-11T13-00-00-thread-locked.jsonl");
            var readablePath = Path.Combine(sessionsDir, "rollout-2026-03-11T13-05-00-thread-readable.jsonl");

            WriteCodexRolloutFile(lockedPath, "thread-locked", "resp-locked", includeAuth: false, authRoot: tempDir);
            WriteCodexRolloutFile(readablePath, "thread-readable", "resp-readable", includeAuth: false, authRoot: tempDir);

            using var lockHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var adapter = new CodexSessionUsageAdapter();
            var root = new SourceRootRecord("src_locked_sessions", "codex", UsageSourceKind.LocalLogs, sessionsDir);
            var records = adapter.ImportAsync(root, new UsageImportContext()).GetAwaiter().GetResult();

            AssertEqual(1, records.Count, "codex locked-file import count");
            AssertEqual("thread-readable", records[0].SessionId, "codex locked-file import readable session wins");
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestCodexSessionUsageAdapterDoesNotDuplicateSessionsRootArtifacts() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var sessionsDir = Path.Combine(tempDir, "sessions");
            var partitionDir = Path.Combine(sessionsDir, "2026", "03", "11");
            Directory.CreateDirectory(partitionDir);

            var rolloutPath = Path.Combine(partitionDir, "rollout-2026-03-11T13-10-00-thread-dedupe.jsonl");
            WriteCodexRolloutFile(rolloutPath, "thread-dedupe", "resp-dedupe", includeAuth: false, authRoot: tempDir);

            var adapter = new CodexSessionUsageAdapter();
            var root = new SourceRootRecord("src_sessions_dedupe", "chatgpt-codex", UsageSourceKind.RecoveredFolder, sessionsDir);
            var records = adapter.ImportAsync(root, new UsageImportContext()).GetAwaiter().GetResult();

            AssertEqual(1, records.Count, "codex sessions-root dedupe record count");
            AssertEqual("thread-dedupe", records[0].SessionId, "codex sessions-root dedupe session id");
            AssertEqual("resp-dedupe", records[0].ResponseId, "codex sessions-root dedupe response id");
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestCodexSessionUsageAdapterDoesNotDuplicateArchivedSessionCopies() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var codexHome = Path.Combine(tempDir, ".codex");
            var sessionsDir = Path.Combine(codexHome, "sessions", "2026", "03", "11");
            var archivedDir = Path.Combine(codexHome, "archived_sessions", "2026", "03", "11");
            Directory.CreateDirectory(sessionsDir);
            Directory.CreateDirectory(archivedDir);

            WriteCodexRolloutFile(
                Path.Combine(sessionsDir, "rollout-2026-03-11T13-10-00-thread-archive.jsonl"),
                "thread-archive",
                "resp-archive",
                includeAuth: false,
                authRoot: codexHome);
            WriteCodexRolloutFile(
                Path.Combine(archivedDir, "rollout-2026-03-11T13-10-00-thread-archive-copy.jsonl"),
                "thread-archive",
                "resp-archive",
                includeAuth: false,
                authRoot: codexHome);

            var adapter = new CodexSessionUsageAdapter();
            var root = new SourceRootRecord("src_codex_archive_dedupe", "codex", UsageSourceKind.LocalLogs, codexHome);
            var records = adapter.ImportAsync(root, new UsageImportContext()).GetAwaiter().GetResult();

            AssertEqual(1, records.Count, "codex archive dedupe record count");
            AssertEqual(140L, records[0].TotalTokens, "codex archive dedupe total tokens");
            AssertEqual("thread-archive", records[0].SessionId, "codex archive dedupe session id");
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static string CreateUsageTelemetryTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-usage-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteUsageTelemetryTempDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }

    private static string SerializeJsonLine(JsonObject obj) {
        return JsonLite.Serialize(JsonValue.From(obj));
    }
}
