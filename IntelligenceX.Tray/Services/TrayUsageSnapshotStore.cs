using System.IO;
using System.Text.Json;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tray.Services;

public sealed class TrayUsageSnapshotStore {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IntelligenceX",
        "Tray",
        "usage-last-snapshot.json");

    public TrayUsageSnapshotCache? Load() {
        try {
            if (!File.Exists(SnapshotPath)) {
                return null;
            }

            var json = File.ReadAllText(SnapshotPath);
            var persisted = JsonSerializer.Deserialize<PersistedUsageSnapshot>(json, SerializerOptions);
            if (persisted is null || persisted.Events.Count == 0) {
                return null;
            }

            return new TrayUsageSnapshotCache(
                persisted.ScannedAtUtc,
                persisted.DiscoveredProviderIds
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                persisted.SourceRoots
                    .Select(static item => item.ToSourceRoot())
                    .Where(static item => item is not null)
                    .Cast<SourceRootRecord>()
                    .ToList(),
                persisted.Events.Select(static item => item.ToUsageEvent()).ToList());
        } catch {
            return null;
        }
    }

    public void Save(
        DateTimeOffset scannedAtUtc,
        IReadOnlyList<UsageEventRecord> events,
        IReadOnlyList<string>? discoveredProviderIds = null,
        IReadOnlyList<SourceRootRecord>? sourceRoots = null) {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) {
            return;
        }

        var directory = Path.GetDirectoryName(SnapshotPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var payload = new PersistedUsageSnapshot {
            ScannedAtUtc = scannedAtUtc,
            DiscoveredProviderIds = (discoveredProviderIds ?? Array.Empty<string>())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SourceRoots = (sourceRoots ?? Array.Empty<SourceRootRecord>())
                .Where(static item => item is not null)
                .Select(static item => PersistedSourceRoot.FromSourceRoot(item))
                .ToList(),
            Events = events.Select(static item => PersistedUsageEvent.FromUsageEvent(item)).ToList()
        };
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        File.WriteAllText(SnapshotPath, json);
    }

    public sealed record TrayUsageSnapshotCache(
        DateTimeOffset ScannedAtUtc,
        List<string> DiscoveredProviderIds,
        List<SourceRootRecord> SourceRoots,
        List<UsageEventRecord> Events);

    private sealed class PersistedUsageSnapshot {
        public DateTimeOffset ScannedAtUtc { get; set; }
        public List<string> DiscoveredProviderIds { get; set; } = [];
        public List<PersistedSourceRoot> SourceRoots { get; set; } = [];
        public List<PersistedUsageEvent> Events { get; set; } = [];
    }

    private sealed class PersistedSourceRoot {
        public string Id { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public UsageSourceKind SourceKind { get; set; }
        public string Path { get; set; } = string.Empty;
        public string? PlatformHint { get; set; }
        public string? MachineLabel { get; set; }
        public string? AccountHint { get; set; }
        public bool Enabled { get; set; } = true;

        public static PersistedSourceRoot FromSourceRoot(SourceRootRecord sourceRoot) {
            ArgumentNullException.ThrowIfNull(sourceRoot);
            return new PersistedSourceRoot {
                Id = sourceRoot.Id,
                ProviderId = sourceRoot.ProviderId,
                SourceKind = sourceRoot.SourceKind,
                Path = sourceRoot.Path,
                PlatformHint = sourceRoot.PlatformHint,
                MachineLabel = sourceRoot.MachineLabel,
                AccountHint = sourceRoot.AccountHint,
                Enabled = sourceRoot.Enabled
            };
        }

        public SourceRootRecord? ToSourceRoot() {
            if (string.IsNullOrWhiteSpace(Id)
                || string.IsNullOrWhiteSpace(ProviderId)
                || string.IsNullOrWhiteSpace(Path)) {
                return null;
            }

            var sourceRoot = new SourceRootRecord(Id, ProviderId, SourceKind, Path) {
                PlatformHint = PlatformHint,
                MachineLabel = MachineLabel,
                AccountHint = AccountHint,
                Enabled = Enabled
            };
            return sourceRoot;
        }
    }

    private sealed class PersistedUsageEvent {
        public string EventId { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public string AdapterId { get; set; } = string.Empty;
        public string SourceRootId { get; set; } = string.Empty;
        public DateTimeOffset TimestampUtc { get; set; }
        public string? ProviderAccountId { get; set; }
        public string? AccountLabel { get; set; }
        public string? PersonLabel { get; set; }
        public string? MachineId { get; set; }
        public string? SessionId { get; set; }
        public string? ThreadId { get; set; }
        public string? TurnId { get; set; }
        public string? ResponseId { get; set; }
        public string? Model { get; set; }
        public string? Surface { get; set; }
        public long? InputTokens { get; set; }
        public long? CachedInputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? ReasoningTokens { get; set; }
        public long? TotalTokens { get; set; }
        public long? DurationMs { get; set; }
        public decimal? CostUsd { get; set; }
        public UsageTruthLevel TruthLevel { get; set; }
        public string? RawHash { get; set; }

        public static PersistedUsageEvent FromUsageEvent(UsageEventRecord usageEvent) {
            return new PersistedUsageEvent {
                EventId = usageEvent.EventId,
                ProviderId = usageEvent.ProviderId,
                AdapterId = usageEvent.AdapterId,
                SourceRootId = usageEvent.SourceRootId,
                TimestampUtc = usageEvent.TimestampUtc,
                ProviderAccountId = usageEvent.ProviderAccountId,
                AccountLabel = usageEvent.AccountLabel,
                PersonLabel = usageEvent.PersonLabel,
                MachineId = usageEvent.MachineId,
                SessionId = usageEvent.SessionId,
                ThreadId = usageEvent.ThreadId,
                TurnId = usageEvent.TurnId,
                ResponseId = usageEvent.ResponseId,
                Model = usageEvent.Model,
                Surface = usageEvent.Surface,
                InputTokens = usageEvent.InputTokens,
                CachedInputTokens = usageEvent.CachedInputTokens,
                OutputTokens = usageEvent.OutputTokens,
                ReasoningTokens = usageEvent.ReasoningTokens,
                TotalTokens = usageEvent.TotalTokens,
                DurationMs = usageEvent.DurationMs,
                CostUsd = usageEvent.CostUsd,
                TruthLevel = usageEvent.TruthLevel,
                RawHash = usageEvent.RawHash
            };
        }

        public UsageEventRecord ToUsageEvent() {
            return new UsageEventRecord(EventId, ProviderId, AdapterId, SourceRootId, TimestampUtc) {
                ProviderAccountId = ProviderAccountId,
                AccountLabel = AccountLabel,
                PersonLabel = PersonLabel,
                MachineId = MachineId,
                SessionId = SessionId,
                ThreadId = ThreadId,
                TurnId = TurnId,
                ResponseId = ResponseId,
                Model = Model,
                Surface = Surface,
                InputTokens = InputTokens,
                CachedInputTokens = CachedInputTokens,
                OutputTokens = OutputTokens,
                ReasoningTokens = ReasoningTokens,
                TotalTokens = TotalTokens,
                DurationMs = DurationMs,
                CostUsd = CostUsd,
                TruthLevel = TruthLevel,
                RawHash = RawHash
            };
        }
    }
}
