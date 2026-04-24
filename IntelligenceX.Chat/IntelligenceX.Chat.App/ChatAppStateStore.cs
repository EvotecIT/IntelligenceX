using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Chat.App;

internal sealed class ChatAppStateStore : IDisposable {
    private const int MaxPersistedMessages = 500;
    private const int MaxPersistedConversations = 80;
    private const int MaxPersistedMemoryFacts = 120;
    private const int MaxPersistedModelEntries = 250;
    private const int MaxPersistedFavoriteEntries = 100;
    private const int MaxPersistedRecentEntries = 100;
    private const int MaxPersistedQueuedTurns = 24;
    private const int MaxPersistedPendingActionsPerConversation = 6;

    private readonly string _dbPath;
    private readonly JsonSerializerOptions _json;
    private readonly SQLite _db = new();

    public ChatAppStateStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath);
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

    internal string DatabasePath => _dbPath;

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

        if (payload.Messages is { Count: > MaxPersistedMessages }) {
            payload.Messages = payload.Messages
                .Skip(payload.Messages.Count - MaxPersistedMessages)
                .ToList();
        }

        if (payload.Conversations is { Count: > 0 }) {
            if (payload.Conversations.Count > MaxPersistedConversations) {
                payload.Conversations = payload.Conversations
                    .OrderByDescending(conversation => conversation.UpdatedUtc)
                    .Take(MaxPersistedConversations)
                    .ToList();
            }

            foreach (var conversation in payload.Conversations) {
                if (conversation.Messages is { Count: > MaxPersistedMessages }) {
                    conversation.Messages = conversation.Messages
                        .Skip(conversation.Messages.Count - MaxPersistedMessages)
                        .ToList();
                }

                if (conversation.PendingActions is { Count: > MaxPersistedPendingActionsPerConversation }) {
                    conversation.PendingActions = conversation.PendingActions
                        .Skip(conversation.PendingActions.Count - MaxPersistedPendingActionsPerConversation)
                        .ToList();
                }
            }
        }

        if (payload.MemoryFacts is { Count: > 0 } && payload.MemoryFacts.Count > MaxPersistedMemoryFacts) {
            payload.MemoryFacts = payload.MemoryFacts
                .OrderByDescending(fact => fact.UpdatedUtc)
                .Take(MaxPersistedMemoryFacts)
                .ToList();
        }

        if (payload.CachedModels is { Count: > MaxPersistedModelEntries }) {
            payload.CachedModels = payload.CachedModels
                .Take(MaxPersistedModelEntries)
                .ToList();
        }

        if (payload.CachedFavoriteModels is { Count: > MaxPersistedFavoriteEntries }) {
            payload.CachedFavoriteModels = payload.CachedFavoriteModels
                .Take(MaxPersistedFavoriteEntries)
                .ToList();
        }

        if (payload.CachedRecentModels is { Count: > MaxPersistedRecentEntries }) {
            payload.CachedRecentModels = payload.CachedRecentModels
                .Take(MaxPersistedRecentEntries)
                .ToList();
        }

        if (payload.PendingTurns is { Count: > MaxPersistedQueuedTurns }) {
            payload.PendingTurns = payload.PendingTurns
                .OrderBy(turn => turn.EnqueuedUtc)
                .TakeLast(MaxPersistedQueuedTurns)
                .ToList();
        }

        if (payload.QueuedTurnsAfterLogin is { Count: > MaxPersistedQueuedTurns }) {
            payload.QueuedTurnsAfterLogin = payload.QueuedTurnsAfterLogin
                .OrderBy(turn => turn.EnqueuedUtc)
                .TakeLast(MaxPersistedQueuedTurns)
                .ToList();
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
        var dt = QueryAsTable(_db.Query(_dbPath, "SELECT profile_name FROM ix_app_profiles ORDER BY profile_name"));
        if (dt is not null) {
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

    private static DataTable? QueryAsTable(object? queryResult) {
        if (queryResult is DataTable table) {
            return table;
        }

        if (queryResult is DataSet dataSet && dataSet.Tables.Count > 0) {
            return dataSet.Tables[0];
        }

        return null;
    }
}

internal sealed class ChatAppState {
    public string ProfileName { get; set; } = "default";
    public string? UserName { get; set; }
    public string? AssistantPersona { get; set; }
    public string ThemePreset { get; set; } = "default";
    public string LocalProviderTransport { get; set; } = "native";
    public string? LocalProviderBaseUrl { get; set; }
    public string LocalProviderModel { get; set; } = OpenAIModelCatalog.DefaultModel;
    public string LocalProviderOpenAIAuthMode { get; set; } = "bearer";
    public string LocalProviderOpenAIBasicUsername { get; set; } = string.Empty;
    public string LocalProviderOpenAIAccountId { get; set; } = string.Empty;
    public int ActiveNativeAccountSlot { get; set; } = 1;
    public string NativeAccountSlot1 { get; set; } = string.Empty;
    public string NativeAccountSlot2 { get; set; } = string.Empty;
    public string NativeAccountSlot3 { get; set; } = string.Empty;
    public List<string> NativeAccountSlots { get; set; } = new();
    public string LocalProviderReasoningEffort { get; set; } = string.Empty;
    public string LocalProviderReasoningSummary { get; set; } = string.Empty;
    public string LocalProviderTextVerbosity { get; set; } = string.Empty;
    public double? LocalProviderTemperature { get; set; }
    public string TimestampMode { get; set; } = "seconds";
    public int? AutonomyMaxToolRounds { get; set; }
    public bool? AutonomyParallelTools { get; set; }
    public int? AutonomyTurnTimeoutSeconds { get; set; }
    public int? AutonomyToolTimeoutSeconds { get; set; }
    public bool? AutonomyWeightedToolRouting { get; set; }
    public int? AutonomyMaxCandidateTools { get; set; }
    public bool? AutonomyPlanExecuteReviewLoop { get; set; }
    public int? AutonomyMaxReviewPasses { get; set; }
    public int? AutonomyModelHeartbeatSeconds { get; set; }
    public string ExportSaveMode { get; set; } = ExportPreferencesContract.DefaultSaveMode;
    public string ExportDefaultFormat { get; set; } = ExportPreferencesContract.DefaultFormat;
    public string ExportVisualThemeMode { get; set; } = ExportPreferencesContract.DefaultVisualThemeMode;
    public int ExportDocxVisualMaxWidthPx { get; set; } = ExportPreferencesContract.DefaultDocxVisualMaxWidthPx;
    public string? ExportLastDirectory { get; set; }
    public bool QueueAutoDispatchEnabled { get; set; } = true;
    public bool ProactiveModeEnabled { get; set; }
    public bool PersistentMemoryEnabled { get; set; } = true;
    public bool ShowAssistantTurnTrace { get; set; }
    public bool ShowAssistantDraftBubbles { get; set; }
    public List<ChatMemoryFactState> MemoryFacts { get; set; } = new();
    public string CachedModelsTransport { get; set; } = "native";
    public string? CachedModelsBaseUrl { get; set; }
    public List<ModelInfoDto> CachedModels { get; set; } = new();
    public List<string> CachedFavoriteModels { get; set; } = new();
    public List<string> CachedRecentModels { get; set; } = new();
    public List<ChatAccountUsageState> AccountUsage { get; set; } = new();
    public bool CachedModelListIsStale { get; set; }
    public string? CachedModelListWarning { get; set; }
    public DateTime? CachedModelsUpdatedUtc { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string? ActiveConversationId { get; set; }
    public string? ThreadId { get; set; }
    public List<string> DisabledTools { get; set; } = new();
    public List<ChatMessageState> Messages { get; set; } = new();
    public List<ChatConversationState> Conversations { get; set; } = new();
    public List<ChatQueuedTurnState> PendingTurns { get; set; } = new();
    public List<ChatQueuedTurnState> QueuedTurnsAfterLogin { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatConversationState {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = "New Chat";
    public string? ThreadId { get; set; }
    public string? RuntimeLabel { get; set; }
    public string? ModelLabel { get; set; }
    public string? ModelOverride { get; set; }
    public string? PendingAssistantQuestionHint { get; set; }
    public List<ChatMessageState> Messages { get; set; } = new();
    public List<ChatPendingActionState> PendingActions { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatPendingActionState {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Request { get; set; } = string.Empty;
    public string Reply { get; set; } = string.Empty;
}

internal sealed class ChatQueuedTurnState {
    public string Text { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public DateTime EnqueuedUtc { get; set; } = DateTime.UtcNow;
    public bool SkipUserBubbleOnDispatch { get; set; }
}

internal sealed class ChatMessageState {
    public string Role { get; set; } = "System";
    public string Text { get; set; } = string.Empty;
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public string? Model { get; set; }
}

internal sealed class ChatMemoryFactState {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Fact { get; set; } = string.Empty;
    public int Weight { get; set; } = 3;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatAccountUsageState {
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public long CachedPromptTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public int Turns { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public DateTime? UsageLimitHitUtc { get; set; }
    public DateTime? UsageLimitRetryAfterUtc { get; set; }
    public string? PlanType { get; set; }
    public string? Email { get; set; }
    public bool? RateLimitAllowed { get; set; }
    public bool? RateLimitReached { get; set; }
    public double? RateLimitUsedPercent { get; set; }
    public DateTime? RateLimitWindowResetUtc { get; set; }
    public DateTime? UsageSnapshotRetrievedAtUtc { get; set; }
    public string? UsageSnapshotSource { get; set; }
    public bool? CreditsHasCredits { get; set; }
    public bool? CreditsUnlimited { get; set; }
    public double? CreditsBalance { get; set; }
}
