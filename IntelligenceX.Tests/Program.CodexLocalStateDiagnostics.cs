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

    private static void TestCodexLocalStateDiagnosticsRecoversThreadArchiveState() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            var backupRoot = Path.Combine(root, "backups");
            Directory.CreateDirectory(codexHome);
            var automationDirectory = Path.Combine(codexHome, "automations", "recheck-thread");
            Directory.CreateDirectory(automationDirectory);
            var automationPath = Path.Combine(automationDirectory, "automation.toml");
            File.WriteAllText(
                automationPath,
                """
                version = 1
                id = "recheck-thread"
                kind = "heartbeat"
                name = "Recheck Thread"
                status = "ACTIVE"
                rrule = "FREQ=MINUTELY;INTERVAL=15"
                target_thread_id = "abababab-abab-abab-abab-abababababab"
                """);
            var dbPath = Path.Combine(codexHome, "state_5.sqlite");
            var sqlite = new SQLite();
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                CREATE TABLE threads (
                    id TEXT PRIMARY KEY,
                    title TEXT,
                    archived INTEGER,
                    archived_at INTEGER
                );
                """);
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, archived, archived_at)
                VALUES (@id, @title, 0, NULL);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "abababab-abab-abab-abab-abababababab",
                    ["@title"] = "active thread"
                });

            var service = new CodexLocalStateDiagnosticsService();
            var result = service
                .RecoverThreadArchiveStateAsync("abababab-abab-abab-abab-abababababab", codexHome, backupRoot)
                .GetAwaiter()
                .GetResult();
            var archived = Convert.ToInt32(sqlite.ExecuteScalar(
                dbPath,
                "SELECT archived FROM threads WHERE id = @id;",
                new Dictionary<string, object?> {
                    ["@id"] = "abababab-abab-abab-abab-abababababab"
                }), CultureInfo.InvariantCulture);
            var archivedAt = sqlite.ExecuteScalar(
                dbPath,
                "SELECT archived_at FROM threads WHERE id = @id;",
                new Dictionary<string, object?> {
                    ["@id"] = "abababab-abab-abab-abab-abababababab"
                });

            AssertEqual(true, result.DatabaseExists, "codex thread recovery database exists");
            AssertEqual(true, result.ArchiveColumnsAvailable, "codex thread recovery archive columns");
            AssertEqual(true, result.ThreadFound, "codex thread recovery found target");
            AssertEqual(false, result.WasArchived, "codex thread recovery original active");
            AssertEqual(false, result.FinalArchived, "codex thread recovery final active");
            AssertEqual(true, File.Exists(result.BackupDatabasePath), "codex thread recovery backup exists");
            AssertEqual(1, result.AutomationCountBefore, "codex thread recovery automation count before");
            AssertEqual(1, result.AutomationCountAfter, "codex thread recovery automation count after");
            AssertEqual(0, result.AutomationRestoredCount, "codex thread recovery automation restored count");
            AssertEqual("recheck-thread", result.AutomationIds.Single(), "codex thread recovery automation id");
            AssertEqual(true, File.Exists(Path.Combine(result.AutomationBackupDirectory, "recheck-thread", "automation.toml")), "codex thread recovery automation backup exists");
            AssertEqual(true, File.Exists(automationPath), "codex thread recovery automation preserved");
            AssertEqual(0, archived, "codex thread recovery active archived flag");
            AssertEqual(true, archivedAt is null || archivedAt is DBNull, "codex thread recovery active archived_at");
        } finally {
            TryDeleteCodexDiagnosticsDirectory(root);
        }
    }

    private static void TestCodexLocalStateDiagnosticsDetectsBrokenThreadCandidates() {
        var root = CreateCodexDiagnosticsTempDirectory();
        try {
            var codexHome = Path.Combine(root, ".codex");
            Directory.CreateDirectory(codexHome);
            var dbPath = Path.Combine(codexHome, "state_5.sqlite");
            var logsDbPath = Path.Combine(codexHome, "logs_2.sqlite");
            var sqlite = new SQLite();
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                CREATE TABLE threads (
                    id TEXT PRIMARY KEY,
                    title TEXT,
                    archived INTEGER,
                    archived_at INTEGER
                );
                """);
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, archived, archived_at)
                VALUES (@id, @title, 0, NULL);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "12121212-1212-1212-1212-121212121212",
                    ["@title"] = "recoverable candidate"
                });
            sqlite.ExecuteNonQuery(
                dbPath,
                """
                INSERT INTO threads (id, title, archived, archived_at)
                VALUES (@id, @title, 1, @archivedAt);
                """,
                new Dictionary<string, object?> {
                    ["@id"] = "34343434-3434-3434-3434-343434343434",
                    ["@title"] = "archived candidate",
                    ["@archivedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            sqlite.ExecuteNonQuery(
                logsDbPath,
                """
                CREATE TABLE logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts INTEGER NOT NULL,
                    level TEXT NOT NULL,
                    target TEXT NOT NULL,
                    feedback_log_body TEXT,
                    thread_id TEXT
                );
                """);
            sqlite.ExecuteNonQuery(
                logsDbPath,
                """
                INSERT INTO logs (ts, level, target, feedback_log_body, thread_id)
                VALUES (@ts, 'WARN', 'codex_app_server::mcp_refresh', @body, NULL);
                """,
                new Dictionary<string, object?> {
                    ["@ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["@body"] = "failed to queue MCP refresh for thread 12121212-1212-1212-1212-121212121212: internal error; agent loop died unexpectedly"
                });
            sqlite.ExecuteNonQuery(
                logsDbPath,
                """
                INSERT INTO logs (ts, level, target, feedback_log_body, thread_id)
                VALUES (@ts, 'WARN', 'codex_app_server::mcp_refresh', @body, NULL);
                """,
                new Dictionary<string, object?> {
                    ["@ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["@body"] = " \t\r\nfailed to queue MCP refresh for thread 12121212-1212-1212-1212-121212121212: internal error; agent loop died unexpectedly"
                });
            sqlite.ExecuteNonQuery(
                logsDbPath,
                """
                INSERT INTO logs (ts, level, target, feedback_log_body, thread_id)
                VALUES (@ts, 'WARN', 'codex_app_server::mcp_refresh', @body, NULL);
                """,
                new Dictionary<string, object?> {
                    ["@ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["@body"] = "failed to queue MCP refresh for thread 34343434-3434-3434-3434-343434343434: internal error; agent loop died unexpectedly"
                });
            sqlite.ExecuteNonQuery(
                logsDbPath,
                """
                INSERT INTO logs (ts, level, target, feedback_log_body, thread_id)
                VALUES (@ts, 'WARN', 'codex_app_server::mcp_refresh', @body, NULL);
                """,
                new Dictionary<string, object?> {
                    ["@ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["@body"] = "user pasted unrelated id aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa while discussing agent loop died"
                });
            sqlite.ExecuteNonQuery(
                logsDbPath,
                """
                INSERT INTO logs (ts, level, target, feedback_log_body, thread_id)
                VALUES (@ts, 'INFO', 'codex_core::session', @body, @threadId);
                """,
                new Dictionary<string, object?> {
                    ["@ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["@body"] = "session_loop{thread_id=12121212-1212-1212-1212-121212121212}: diagnostic command text mentions agent loop died unexpectedly",
                    ["@threadId"] = "12121212-1212-1212-1212-121212121212"
                });

            var service = new CodexLocalStateDiagnosticsService();
            var diagnostics = service.CollectAsync(codexHome).GetAwaiter().GetResult();
            var candidate = diagnostics.BrokenThreadCandidates.Single(item => item.ThreadId == "12121212-1212-1212-1212-121212121212");
            var archivedCandidate = diagnostics.BrokenThreadCandidates.Single(item => item.ThreadId == "34343434-3434-3434-3434-343434343434");

            AssertEqual(CodexLocalStateHealthStatus.Warning, diagnostics.Status, "codex broken thread status");
            AssertEqual(2, diagnostics.BrokenThreadCandidateCount, "codex broken thread candidate count");
            AssertEqual(1, diagnostics.RecoverableBrokenThreadCandidateCount, "codex recoverable broken thread candidate count");
            AssertEqual("12121212-1212-1212-1212-121212121212", candidate.ThreadId, "codex broken thread candidate id");
            AssertEqual(true, candidate.ThreadFound, "codex broken thread candidate found");
            AssertEqual(false, candidate.IsArchived, "codex broken thread candidate active");
            AssertEqual(2, candidate.FailureCount, "codex broken thread candidate failures");
            AssertEqual(true, archivedCandidate.ThreadFound, "codex archived broken thread candidate found");
            AssertEqual(true, archivedCandidate.IsArchived, "codex archived broken thread candidate archived");
            AssertEqual(true, diagnostics.Findings.Any(f => f.Key == "broken-thread-candidates"), "codex broken thread finding");
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
