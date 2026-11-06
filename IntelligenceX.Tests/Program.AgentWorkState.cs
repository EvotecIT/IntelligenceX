using System.Globalization;
using System.Linq;
using System.Text.Json;
using DBAClientX;
using IntelligenceX.Codex;
using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestCodexAgentWorkMetadataReadOnly() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-agent-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var path = Path.Combine(directory, "state_5.sqlite");
            using (var sqlite = new SQLite()) {
                sqlite.ExecuteNonQuery(path, """
                    CREATE TABLE threads (id TEXT, title TEXT, cwd TEXT, git_branch TEXT, git_origin_url TEXT,
                        created_at INTEGER, updated_at INTEGER, archived INTEGER, first_user_message TEXT);
                    INSERT INTO threads VALUES ('fake', 'private sentinel', '/missing/project', 'work',
                        'https://private-sentinel@github.com/Sample/Project.git', 1793527200, 1793527260, 1, 'private sentinel');
                    """);
            }
            var before = File.ReadAllBytes(path);
            var root = new SourceRootRecord("fixture", "codex", UsageSourceKind.LocalLogs, directory);
            var sessions = CodexAgentSessionMetadataReader.ReadAsync(root).GetAwaiter().GetResult();
            AssertEqual(1, sessions.Count, "one database session");
            var session = sessions[0];
            AssertEqual("Sample/Project", session.Repository, "remote identity strips credentials");
            AssertEqual<string?>(null, session.Title, "prompt-derived database title is not exported");
            AssertEqual(AgentWorkState.Finished, session.State, "archive state");
            AssertEqual(DateTimeOffset.FromUnixTimeSeconds(1793527260), session.LastActivityAtUtc, "provider activity time");
            AssertEqual(true, before.SequenceEqual(File.ReadAllBytes(path)), "read-only database bytes");
            using (var sqlite = new SQLite()) {
                sqlite.ExecuteNonQuery(path, """
                    ALTER TABLE threads ADD COLUMN updated_at_ms INTEGER;
                    UPDATE threads SET updated_at_ms = 1793527260123;
                    """);
            }
            before = File.ReadAllBytes(path);
            sessions = CodexAgentSessionMetadataReader.ReadAsync(root).GetAwaiter().GetResult();
            AssertEqual(DateTimeOffset.FromUnixTimeMilliseconds(1793527260123), sessions[0].LastActivityAtUtc, "Desktop millisecond activity time");
            AssertEqual(true, before.SequenceEqual(File.ReadAllBytes(path)), "millisecond metadata remains read-only");
            var json = AgentWorkJson.Serialize(new AgentWorkSnapshot { Sessions = sessions });
            AssertEqual(false, json.Contains("private sentinel"), "no message projection");
            AssertEqual(false, json.Contains("private-sentinel"), "no remote credentials");
            File.WriteAllText(Path.Combine(directory, "session_index.jsonl"), """{"id":"fake","thread_name":"Explicit name"}""");
            var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { root },
                nowUtc: DateTimeOffset.FromUnixTimeSeconds(1793527260), codexMetadata: sessions);
            AssertEqual("Explicit name", snapshot.Sessions.Single().Title, "explicit name survives database enrichment");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

#if !NET472
    private static void TestAgentStatusCliJson() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-agent-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "sessions"));
        var output = Console.Out;
        var error = Console.Error;
        using var capture = new StringWriter(CultureInfo.InvariantCulture);
        try {
            File.WriteAllText(Path.Combine(directory, "sessions", "rollout-synthetic.jsonl"), """
                {"timestamp":"2026-11-01T10:00:00Z","type":"session_meta","payload":{"id":"fake-cli","title":"Synthetic work"}}
                {"timestamp":"2026-11-01T10:01:00Z","type":"event_msg","payload":{"type":"turn_aborted","message":"private sentinel"}}
                """);
            Console.SetOut(capture);
            Console.SetError(capture);
            var exit = IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--json", "--all-history", "--codex-root", directory, "--cache-path", Path.Combine(directory, "cache.db") }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "CLI snapshot exit");
            var json = capture.ToString();
            var snapshot = JsonSerializer.Deserialize(json, AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
            AssertEqual(1, snapshot.Sessions.Count, "explicit root isolation");
            AssertEqual("fake-cli", snapshot.Sessions[0].Id, "CLI session");
            AssertEqual(AgentAttentionKind.InterruptedTurn, snapshot.NeedsAttention[0].Kind, "CLI attention rule");
            AssertEqual(false, json.Contains("private sentinel"), "CLI privacy");
            capture.GetStringBuilder().Clear();
            exit = IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--json", "--all-history", "--codex-root", directory,
                "--cache-path", Path.Combine(directory, "cache.db") }).GetAwaiter().GetResult();
            var warm = JsonSerializer.Deserialize(capture.ToString(), AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
            AssertEqual(0, exit, "warm CLI snapshot exit");
            AssertEqual(1, warm.Scan!.FilesReused, "persistent CLI cache reuse");
            File.WriteAllText(Path.Combine(directory, "sessions", "rollout-synthetic.jsonl"), $$$"""
                {"timestamp":"{{{DateTimeOffset.UtcNow.AddDays(-8).ToString("O")}}}","type":"session_meta","payload":{"id":"fake-cli"}}
                """);
            capture.GetStringBuilder().Clear();
            exit = IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--json", "--codex-root", directory,
                "--since", "7d", "--no-cache" }).GetAwaiter().GetResult();
            var recent = JsonSerializer.Deserialize(capture.ToString(), AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
            AssertEqual(0, exit, "recent CLI snapshot exit");
            AssertEqual(0, recent.Sessions.Count, "CLI since filter");
            AssertEqual(1, IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--since", "7" }).GetAwaiter().GetResult(), "invalid age");
            AssertEqual(1, IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--no-cache", "--cache-path", "unused" }).GetAwaiter().GetResult(), "conflicting cache flags");
            AssertEqual(1, IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--stale-days", "0" }).GetAwaiter().GetResult(), "invalid threshold");
        } finally {
            Console.SetOut(output);
            Console.SetError(error);
            Directory.Delete(directory, recursive: true);
        }
    }
#endif
}
