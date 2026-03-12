using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Codex;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryStableSourceRootIdNormalizesPaths() {
        var pathA = @"C:\Users\tester\.codex\sessions\";
        var pathB = @"C:\Users\tester\.codex\sessions";

        var idA = SourceRootRecord.CreateStableId("codex", UsageSourceKind.LocalLogs, pathA);
        var idB = SourceRootRecord.CreateStableId("codex", UsageSourceKind.LocalLogs, pathB);

        AssertEqual(idA, idB, "stable source root id");
    }

    private static void TestUsageTelemetryDedupePrefersAccountSessionTurn() {
        var record = new UsageEventRecord(
            eventId: "ev_1",
            providerId: "codex",
            adapterId: "codex.logs",
            sourceRootId: "src_1",
            timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero)) {
            ProviderAccountId = "acct_123",
            SessionId = "sess_9",
            TurnId = "turn_7",
            ResponseId = "resp_ignore",
            RawHash = "raw_ignore",
        };

        var keys = record.GetDeduplicationKeys();
        AssertEqual(3, keys.Count, "dedupe key count");
        AssertEqual("acct-session-turn|codex|acct_123|sess_9|turn_7", keys[0], "primary dedupe key");
        AssertEqual("response|codex|resp_ignore", keys[1], "secondary dedupe key");
        AssertEqual("raw|codex|raw_ignore", keys[2], "tertiary dedupe key");
    }

    private static void TestUsageTelemetryStoreMergesResponseDuplicates() {
        var store = new InMemoryUsageEventStore();
        var first = new UsageEventRecord(
            eventId: "ev_a",
            providerId: "claude",
            adapterId: "claude.logs",
            sourceRootId: "src_logs",
            timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero)) {
            ResponseId = "resp_shared",
            InputTokens = 140,
            TruthLevel = UsageTruthLevel.Exact,
        };
        var second = new UsageEventRecord(
            eventId: "ev_b",
            providerId: "claude",
            adapterId: "claude.web",
            sourceRootId: "src_web",
            timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 1, TimeSpan.Zero)) {
            ResponseId = "resp_shared",
            OutputTokens = 40,
            TotalTokens = 180,
            DurationMs = 3200,
            TruthLevel = UsageTruthLevel.Inferred,
        };

        var upsertA = store.Upsert(first);
        var upsertB = store.Upsert(second);

        AssertEqual(true, upsertA.Inserted, "first insert");
        AssertEqual(false, upsertA.Updated, "first update");
        AssertEqual(false, upsertB.Inserted, "second insert");
        AssertEqual(true, upsertB.Updated, "second merge");
        AssertEqual("ev_a", upsertB.CanonicalEventId, "canonical event id");

        var all = store.GetAll();
        AssertEqual(1, all.Count, "merged event count");
        AssertEqual(140L, all[0].InputTokens, "merged input tokens");
        AssertEqual(40L, all[0].OutputTokens, "merged output tokens");
        AssertEqual(180L, all[0].TotalTokens, "merged total tokens");
        AssertEqual(3200L, all[0].DurationMs, "merged duration");
        AssertEqual(UsageTruthLevel.Exact, all[0].TruthLevel, "truth level keeps strongest evidence");
    }

    private static void TestUsageTelemetrySourceRootStoreOrdersRoots() {
        var store = new InMemorySourceRootStore();
        store.Upsert(new SourceRootRecord("b", "claude", UsageSourceKind.LocalLogs, "/z/projects"));
        store.Upsert(new SourceRootRecord("a", "codex", UsageSourceKind.LocalLogs, "/a/sessions"));

        var roots = store.GetAll();
        AssertEqual(2, roots.Count, "source root count");
        AssertEqual("claude", roots[0].ProviderId, "first root provider");
        AssertEqual("codex", roots[1].ProviderId, "second root provider");
    }

    private static void TestUsageTelemetrySqliteStoreMergesResponseDuplicates() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-usage-telemetry-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "telemetry.db");

        using var eventStore = new SqliteUsageEventStore(dbPath);
        using var rootStore = new SqliteSourceRootStore(dbPath);

        rootStore.Upsert(new SourceRootRecord("src_1", "codex", UsageSourceKind.LocalLogs, Path.Combine(temp, ".codex", "sessions")));

        var first = new UsageEventRecord(
            eventId: "ev_a",
            providerId: "codex",
            adapterId: "codex.logs",
            sourceRootId: "src_1",
            timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero)) {
            ResponseId = "resp_shared",
            InputTokens = 240,
            TruthLevel = UsageTruthLevel.Exact,
        };
        var second = new UsageEventRecord(
            eventId: "ev_b",
            providerId: "codex",
            adapterId: "codex.logs",
            sourceRootId: "src_1",
            timestampUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 1, TimeSpan.Zero)) {
            ResponseId = "resp_shared",
            OutputTokens = 64,
            TotalTokens = 304,
            TruthLevel = UsageTruthLevel.Inferred,
        };

        var upsertA = eventStore.Upsert(first);
        var upsertB = eventStore.Upsert(second);

        AssertEqual(true, upsertA.Inserted, "sqlite first insert");
        AssertEqual(false, upsertB.Inserted, "sqlite second insert");
        AssertEqual(true, upsertB.Updated, "sqlite merge update");
        AssertEqual("ev_a", upsertB.CanonicalEventId, "sqlite canonical event id");

        var all = eventStore.GetAll();
        AssertEqual(1, all.Count, "sqlite merged event count");
        AssertEqual(240L, all[0].InputTokens, "sqlite merged input");
        AssertEqual(64L, all[0].OutputTokens, "sqlite merged output");
        AssertEqual(304L, all[0].TotalTokens, "sqlite merged total");

        AssertEqual(true, rootStore.TryGet("src_1", out var persistedRoot), "sqlite root persisted");
        AssertEqual("codex", persistedRoot.ProviderId, "sqlite root provider");
    }

    private static void TestCodexSessionUsageAdapterImportsExactUsageAndSkipsDuplicateTotals() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-codex-import-" + Guid.NewGuid().ToString("N"));
        var codexHome = Path.Combine(temp, ".codex");
        var sessionsDir = Path.Combine(codexHome, "sessions", "2026", "03", "11");
        Directory.CreateDirectory(sessionsDir);

        File.WriteAllText(
            Path.Combine(codexHome, "auth.json"),
            "{\"tokens\":{\"account_id\":\"acct_codex_primary\"}}");

        var sessionId = "019cdd6b-f079-71a2-b172-e96a48818940";
        var sessionPath = Path.Combine(sessionsDir, "rollout-2026-03-11T15-02-44-" + sessionId + ".jsonl");
        File.WriteAllText(
            sessionPath,
            string.Join(Environment.NewLine, new[] {
                "{\"timestamp\":\"2026-03-11T15:02:44.663Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"" + sessionId + "\"}}",
                "{\"timestamp\":\"2026-03-11T15:02:44.700Z\",\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-5.3-codex\"}}",
                "{\"timestamp\":\"2026-03-11T15:02:49.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":8509,\"cached_input_tokens\":7424,\"output_tokens\":282,\"reasoning_output_tokens\":173,\"total_tokens\":8791},\"last_token_usage\":{\"input_tokens\":8509,\"cached_input_tokens\":7424,\"output_tokens\":282,\"reasoning_output_tokens\":173,\"total_tokens\":8791}}}}",
                "{\"timestamp\":\"2026-03-11T15:02:50.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":8509,\"cached_input_tokens\":7424,\"output_tokens\":282,\"reasoning_output_tokens\":173,\"total_tokens\":8791},\"last_token_usage\":{\"input_tokens\":8509,\"cached_input_tokens\":7424,\"output_tokens\":282,\"reasoning_output_tokens\":173,\"total_tokens\":8791}}}}",
                "{\"timestamp\":\"2026-03-11T15:02:55.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":21126,\"cached_input_tokens\":14848,\"output_tokens\":699,\"reasoning_output_tokens\":292,\"total_tokens\":21825},\"last_token_usage\":{\"input_tokens\":12617,\"cached_input_tokens\":7424,\"output_tokens\":417,\"reasoning_output_tokens\":119,\"total_tokens\":13034}}}}"
            }));

        var adapter = new CodexSessionUsageAdapter();
        var root = new SourceRootRecord("src_codex", "codex", UsageSourceKind.LocalLogs, codexHome);
        var context = new UsageImportContext {
            MachineId = "devbox"
        };

        var result = adapter.ImportAsync(root, context).GetAwaiter().GetResult();

        AssertEqual(2, result.Count, "codex imported record count");
        AssertEqual("acct_codex_primary", result[0].ProviderAccountId, "codex account id");
        AssertEqual(sessionId, result[0].SessionId, "codex session id");
        AssertEqual("gpt-5.3-codex", result[0].Model, "codex model");
        AssertEqual("cli", result[0].Surface, "codex surface");
        AssertEqual(8509L, result[0].InputTokens, "codex first input");
        AssertEqual(7424L, result[0].CachedInputTokens, "codex first cache");
        AssertEqual(282L, result[0].OutputTokens, "codex first output");
        AssertEqual(8791L, result[0].TotalTokens, "codex first total");
        AssertEqual(12617L, result[1].InputTokens, "codex second input");
        AssertEqual(417L, result[1].OutputTokens, "codex second output");
        AssertEqual(13034L, result[1].TotalTokens, "codex second total");
        AssertEqual(UsageTruthLevel.Exact, result[1].TruthLevel, "codex truth level");
    }
}
