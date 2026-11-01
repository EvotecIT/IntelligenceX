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
            var json = AgentWorkJson.Serialize(new AgentWorkSnapshot { Sessions = sessions });
            AssertEqual(false, json.Contains("private sentinel"), "no message projection");
            AssertEqual(false, json.Contains("private-sentinel"), "no remote credentials");
            File.WriteAllText(Path.Combine(directory, "session_index.jsonl"), """{"id":"fake","thread_name":"Explicit name"}""");
            var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { root }, codexMetadata: sessions);
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
            var exit = IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--json", "--codex-root", directory }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "CLI snapshot exit");
            var json = capture.ToString();
            var snapshot = JsonSerializer.Deserialize(json, AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
            AssertEqual(1, snapshot.Sessions.Count, "explicit root isolation");
            AssertEqual("fake-cli", snapshot.Sessions[0].Id, "CLI session");
            AssertEqual(AgentAttentionKind.InterruptedTurn, snapshot.NeedsAttention[0].Kind, "CLI attention rule");
            AssertEqual(false, json.Contains("private sentinel"), "CLI privacy");
            AssertEqual(1, IntelligenceX.Cli.Program.DispatchAsync(new[] { "agents", "status", "--stale-days", "0" }).GetAwaiter().GetResult(), "invalid threshold");
        } finally {
            Console.SetOut(output);
            Console.SetError(error);
            Directory.Delete(directory, recursive: true);
        }
    }
#endif
}
