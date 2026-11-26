using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>An exclusive, expiring reservation independent of task requests and Goals.</summary>
public sealed record Lease(string Key, string Holder, string Machine, string Purpose, string LeaseId, DateTimeOffset ExpiresAtUtc) {
    public DateTimeOffset? AcquiredAtUtc { get; init; }
    public string? PreviousHolder { get; init; }
    public DateTimeOffset? TakenOverAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? BudgetObservedAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool TtlAdjustedToPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PreviousLeaseId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeaseHandoff? Handoff { get; init; }
    /// <summary>A TTL above policy that an operator approved for this one reservation, renewals included.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ApprovedTtlSeconds { get; init; }
}
/// <summary>An operator's answer to a reservation waiting in needs-approval. TtlSeconds 0 approves the requested TTL.</summary>
public sealed record LeaseApproval(string LeaseId, bool Approve, int TtlSeconds = 0);
public sealed record PendingLeaseApproval(Lease Lease, int RequestedTtlSeconds, int MaximumTtlSeconds);
public sealed record LeaseApprovalList(PendingLeaseApproval[] Pending);
public sealed record LeaseHandoff(string Commit, string NextStep, string? PullRequest = null);
public sealed record LeaseAcquire(string Key, string Holder, string Machine, string Purpose, int TtlSeconds,
    string? TransferLeaseId = null, LeaseHandoff? Handoff = null);
public sealed record LeaseRenew(string Key, string LeaseId, int TtlSeconds);
public sealed record LeaseRelease(string Key, string LeaseId);
public sealed record LeaseReply(bool Ok, string State, Lease? Lease = null) {
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? QueuePosition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? EstimatedWaitSeconds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? HolderLastActivityAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? TakeoverAtUtc { get; init; }
    /// <summary>The longest TTL policy allows for this key, sent with a ttl-exceeds-policy reply.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MaximumTtlSeconds { get; init; }
}
public sealed record LeaseQuery(string Query, Lease[] Leases, int Total);
public sealed record MachineStatus(string Machine, string Reporter, int LiveSessions, int LimitedSessions, Lease[] Leases, int TotalLeases) {
    /// <summary>Optional machine-fact source availability, independent of leases and session observations.</summary>
    public ProbeAvailability Probe { get; init; } = new("unavailable", "not-configured");
    public MachineResourceStatus? Resources { get; set; }
    public MachineSession[] Sessions { get; set; } = [];
}
public sealed record MachineResourceStatus(int BuildSlots, int TestSlots, int Builds, int Tests, int Queued,
    string CanStartHeavy, string Reason, bool SustainedHighCpu) {
    public int AgentSlots { get; init; }
    public int LiveAgents { get; init; }
}
public sealed record MachineSession(string Id, string Provider, string? Title, string Origin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FreshAdvice = null);
public sealed record ProbeAvailability(string State, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null) {
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? ObservedAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? SampledAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? CpuUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? LoadAverage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MemoryPressure { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MemoryUsedBytes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MemoryTotalBytes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? SwapUsedBytes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProbeVolumeView[]? Volumes { get; init; }
    public bool VolumesCapped { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RunnerBusy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RunnerListening { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? PausedUntilUtc { get; init; }
}
public sealed record ProbeVolumeView(string Mount, long FreeBytes, long TotalBytes);
/// <summary>Limits are qualified to observed sessions. Unmapped accounts never imply an account-wide balance.</summary>
public sealed record BudgetLimit(string Machine, string Provider, string? Account, int Sessions);
public sealed record BudgetStatus(BudgetLimit[] Limits, int TotalGroups, string Coverage = "observed-session-limits;credits-and-reset-unknown") {
    public Dictionary<string, string?> RoutingSuggestions { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The Ledger host's own accounts as their clients report them; other machines' accounts aren't reported yet.</summary>
    public AccountLimit[] Accounts { get; init; } = [];
}

/// <summary>
/// One provider account's usage: how much of its window is used, when it resets and what credit is left, as the
/// provider's own client reports it. Null fields are ones the provider didn't report.
/// </summary>
public sealed record AccountLimit(string Provider, string Source, DateTimeOffset ObservedAtUtc) {
    /// <summary>Local identity used to qualify the observation. Budget readers receive the usage figures only.</summary>
    [JsonIgnore]
    public string? AccountId { get; init; }
    [JsonIgnore] public DateTimeOffset? WindowObservedAtUtc { get; init; }
    [JsonIgnore] public DateTimeOffset? ShortWindowObservedAtUtc { get; init; }
    [JsonIgnore] public DateTimeOffset? AvailableAtUtc { get; init; }
    public string? Machine { get; init; }
    public string? Plan { get; init; }
    public double? UsedPercent { get; init; }
    public int? WindowMinutes { get; init; }
    public DateTimeOffset? ResetsAtUtc { get; init; }
    public double? ShortUsedPercent { get; init; }
    public DateTimeOffset? ShortResetsAtUtc { get; init; }
    public bool? HasCredits { get; init; }
    public string? CreditBalance { get; init; }
    public int? FreeResets { get; init; }
    public bool? LimitReached { get; init; }
    public string? Note { get; init; }
}

/// <summary>Separate local write configuration for lease tools. Read credentials stay read-only.</summary>
public sealed record LeaseClientSettings {
    public required string Endpoint { get; init; }
    public required string ReadToken { get; init; }
    public required string WriteToken { get; init; }
    public string? CertificateSha256 { get; init; }
    /// <inheritdoc cref="LedgerClientSettings.Fallback"/>
    public string? Fallback { get; init; }

    public static LeaseClientSettings Load(string path) {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Lease settings exceed their bound.");
        var value = JsonSerializer.Deserialize(File.ReadAllText(path), LedgerReadJson.Default.LeaseClientSettings)
            ?? throw new InvalidDataException("Missing lease settings.");
        value.ReadSettings().Validate();
        if (value.WriteToken is null || value.WriteToken.Length < 32 || value.WriteToken.Any(char.IsWhiteSpace) || value.WriteToken == value.ReadToken)
            throw new InvalidDataException("Separate lease write credentials are required.");
        return value;
    }

    internal LedgerClientSettings ReadSettings() => new() { Endpoint = Endpoint, ReadToken = ReadToken, CertificateSha256 = CertificateSha256, Fallback = Fallback };
}

/// <summary>Latest reporter liveness and coverage, stored as one row rather than journal entries.</summary>
public sealed record ReporterHealth(string MachineId, string ReporterId, string SourceVersion, DateTimeOffset ObservedAtUtc, string[] Coverage,
    ReporterProgress[]? Progress = null, ProbeAvailability? Probe = null) {
    /// <summary>GitHub REST use of the reporter and of the local components that left a tally for it to forward.</summary>
    public GitHubUsageReport[]? GitHubUsage { get; init; }
    /// <summary>Sign-in and permission facts for GitHub, Codex and Claude on the reporter's machine.</summary>
    public AuthFact[]? Auth { get; init; }
    /// <summary>The machine's latest checkout scan. Sent only after a new scan, so most health reports leave it out.</summary>
    public CheckoutCensus? Checkouts { get; init; }
}

/// <summary>Collection cadence comes from the same revisioned policy used by the service.</summary>
public sealed record ReporterHealthReceipt(bool Ok, long PolicyRevision = 0, SessionPolicy? Session = null);
/// <summary>Latest activity for an exact open turn, delivered with reporter health without journal fan-out.</summary>
public sealed record ReporterProgress(string ItemId, string TurnId, DateTimeOffset LastActivityAtUtc);
