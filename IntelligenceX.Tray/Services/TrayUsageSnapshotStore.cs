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

            var sourceRoots = persisted.SourceRoots
                .Select(static item => item.ToSourceRoot())
                .Where(static item => item is not null)
                .Cast<SourceRootRecord>()
                .ToList();
            var events = persisted.Events.Select(static item => item.ToUsageEvent()).ToList();
            var rawEvents = persisted.RawEvents.Count > 0
                ? persisted.RawEvents.Select(static item => item.ToUsageEvent()).ToList()
                : events;
            var discoveredProviderIds = persisted.DiscoveredProviderIds
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new TrayUsageSnapshotCache(
                persisted.ScannedAtUtc,
                discoveredProviderIds,
                sourceRoots,
                events,
                rawEvents,
                persisted.Health?.ToSnapshotHealth()
                ?? BuildFallbackHealth(sourceRoots, events, discoveredProviderIds));
        } catch {
            return null;
        }
    }

    public void Save(
        DateTimeOffset scannedAtUtc,
        IReadOnlyList<UsageEventRecord> events,
        IReadOnlyList<string>? discoveredProviderIds = null,
        IReadOnlyList<SourceRootRecord>? sourceRoots = null,
        UsageTelemetrySnapshotHealth? health = null,
        IReadOnlyList<UsageEventRecord>? rawEvents = null) {
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
            Events = events.Select(static item => PersistedUsageEvent.FromUsageEvent(item)).ToList(),
            RawEvents = (rawEvents ?? events).Select(static item => PersistedUsageEvent.FromUsageEvent(item)).ToList(),
            Health = health is null ? null : PersistedSnapshotHealth.FromSnapshotHealth(health)
        };
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        File.WriteAllText(SnapshotPath, json);
    }

    public sealed record TrayUsageSnapshotCache(
        DateTimeOffset ScannedAtUtc,
        List<string> DiscoveredProviderIds,
        List<SourceRootRecord> SourceRoots,
        List<UsageEventRecord> Events,
        List<UsageEventRecord> RawEvents,
        UsageTelemetrySnapshotHealth? Health);

    private sealed class PersistedUsageSnapshot {
        public DateTimeOffset ScannedAtUtc { get; set; }
        public List<string> DiscoveredProviderIds { get; set; } = [];
        public List<PersistedSourceRoot> SourceRoots { get; set; } = [];
        public List<PersistedUsageEvent> Events { get; set; } = [];
        public List<PersistedUsageEvent> RawEvents { get; set; } = [];
        public PersistedSnapshotHealth? Health { get; set; }
    }

    private static UsageTelemetrySnapshotHealth BuildFallbackHealth(
        IReadOnlyList<SourceRootRecord> sourceRoots,
        IReadOnlyList<UsageEventRecord> events,
        IReadOnlyList<string> discoveredProviderIds) {
        var providerIds = discoveredProviderIds
            .Concat(sourceRoots.Select(static root => root.ProviderId))
            .Concat(events.Select(static item => item.ProviderId))
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var latestEventUtc = events.Count == 0
            ? (DateTimeOffset?)null
            : events.Max(static item => item.TimestampUtc);
        var providerHealth = providerIds
            .Select(providerId => {
                var providerRoots = sourceRoots
                    .Where(root => string.Equals(root.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var providerEvents = events
                    .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var accountLabels = providerEvents
                    .SelectMany(static item => new[] { item.AccountLabel, item.ProviderAccountId })
                    .Concat(providerRoots.Select(static root => root.AccountHint))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var latestProviderEventUtc = providerEvents.Length == 0
                    ? (DateTimeOffset?)null
                    : providerEvents.Max(static item => item.TimestampUtc);
                return new UsageTelemetryProviderHealth(
                    providerId,
                    providerRoots.Length,
                    accountLabels,
                    providerEvents.Length,
                    parsedArtifacts: 0,
                    reusedArtifacts: 0,
                    duplicateRecordsCollapsed: 0,
                    latestProviderEventUtc,
                    isPartialScan: false);
            })
            .ToList();
        var allAccounts = providerHealth
            .SelectMany(static item => item.AccountLabels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UsageTelemetrySnapshotHealth(
            isCachedSnapshot: true,
            isPartialScan: false,
            providerIds.Length,
            sourceRoots.Count,
            allAccounts,
            events.Count,
            parsedArtifacts: 0,
            reusedArtifacts: 0,
            duplicateRecordsCollapsed: 0,
            latestEventUtc,
            issueCount: 0,
            providerHealth);
    }

    private sealed class PersistedSnapshotHealth {
        public bool IsCachedSnapshot { get; set; }
        public bool IsPartialScan { get; set; }
        public int ProviderCount { get; set; }
        public int RootsCount { get; set; }
        public List<string> AccountLabels { get; set; } = [];
        public int EventsCount { get; set; }
        public int ParsedArtifacts { get; set; }
        public int ReusedArtifacts { get; set; }
        public int DuplicateRecordsCollapsed { get; set; }
        public DateTimeOffset? LatestEventUtc { get; set; }
        public int IssueCount { get; set; }
        public List<PersistedProviderSnapshotHealth> ProviderHealth { get; set; } = [];

        public static PersistedSnapshotHealth FromSnapshotHealth(UsageTelemetrySnapshotHealth health) {
            return new PersistedSnapshotHealth {
                IsCachedSnapshot = health.IsCachedSnapshot,
                IsPartialScan = health.IsPartialScan,
                ProviderCount = health.ProviderCount,
                RootsCount = health.RootsCount,
                AccountLabels = health.AccountLabels.ToList(),
                EventsCount = health.EventsCount,
                ParsedArtifacts = health.ParsedArtifacts,
                ReusedArtifacts = health.ReusedArtifacts,
                DuplicateRecordsCollapsed = health.DuplicateRecordsCollapsed,
                LatestEventUtc = health.LatestEventUtc,
                IssueCount = health.IssueCount,
                ProviderHealth = health.ProviderHealth
                    .Select(static item => PersistedProviderSnapshotHealth.FromProviderHealth(item))
                    .ToList()
            };
        }

        public UsageTelemetrySnapshotHealth ToSnapshotHealth() {
            return new UsageTelemetrySnapshotHealth(
                IsCachedSnapshot,
                IsPartialScan,
                ProviderCount,
                RootsCount,
                AccountLabels,
                EventsCount,
                ParsedArtifacts,
                ReusedArtifacts,
                DuplicateRecordsCollapsed,
                LatestEventUtc,
                IssueCount,
                ProviderHealth.Select(static item => item.ToProviderHealth()).ToList());
        }
    }

    private sealed class PersistedProviderSnapshotHealth {
        public string ProviderId { get; set; } = string.Empty;
        public int RootsCount { get; set; }
        public List<string> AccountLabels { get; set; } = [];
        public int EventsCount { get; set; }
        public int ParsedArtifacts { get; set; }
        public int ReusedArtifacts { get; set; }
        public int DuplicateRecordsCollapsed { get; set; }
        public DateTimeOffset? LatestEventUtc { get; set; }
        public bool IsPartialScan { get; set; }

        public static PersistedProviderSnapshotHealth FromProviderHealth(UsageTelemetryProviderHealth health) {
            return new PersistedProviderSnapshotHealth {
                ProviderId = health.ProviderId,
                RootsCount = health.RootsCount,
                AccountLabels = health.AccountLabels.ToList(),
                EventsCount = health.EventsCount,
                ParsedArtifacts = health.ParsedArtifacts,
                ReusedArtifacts = health.ReusedArtifacts,
                DuplicateRecordsCollapsed = health.DuplicateRecordsCollapsed,
                LatestEventUtc = health.LatestEventUtc,
                IsPartialScan = health.IsPartialScan
            };
        }

        public UsageTelemetryProviderHealth ToProviderHealth() {
            return new UsageTelemetryProviderHealth(
                ProviderId,
                RootsCount,
                AccountLabels,
                EventsCount,
                ParsedArtifacts,
                ReusedArtifacts,
                DuplicateRecordsCollapsed,
                LatestEventUtc,
                IsPartialScan);
        }
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
        public string? ConversationTitle { get; set; }
        public string? WorkspacePath { get; set; }
        public string? RepositoryName { get; set; }
        public string? TurnId { get; set; }
        public string? ResponseId { get; set; }
        public string? Model { get; set; }
        public string? Surface { get; set; }
        public long? InputTokens { get; set; }
        public long? CachedInputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? ReasoningTokens { get; set; }
        public long? TotalTokens { get; set; }
        public int? CompactCount { get; set; }
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
                ConversationTitle = usageEvent.ConversationTitle,
                WorkspacePath = usageEvent.WorkspacePath,
                RepositoryName = usageEvent.RepositoryName,
                TurnId = usageEvent.TurnId,
                ResponseId = usageEvent.ResponseId,
                Model = usageEvent.Model,
                Surface = usageEvent.Surface,
                InputTokens = usageEvent.InputTokens,
                CachedInputTokens = usageEvent.CachedInputTokens,
                OutputTokens = usageEvent.OutputTokens,
                ReasoningTokens = usageEvent.ReasoningTokens,
                TotalTokens = usageEvent.TotalTokens,
                CompactCount = usageEvent.CompactCount,
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
                ConversationTitle = ConversationTitle,
                WorkspacePath = WorkspacePath,
                RepositoryName = RepositoryName,
                TurnId = TurnId,
                ResponseId = ResponseId,
                Model = Model,
                Surface = Surface,
                InputTokens = InputTokens,
                CachedInputTokens = CachedInputTokens,
                OutputTokens = OutputTokens,
                ReasoningTokens = ReasoningTokens,
                TotalTokens = TotalTokens,
                CompactCount = CompactCount,
                DurationMs = DurationMs,
                CostUsd = CostUsd,
                TruthLevel = TruthLevel,
                RawHash = RawHash
            };
        }
    }
}
