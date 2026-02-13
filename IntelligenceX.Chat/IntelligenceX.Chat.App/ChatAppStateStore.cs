using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;

namespace IntelligenceX.Chat.App;

internal sealed class ChatAppStateStore : IDisposable {
    private readonly string _dbPath;
    private readonly JsonSerializerOptions _json;
    private readonly SQLite _db = new();

    public ChatAppStateStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = dbPath;
        _json = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }

        EnsureSchema();
    }

    public static string GetDefaultDbPath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "app-state.db");
    }

    public Task<ChatAppState?> GetAsync(string profileName, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(profileName)) {
            return Task.FromResult<ChatAppState?>(null);
        }

        var json = _db.ExecuteScalar(
            _dbPath,
            "SELECT json FROM ix_app_profiles WHERE profile_name = @name",
            parameters: new Dictionary<string, object?> { ["@name"] = profileName.Trim() }) as string;

        if (string.IsNullOrWhiteSpace(json)) {
            return Task.FromResult<ChatAppState?>(null);
        }

        try {
            var state = JsonSerializer.Deserialize<ChatAppState>(json, _json);
            return Task.FromResult(state);
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to parse app profile '{profileName}'.", ex);
        }
    }

    public Task UpsertAsync(string profileName, ChatAppState state, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(profileName)) {
            throw new ArgumentException("Profile name cannot be empty.", nameof(profileName));
        }

        var payload = state ?? throw new ArgumentNullException(nameof(state));
        payload.ProfileName = profileName.Trim();
        payload.UpdatedUtc = DateTime.UtcNow;

        if (payload.Messages is { Count: > 250 }) {
            payload.Messages = payload.Messages
                .Skip(payload.Messages.Count - 250)
                .ToList();
        }

        if (payload.Conversations is { Count: > 0 }) {
            if (payload.Conversations.Count > 40) {
                payload.Conversations = payload.Conversations
                    .OrderByDescending(conversation => conversation.UpdatedUtc)
                    .Take(40)
                    .ToList();
            }

            foreach (var conversation in payload.Conversations) {
                if (conversation.Messages is { Count: > 250 }) {
                    conversation.Messages = conversation.Messages
                        .Skip(conversation.Messages.Count - 250)
                        .ToList();
                }
            }
        }

        var json = JsonSerializer.Serialize(payload, _json);

        _db.ExecuteNonQuery(
            _dbPath,
            """
            INSERT INTO ix_app_profiles (profile_name, json, updated_utc)
            VALUES (@name, @json, @updated_utc)
            ON CONFLICT(profile_name) DO UPDATE SET
              json = excluded.json,
              updated_utc = excluded.updated_utc;
            """,
            parameters: new Dictionary<string, object?> {
                ["@name"] = payload.ProfileName,
                ["@json"] = json,
                ["@updated_utc"] = payload.UpdatedUtc.ToString("O")
            });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListProfileNamesAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var names = new List<string>();
        var result = _db.Query(_dbPath, "SELECT profile_name FROM ix_app_profiles ORDER BY profile_name");
        if (result is System.Data.DataTable dt) {
            foreach (System.Data.DataRow row in dt.Rows) {
                var value = row[0]?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    names.Add(value!);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private void EnsureSchema() {
        _db.ExecuteNonQuery(_dbPath, """
            CREATE TABLE IF NOT EXISTS ix_app_profiles (
              profile_name TEXT PRIMARY KEY,
              json TEXT NOT NULL,
              updated_utc TEXT NOT NULL
            );
            """);
    }

    public void Dispose() {
        _db.Dispose();
    }
}

internal sealed class ChatAppState {
    public string ProfileName { get; set; } = "default";
    public string? UserName { get; set; }
    public string? AssistantPersona { get; set; }
    public string ThemePreset { get; set; } = "default";
    public string TimestampMode { get; set; } = "seconds";
    public int? AutonomyMaxToolRounds { get; set; }
    public bool? AutonomyParallelTools { get; set; }
    public int? AutonomyTurnTimeoutSeconds { get; set; }
    public int? AutonomyToolTimeoutSeconds { get; set; }
    public string ExportSaveMode { get; set; } = ExportPreferencesContract.DefaultSaveMode;
    public string ExportDefaultFormat { get; set; } = ExportPreferencesContract.DefaultFormat;
    public string? ExportLastDirectory { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string? ActiveConversationId { get; set; }
    public string? ThreadId { get; set; }
    public List<string> DisabledTools { get; set; } = new();
    public List<ChatMessageState> Messages { get; set; } = new();
    public List<ChatConversationState> Conversations { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatConversationState {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = "New Chat";
    public string? ThreadId { get; set; }
    public List<ChatMessageState> Messages { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatMessageState {
    public string Role { get; set; } = "System";
    public string Text { get; set; } = string.Empty;
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
}
