using System.Globalization;
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

    private static void TestCodexLocalStateDiagnosticsHandlesMigrationDefaultValues() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            Directory.CreateDirectory(codexHome);
            var dbPath = Path.Combine(codexHome, "state_5.sqlite");
            var sqlite = new SQLite();
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                CREATE TABLE _sqlx_migrations (
                    version BIGINT PRIMARY KEY,
                    description TEXT NOT NULL,
                    installed_on TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """);
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                CREATE TABLE threads (
                    id TEXT PRIMARY KEY,
                    title TEXT,
                    first_user_message TEXT,
                    cwd TEXT,
                    archived INTEGER
                );
                """);
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, first_user_message, cwd, archived)
                VALUES (@id, @title, @preview, @cwd, 0);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "cccccccc-cccc-cccc-cccc-cccccccccccc",
                    ["@title"] = "short title",
                    ["@preview"] = "short preview",
                    ["@cwd"] = @"\\?\C:\Support\GitHub\Example"
                });

            var service = new CodexLocalStateDiagnosticsService();
            var diagnostics = service.CollectAsync(codexHome).GetAwaiter().GetResult();

            AssertEqual(CodexLocalStateHealthStatus.Warning, diagnostics.Status, "codex migrations default status");
            AssertEqual(true, diagnostics.CanConnect, "codex migrations default can connect");
            AssertEqual(1, diagnostics.ExtendedPathCount, "codex migrations default extended path count");
            AssertEqual(true, diagnostics.Findings.Any(f => f.Key == "extended-paths:threads.cwd"), "codex migrations default cwd finding");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static void TestCodexLocalStateDiagnosticsNormalizesActiveThreadPaths() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            var backupRoot = Path.Combine(root, "backups");
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
                VALUES (@id, @title, @preview, @rollout_path, @cwd, @archived);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "dddddddd-dddd-dddd-dddd-dddddddddddd",
                    ["@title"] = "active",
                    ["@preview"] = "active preview",
                    ["@rollout_path"] = @"\\?\C:\Users\Example\.codex\sessions\rollout.jsonl",
                    ["@cwd"] = @"\\?\C:\Support\GitHub\Example",
                    ["@archived"] = 0
                });
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, first_user_message, rollout_path, cwd, archived)
                VALUES (@id, @title, @preview, @rollout_path, @cwd, @archived);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                    ["@title"] = "archived",
                    ["@preview"] = "archived preview",
                    ["@rollout_path"] = @"\\?\C:\Users\Example\.codex\sessions\archived.jsonl",
                    ["@cwd"] = @"\\?\C:\Support\GitHub\Archived",
                    ["@archived"] = 1
                });

            var service = new CodexLocalStateDiagnosticsService();
            var result = service.NormalizeActiveThreadPathsAsync(codexHome, backupRoot).GetAwaiter().GetResult();
            var activeRolloutPath = Convert.ToString(sqlite.ExecuteScalar(
                dbPath,
                "SELECT rollout_path FROM threads WHERE id = @id;",
                new Dictionary<string, object?> {
                    ["@id"] = "dddddddd-dddd-dddd-dddd-dddddddddddd"
                }), CultureInfo.InvariantCulture);
            var activeCwd = Convert.ToString(sqlite.ExecuteScalar(
                dbPath,
                "SELECT cwd FROM threads WHERE id = @id;",
                new Dictionary<string, object?> {
                    ["@id"] = "dddddddd-dddd-dddd-dddd-dddddddddddd"
                }), CultureInfo.InvariantCulture);
            var archivedRolloutPath = Convert.ToString(sqlite.ExecuteScalar(
                dbPath,
                "SELECT rollout_path FROM threads WHERE id = @id;",
                new Dictionary<string, object?> {
                    ["@id"] = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
                }), CultureInfo.InvariantCulture);

            AssertEqual(true, result.DatabaseExists, "codex path repair database exists");
            AssertEqual(2, result.ScannedPathValueCount, "codex path repair scanned values");
            AssertEqual(2, result.ChangedPathValueCount, "codex path repair changed values");
            AssertEqual(1, result.ChangedThreadRowCount, "codex path repair changed rows");
            AssertEqual(true, File.Exists(result.BackupDatabasePath), "codex path repair backup exists");
            AssertEqual(@"C:\Users\Example\.codex\sessions\rollout.jsonl", activeRolloutPath, "codex path repair active rollout");
            AssertEqual(@"C:\Support\GitHub\Example", activeCwd, "codex path repair active cwd");
            AssertEqual(@"\\?\C:\Users\Example\.codex\sessions\archived.jsonl", archivedRolloutPath, "codex path repair archived unchanged");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static void TestCodexLocalStateDiagnosticsReportsStorageAreas() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            Directory.CreateDirectory(Path.Combine(codexHome, "sessions"));
            Directory.CreateDirectory(Path.Combine(codexHome, "logs"));
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
            File.WriteAllText(Path.Combine(codexHome, "sessions", "rollout.jsonl"), "session");
            File.WriteAllText(Path.Combine(codexHome, "logs", "codex.log"), "log");

            var service = new CodexLocalStateDiagnosticsService();
            var diagnostics = service.CollectAsync(codexHome).GetAwaiter().GetResult();

            AssertEqual(true, diagnostics.Areas.Any(a => a.Key == "state-db"), "codex areas include sqlite state");
            AssertEqual(true, diagnostics.Areas.Any(a => a.Key == "sessions" && a.FileCount == 1), "codex areas include sessions");
            AssertEqual(true, diagnostics.Areas.Any(a => a.Key == "logs" && a.FileCount == 1), "codex areas include logs");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static void TestCodexLocalStateCleanupArchivesStaleFiles() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            var backupRoot = Path.Combine(root, "backups");
            var sessions = Path.Combine(codexHome, "sessions", "2026", "05", "01");
            var logs = Path.Combine(codexHome, "logs");
            Directory.CreateDirectory(sessions);
            Directory.CreateDirectory(logs);
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
            var staleSession = Path.Combine(sessions, "old.jsonl");
            var activeSession = Path.Combine(sessions, "active.jsonl");
            var staleLog = Path.Combine(logs, "old.log");
            File.WriteAllText(staleSession, "old-session");
            File.WriteAllText(activeSession, "active-session");
            File.WriteAllText(staleLog, "old-log");
            File.SetLastWriteTimeUtc(staleSession, DateTime.UtcNow.AddDays(-20));
            File.SetLastWriteTimeUtc(activeSession, DateTime.UtcNow.AddDays(-20));
            File.SetLastWriteTimeUtc(staleLog, DateTime.UtcNow.AddHours(-4));
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, first_user_message, rollout_path, cwd, archived)
                VALUES (@id, @title, @preview, @rollout_path, @cwd, 0);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "ffffffff-ffff-ffff-ffff-ffffffffffff",
                    ["@title"] = "active",
                    ["@preview"] = "active preview",
                    ["@rollout_path"] = activeSession,
                    ["@cwd"] = string.Empty
                });

            var service = new CodexLocalStateDiagnosticsService();
            var result = service.CleanUpAsync(codexHome, backupRoot).GetAwaiter().GetResult();

            AssertEqual(1, result.ArchivedSessionFileCount, "codex cleanup archived stale inactive session");
            AssertEqual(1, result.ArchivedLogFileCount, "codex cleanup archived stale log");
            AssertEqual(false, File.Exists(staleSession), "codex cleanup moved stale session");
            AssertEqual(true, File.Exists(activeSession), "codex cleanup kept active session");
            AssertEqual(false, File.Exists(staleLog), "codex cleanup moved stale log");
            AssertEqual(true, Directory.Exists(result.ArchiveDirectory), "codex cleanup archive directory");
            AssertEqual(true, File.Exists(result.PathRepair.BackupDatabasePath), "codex cleanup path repair backup");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static void TestCodexLocalStateCleanupCreatesEmptyArchiveRoot() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            var backupRoot = Path.Combine(root, "backups");
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

            var service = new CodexLocalStateDiagnosticsService();
            var result = service.CleanUpAsync(codexHome, backupRoot).GetAwaiter().GetResult();

            AssertEqual(0, result.ArchivedSessionFileCount, "codex cleanup empty archive sessions");
            AssertEqual(0, result.ArchivedLogFileCount, "codex cleanup empty archive logs");
            AssertEqual(true, Directory.Exists(result.ArchiveDirectory), "codex cleanup empty archive directory");
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
