using System;
using IntelligenceX.OpenAI;
using IntelligenceX.Telemetry;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Records IntelligenceX-owned chat turns into the provider-neutral usage ledger.
/// </summary>
public sealed class InternalIxUsageRecorder : IDisposable {
    /// <summary>
    /// Stable provider identifier used for IntelligenceX-owned usage.
    /// </summary>
    public const string StableProviderId = "ix";

    /// <summary>
    /// Stable adapter identifier used for client turn-completed events.
    /// </summary>
    public const string StableAdapterId = "ix.client-turn";

    /// <summary>
    /// Default surface used when no explicit telemetry label is provided.
    /// </summary>
    public const string DefaultSurface = "chat";

    private readonly IntelligenceXClient _client;
    private readonly IUsageEventStore _usageEventStore;
    private readonly SourceRootRecord _sourceRoot;
    private readonly string _providerId;
    private readonly string? _providerAccountId;
    private readonly string? _accountLabel;
    private readonly string? _machineId;

    /// <summary>
    /// Initializes a new recorder and subscribes it to the supplied client.
    /// </summary>
    public InternalIxUsageRecorder(
        IntelligenceXClient client,
        ISourceRootStore sourceRootStore,
        IUsageEventStore usageEventStore,
        string? providerId = null,
        string? machineId = null,
        string? accountLabel = null,
        string? providerAccountId = null,
        string? sourcePath = null) {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (sourceRootStore is null) {
            throw new ArgumentNullException(nameof(sourceRootStore));
        }
        _usageEventStore = usageEventStore ?? throw new ArgumentNullException(nameof(usageEventStore));

        _machineId = NormalizeOptional(machineId) ?? ResolveMachineId();
        _providerId = NormalizeOptional(providerId) ?? ResolveTelemetryProviderId(_client.TransportKind);
        _accountLabel = NormalizeOptional(accountLabel);
        _providerAccountId = NormalizeOptional(providerAccountId);

        var path = NormalizeOptional(sourcePath) ?? BuildDefaultSourcePath(_providerId, _machineId);
        _sourceRoot = new SourceRootRecord(
            SourceRootRecord.CreateStableId(_providerId, UsageSourceKind.InternalIx, path),
            _providerId,
            UsageSourceKind.InternalIx,
            path) {
            MachineLabel = _machineId,
            AccountHint = _accountLabel,
        };

        sourceRootStore.Upsert(_sourceRoot);
        _client.TurnCompleted += OnTurnCompleted;
    }

    /// <summary>
    /// Gets the source root registered for this recorder.
    /// </summary>
    public SourceRootRecord SourceRoot => _sourceRoot;

    private void OnTurnCompleted(object? sender, IntelligenceXTurnCompletedEventArgs args) {
        if (args is null || args.Turn is null) {
            return;
        }

        _usageEventStore.Upsert(CreateUsageEvent(args));
    }

    private UsageEventRecord CreateUsageEvent(IntelligenceXTurnCompletedEventArgs args) {
        var turn = args.Turn!;
        var usage = turn.Usage;
        var feature = NormalizeOptional(args.Feature);
        var surface = feature ?? NormalizeOptional(args.Surface) ?? DefaultSurface;
        var responseId = NormalizeOptional(turn.ResponseId);
        var turnId = NormalizeOptional(turn.Id) ?? responseId ?? "turn";
        var threadId = NormalizeOptional(args.ThreadId) ?? "thread";
        var eventIdentity = _providerId + "|" + threadId + "|" + turnId + "|" + (responseId ?? string.Empty);
        var rawIdentity = eventIdentity + "|" + args.CompletedAtUtc.ToUniversalTime().ToString("O") + "|" + surface + "|" + args.Model;

        var record = new UsageEventRecord(
            "ev_" + UsageTelemetryIdentity.ComputeStableHash(eventIdentity, 16),
            _providerId,
            StableAdapterId,
            _sourceRoot.Id,
            args.CompletedAtUtc) {
            ProviderAccountId = _providerAccountId,
            AccountLabel = _accountLabel ?? feature,
            MachineId = _machineId,
            SessionId = threadId,
            ThreadId = threadId,
            TurnId = turnId,
            ResponseId = responseId,
            Model = NormalizeOptional(args.Model),
            Surface = surface,
            DurationMs = SafeDurationMs(args.Duration),
            RawHash = UsageTelemetryIdentity.ComputeStableHash(rawIdentity, 16),
            TruthLevel = usage is null ? UsageTruthLevel.Unknown : UsageTruthLevel.Exact,
        };

        if (usage is not null) {
            record.InputTokens = usage.InputTokens;
            record.CachedInputTokens = usage.CachedInputTokens;
            record.OutputTokens = usage.OutputTokens;
            record.ReasoningTokens = usage.ReasoningTokens;
            record.TotalTokens = usage.TotalTokens;
        }

        return record;
    }

    private static long SafeDurationMs(TimeSpan duration) {
        var milliseconds = duration.TotalMilliseconds;
        if (milliseconds <= 0) {
            return 0;
        }

        if (milliseconds >= long.MaxValue) {
            return long.MaxValue;
        }

        return (long)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
    }

    private static string BuildDefaultSourcePath(string providerId, string? machineId) {
        var machineSegment = NormalizeOptional(machineId) ?? "local";
        return providerId + "://internal/" + machineSegment;
    }

    private static string ResolveTelemetryProviderId(OpenAITransportKind transportKind) {
        return transportKind switch {
            OpenAITransportKind.CopilotCli => "copilot",
            _ => StableProviderId
        };
    }

    private static string ResolveMachineId() {
        var value = NormalizeOptional(Environment.MachineName);
        return value ?? "local";
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <inheritdoc />
    public void Dispose() {
        _client.TurnCompleted -= OnTurnCompleted;
    }
}
