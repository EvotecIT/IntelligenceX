using System;
using System.Text.Json.Serialization;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Service;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BackgroundSchedulerRuntimeStoreDto))]
[JsonSerializable(typeof(SessionCapabilityBackgroundSchedulerActivityDto[]))]
internal sealed partial class BackgroundSchedulerRuntimeStoreJsonContext : JsonSerializerContext;

internal sealed class BackgroundSchedulerRuntimeStoreDto {
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
    [JsonPropertyName("lastSchedulerTickUtcTicks")]
    public long LastSchedulerTickUtcTicks { get; set; }
    [JsonPropertyName("lastOutcome")]
    public string LastOutcome { get; set; } = string.Empty;
    [JsonPropertyName("lastOutcomeUtcTicks")]
    public long LastOutcomeUtcTicks { get; set; }
    [JsonPropertyName("lastSuccessUtcTicks")]
    public long LastSuccessUtcTicks { get; set; }
    [JsonPropertyName("lastFailureUtcTicks")]
    public long LastFailureUtcTicks { get; set; }
    [JsonPropertyName("completedExecutionCount")]
    public int CompletedExecutionCount { get; set; }
    [JsonPropertyName("requeuedExecutionCount")]
    public int RequeuedExecutionCount { get; set; }
    [JsonPropertyName("releasedExecutionCount")]
    public int ReleasedExecutionCount { get; set; }
    [JsonPropertyName("consecutiveFailureCount")]
    public int ConsecutiveFailureCount { get; set; }
    [JsonPropertyName("pausedUntilUtcTicks")]
    public long PausedUntilUtcTicks { get; set; }
    [JsonPropertyName("pauseReason")]
    public string PauseReason { get; set; } = string.Empty;
    [JsonPropertyName("lastAdaptiveIdleUtcTicks")]
    public long LastAdaptiveIdleUtcTicks { get; set; }
    [JsonPropertyName("lastAdaptiveIdleDelaySeconds")]
    public int LastAdaptiveIdleDelaySeconds { get; set; }
    [JsonPropertyName("lastAdaptiveIdleReason")]
    public string LastAdaptiveIdleReason { get; set; } = string.Empty;
    [JsonPropertyName("recentActivity")]
    public SessionCapabilityBackgroundSchedulerActivityDto[] RecentActivity { get; set; } = Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>();
}
