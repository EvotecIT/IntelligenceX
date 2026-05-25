using System.Text.Json;
using DBAClientX;
using IntelligenceX.Codex;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestCodexLocalStateDiagnosticsFlagsExtendedPathsAndMetadata() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            Directory.CreateDirectory(codexHome);
            var dbPath = Path.Combine(codexHome, "state_5.sqlite");
            var sqlite = new SQLite();
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                CREATE TABLE threads (
                    id TEXT PRIMARY KEY,
                    title TEXT,
                    first_user_message TEXT,
                    rollout_path TEXT,
                    cwd TEXT,
                    archived INTEGER
                );
                """);
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, first_user_message, rollout_path, cwd, archived)
                VALUES (@id, @title, @preview, @rollout_path, @cwd, 0);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    ["@title"] = new string('x', 180),
                    ["@preview"] = new string('y', 260),
                    ["@rollout_path"] = @"\\?\C:\Users\Example\.codex\sessions\rollout.jsonl",
                    ["@cwd"] = @"\\?\C:\Support\GitHub\Example"
                });
            File.WriteAllText(
                Path.Combine(codexHome, "config.toml"),
                "source = '\\\\?\\C:\\Users\\Example\\.codex\\skills\\keep-codex-fast'\n");

            var service = new CodexLocalStateDiagnosticsService();
            var diagnostics = service.CollectAsync(codexHome).GetAwaiter().GetResult();

            AssertEqual(CodexLocalStateHealthStatus.Warning, diagnostics.Status, "codex local state status");
            AssertEqual(2, diagnostics.ExtendedPathCount, "codex sqlite extended path count");
            AssertEqual(1, diagnostics.ConfigExtendedPathCount, "codex config extended path count");
            AssertEqual(1, diagnostics.OversizedThreadMetadataCount, "codex oversized metadata count");
            AssertEqual(1, diagnostics.ActiveThreadCount, "codex active thread count");
            AssertEqual(true, diagnostics.DatabaseExists, "codex state db exists");
            AssertEqual(true, diagnostics.CanConnect, "codex state db connect");
            AssertEqual(true, diagnostics.Findings.Any(f => f.Key == "extended-paths:threads.rollout_path"), "codex rollout path finding");
            AssertEqual(true, diagnostics.Findings.Any(f => f.Key == "extended-paths:threads.cwd"), "codex cwd finding");
            AssertEqual(true, diagnostics.Findings.Any(f => f.Key == "config-extended-paths"), "codex config finding");
            AssertEqual(true, diagnostics.Findings.Any(f => f.Key == "thread-metadata-bloat"), "codex metadata finding");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static void TestCodexLocalStateDiagnosticsIgnoresPathSyntaxInTextColumns() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            Directory.CreateDirectory(codexHome);
            var dbPath = Path.Combine(codexHome, "state_5.sqlite");
            var sqlite = new SQLite();
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                CREATE TABLE threads (
                    id TEXT PRIMARY KEY,
                    title TEXT,
                    first_user_message TEXT,
                    rollout_path TEXT,
                    cwd TEXT,
                    archived INTEGER
                );
                """);
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, first_user_message, rollout_path, cwd, archived)
                VALUES (@id, @title, @preview, @rollout_path, @cwd, 0);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                    ["@title"] = @"How does \\?\C:\ path syntax work?",
                    ["@preview"] = @"This thread is only discussing the \\?\ prefix.",
                    ["@rollout_path"] = Path.Combine(codexHome, "sessions", "rollout.jsonl"),
                    ["@cwd"] = root
                });
            File.WriteAllText(
                Path.Combine(codexHome, "session_index.jsonl"),
                JsonSerializer.Serialize(new {
                    id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                    thread_name = @"friendly \\?\ syntax discussion"
                }) + Environment.NewLine);

            var service = new CodexLocalStateDiagnosticsService();
            var diagnostics = service.CollectAsync(codexHome).GetAwaiter().GetResult();

            AssertEqual(CodexLocalStateHealthStatus.Healthy, diagnostics.Status, "codex local state healthy");
            AssertEqual(0, diagnostics.ExtendedPathCount, "codex ignores text extended syntax");
            AssertEqual(0, diagnostics.ConfigExtendedPathCount, "codex ignores session index extended syntax");
            AssertEqual(0, diagnostics.OversizedThreadMetadataCount, "codex no metadata warning");
            AssertEqual(0, diagnostics.Findings.Count, "codex no findings");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static string CreateCodexDiagnosticsTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-codex-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteCodexDiagnosticsDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup for test temp directories.
        }
    }
}
