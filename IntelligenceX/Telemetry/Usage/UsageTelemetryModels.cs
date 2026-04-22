using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Describes the confidence level of a usage value.
/// </summary>
public enum UsageTruthLevel {
    /// <summary>
    /// No reliable confidence information is available.
    /// </summary>
    Unknown,

    /// <summary>
    /// The value is estimated from indirect signals.
    /// </summary>
    Estimated,

    /// <summary>
    /// The value is inferred from partial provider data.
    /// </summary>
    Inferred,

    /// <summary>
    /// The value comes from authoritative token or cost telemetry.
    /// </summary>
    Exact,
}

/// <summary>
/// Identifies how usage was obtained.
/// </summary>
public enum UsageSourceKind {
    /// <summary>
    /// Usage was imported from local files on the current machine.
    /// </summary>
    LocalLogs,

    /// <summary>
    /// Usage was imported from a recovered or migrated folder.
    /// </summary>
    RecoveredFolder,

    /// <summary>
    /// Usage was collected by probing a CLI or local command.
    /// </summary>
    CliProbe,

    /// <summary>
    /// Usage was imported from an authenticated OAuth-backed API.
    /// </summary>
    OAuthApi,

    /// <summary>
    /// Usage was imported from an authenticated web session.
    /// </summary>
    WebSession,

    /// <summary>
    /// Usage was imported from an OpenAI-compatible or Anthropic-compatible API.
    /// </summary>
    CompatibleApi,

    /// <summary>
    /// Usage was emitted by an IntelligenceX-owned feature.
    /// </summary>
    InternalIx,
}

/// <summary>
/// Defines a root location or endpoint used to discover usage.
/// </summary>
public sealed class SourceRootRecord {
    /// <summary>
    /// Initializes a new source-root record.
    /// </summary>
    public SourceRootRecord(
        string id,
        string providerId,
        UsageSourceKind sourceKind,
        string path) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Source root id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(providerId)) {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        Id = id.Trim();
        ProviderId = providerId.Trim();
        SourceKind = sourceKind;
        Path = UsageTelemetryIdentity.NormalizePath(path);
        Enabled = true;
    }

    /// <summary>
    /// Gets the stable source-root identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the provider identifier.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the source kind.
    /// </summary>
    public UsageSourceKind SourceKind { get; }

    /// <summary>
    /// Gets the normalized path or logical locator.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets or sets the optional platform hint.
    /// </summary>
    public string? PlatformHint { get; set; }

    /// <summary>
    /// Gets or sets the optional machine label.
    /// </summary>
    public string? MachineLabel { get; set; }

    /// <summary>
    /// Gets or sets the optional account hint.
    /// </summary>
    public string? AccountHint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this root is enabled for discovery.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Creates a deterministic source-root identifier from provider, source kind, and normalized path.
    /// </summary>
    public static string CreateStableId(string providerId, UsageSourceKind sourceKind, string path) {
        if (string.IsNullOrWhiteSpace(providerId)) {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var normalized = providerId.Trim().ToLowerInvariant() + "|" + sourceKind + "|" +
                         UsageTelemetryIdentity.NormalizePath(path).ToLowerInvariant();
        return "src_" + UsageTelemetryIdentity.ComputeStableHash(normalized, 12);
    }
}

/// <summary>
/// Describes a raw artifact observed during import.
/// </summary>
public sealed class RawArtifactDescriptor {
    /// <summary>
    /// Initializes a new raw-artifact descriptor.
    /// </summary>
    public RawArtifactDescriptor(string sourceRootId, string adapterId, string path, string fingerprint) {
        if (string.IsNullOrWhiteSpace(sourceRootId)) {
            throw new ArgumentException("Source root id is required.", nameof(sourceRootId));
        }
        if (string.IsNullOrWhiteSpace(adapterId)) {
            throw new ArgumentException("Adapter id is required.", nameof(adapterId));
        }
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Artifact path is required.", nameof(path));
        }
        if (string.IsNullOrWhiteSpace(fingerprint)) {
            throw new ArgumentException("Artifact fingerprint is required.", nameof(fingerprint));
        }

        SourceRootId = sourceRootId.Trim();
        AdapterId = adapterId.Trim();
        Path = UsageTelemetryIdentity.NormalizePath(path);
        Fingerprint = fingerprint.Trim();
    }

    /// <summary>
    /// Gets the source root that produced this artifact.
    /// </summary>
    public string SourceRootId { get; }

    /// <summary>
    /// Gets the adapter that produced this artifact.
    /// </summary>
    public string AdapterId { get; }

    /// <summary>
    /// Gets the normalized artifact path or logical locator.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the stable artifact fingerprint used for re-import and provenance.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Gets or sets the parser version that produced this descriptor.
    /// </summary>
    public string? ParserVersion { get; set; }

    /// <summary>
    /// Gets or sets the artifact size in bytes when available.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the last-write timestamp in UTC when available.
    /// </summary>
    public DateTimeOffset? LastWriteTimeUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the artifact was imported.
    /// </summary>
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the number of bytes parsed from this artifact when incremental scanners track offsets.
    /// </summary>
    public long? ParsedBytes { get; set; }

    /// <summary>
    /// Gets or sets optional scanner-specific cached state for quick report generation.
    /// </summary>
    public string? StateJson { get; set; }

    /// <summary>
    /// Creates a file-backed raw-artifact descriptor using file metadata as the incremental fingerprint.
    /// </summary>
    public static RawArtifactDescriptor CreateFile(
        string sourceRootId,
        string adapterId,
        string path,
        string? parserVersion = null,
        DateTimeOffset? importedAtUtc = null) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Artifact path is required.", nameof(path));
        }

        var normalizedPath = UsageTelemetryIdentity.NormalizePath(path);
        var fileInfo = new FileInfo(normalizedPath);
        var sizeBytes = fileInfo.Exists ? fileInfo.Length : 0L;
        var lastWriteUtc = fileInfo.Exists
            ? new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        var fingerprint = UsageTelemetryIdentity.ComputeStableHash(
            normalizedPath + "|" +
            sizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
            lastWriteUtc.UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new RawArtifactDescriptor(sourceRootId, adapterId, normalizedPath, fingerprint) {
            ParserVersion = NormalizeOptional(parserVersion),
            SizeBytes = sizeBytes,
            LastWriteTimeUtc = fileInfo.Exists ? lastWriteUtc : null,
            ImportedAtUtc = importedAtUtc ?? DateTimeOffset.UtcNow
        };
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Canonical usage event written to the telemetry ledger.
/// </summary>
public sealed class UsageEventRecord {
    /// <summary>
    /// Initializes a new usage event record.
    /// </summary>
    public UsageEventRecord(
        string eventId,
        string providerId,
        string adapterId,
        string sourceRootId,
        DateTimeOffset timestampUtc) {
        if (string.IsNullOrWhiteSpace(eventId)) {
            throw new ArgumentException("Event id is required.", nameof(eventId));
        }
        if (string.IsNullOrWhiteSpace(providerId)) {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }
        if (string.IsNullOrWhiteSpace(adapterId)) {
            throw new ArgumentException("Adapter id is required.", nameof(adapterId));
        }
        if (string.IsNullOrWhiteSpace(sourceRootId)) {
            throw new ArgumentException("Source root id is required.", nameof(sourceRootId));
        }

        EventId = eventId.Trim();
        ProviderId = providerId.Trim();
        AdapterId = adapterId.Trim();
        SourceRootId = sourceRootId.Trim();
        TimestampUtc = timestampUtc;
    }

    /// <summary>
    /// Gets the stable event identifier supplied by the importer.
    /// </summary>
    public string EventId { get; }

    /// <summary>
    /// Gets the stable provider identifier.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the stable adapter identifier.
    /// </summary>
    public string AdapterId { get; }

    /// <summary>
    /// Gets the source root that produced the event.
    /// </summary>
    public string SourceRootId { get; }

    /// <summary>
    /// Gets or sets the provider-owned account identifier when available.
    /// </summary>
    public string? ProviderAccountId { get; set; }

    /// <summary>
    /// Gets or sets the provider-specific account label.
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// Gets or sets the optional person-level grouping label used to merge several provider accounts.
    /// </summary>
    public string? PersonLabel { get; set; }

    /// <summary>
    /// Gets or sets the machine identifier or label associated with the event.
    /// </summary>
    public string? MachineId { get; set; }

    /// <summary>
    /// Gets or sets the provider session identifier when available.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the provider thread or conversation identifier when available.
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// Gets or sets the provider conversation title when available.
    /// </summary>
    public string? ConversationTitle { get; set; }

    /// <summary>
    /// Gets or sets the workspace path associated with the conversation when available.
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// Gets or sets the repository name associated with the conversation when available.
    /// </summary>
    public string? RepositoryName { get; set; }

    /// <summary>
    /// Gets or sets the provider turn identifier when available.
    /// </summary>
    public string? TurnId { get; set; }

    /// <summary>
    /// Gets or sets the provider response identifier when available.
    /// </summary>
    public string? ResponseId { get; set; }

    /// <summary>
    /// Gets the UTC timestamp associated with the event.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>
    /// Gets or sets the model name associated with the event.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the provider surface such as CLI, web, or desktop app.
    /// </summary>
    public string? Surface { get; set; }

    /// <summary>
    /// Gets or sets the input token count.
    /// </summary>
    public long? InputTokens { get; set; }

    /// <summary>
    /// Gets or sets the cached input token count.
    /// </summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>
    /// Gets or sets the output token count.
    /// </summary>
    public long? OutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the reasoning token count when exposed by the provider.
    /// </summary>
    public long? ReasoningTokens { get; set; }

    /// <summary>
    /// Gets or sets the total token count.
    /// </summary>
    public long? TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of context compactions associated with this event.
    /// </summary>
    public int? CompactCount { get; set; }

    /// <summary>
    /// Gets or sets the elapsed duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the cost in USD when known.
    /// </summary>
    public decimal? CostUsd { get; set; }

    /// <summary>
    /// Gets or sets the confidence level for this event's quantitative values.
    /// </summary>
    public UsageTruthLevel TruthLevel { get; set; } = UsageTruthLevel.Unknown;

    /// <summary>
    /// Gets or sets the raw artifact hash used as the weakest dedupe fallback.
    /// </summary>
    public string? RawHash { get; set; }

    /// <summary>
    /// Returns dedupe keys in priority order for this event.
    /// </summary>
    public IReadOnlyList<string> GetDeduplicationKeys() {
        return UsageTelemetryIdentity.BuildDeduplicationKeys(this);
    }
}

/// <summary>
/// Manual account-binding rule used to normalize imported telemetry into stable identities.
/// </summary>
public sealed class UsageAccountBindingRecord {
    /// <summary>
    /// Initializes a new account-binding record.
    /// </summary>
    public UsageAccountBindingRecord(string id, string providerId) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Binding id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(providerId)) {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }

        Id = id.Trim();
        ProviderId = providerId.Trim();
        Enabled = true;
    }

    /// <summary>
    /// Gets the stable binding identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the provider identifier matched by this binding.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets or sets the optional source-root identifier that must match.
    /// </summary>
    public string? SourceRootId { get; set; }

    /// <summary>
    /// Gets or sets the optional provider account identifier that must match.
    /// </summary>
    public string? MatchProviderAccountId { get; set; }

    /// <summary>
    /// Gets or sets the optional raw account label that must match.
    /// </summary>
    public string? MatchAccountLabel { get; set; }

    /// <summary>
    /// Gets or sets the canonical provider account identifier to apply when the binding matches.
    /// </summary>
    public string? ProviderAccountId { get; set; }

    /// <summary>
    /// Gets or sets the canonical account label to apply when the binding matches.
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// Gets or sets the person-level label to apply when the binding matches.
    /// </summary>
    public string? PersonLabel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this binding is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Creates a deterministic account-binding identifier from provider and matching fields.
    /// </summary>
    public static string CreateStableId(
        string providerId,
        string? sourceRootId = null,
        string? matchProviderAccountId = null,
        string? matchAccountLabel = null) {
        if (string.IsNullOrWhiteSpace(providerId)) {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }

        var normalized = string.Join(
            "|",
            providerId.Trim().ToLowerInvariant(),
            NormalizeOptional(sourceRootId)?.ToLowerInvariant() ?? string.Empty,
            NormalizeOptional(matchProviderAccountId)?.ToLowerInvariant() ?? string.Empty,
            NormalizeOptional(matchAccountLabel)?.ToLowerInvariant() ?? string.Empty);
        return "acct_" + UsageTelemetryIdentity.ComputeStableHash(normalized, 12);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Utility helpers for telemetry identity, normalization, and dedupe keys.
/// </summary>
public static class UsageTelemetryIdentity {
    /// <summary>
    /// Normalizes a filesystem path or logical locator.
    /// </summary>
    public static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var trimmed = path.Trim();
        if (trimmed.IndexOf("://", StringComparison.Ordinal) >= 0) {
            return trimmed;
        }
        if (LooksLikeWindowsAbsolutePath(trimmed)) {
            return TrimTrailingDirectorySeparators(trimmed.Replace('/', '\\'));
        }

        var normalized = trimmed;
        try {
            normalized = Path.GetFullPath(trimmed);
        } catch {
            // Keep the trimmed input when the locator is not a local filesystem path.
        }

        return TrimTrailingDirectorySeparators(normalized);
    }

    private static bool LooksLikeWindowsAbsolutePath(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        return (value.Length >= 3
                && char.IsLetter(value[0])
                && value[1] == ':'
                && (value[2] == '\\' || value[2] == '/'))
               || value.StartsWith(@"\\", StringComparison.Ordinal)
               || value.StartsWith("//", StringComparison.Ordinal);
    }

    private static string TrimTrailingDirectorySeparators(string value) {
        if (string.IsNullOrEmpty(value)) {
            return value;
        }

        var rootLength = GetRootLength(value);
        var end = value.Length;
        while (end > rootLength && (value[end - 1] == '\\' || value[end - 1] == '/')) {
            end--;
        }

        return end == value.Length ? value : value.Substring(0, end);
    }

    private static int GetRootLength(string value) {
        if (string.IsNullOrEmpty(value)) {
            return 0;
        }

        if (value.Length >= 3
            && char.IsLetter(value[0])
            && value[1] == ':'
            && (value[2] == '\\' || value[2] == '/')) {
            return 3;
        }

        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal)) {
            return 2;
        }

        return value[0] == '/' || value[0] == '\\' ? 1 : 0;
    }

    /// <summary>
    /// Builds dedupe keys for a usage event in priority order.
    /// </summary>
    public static IReadOnlyList<string> BuildDeduplicationKeys(UsageEventRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(record.ProviderAccountId) &&
            !string.IsNullOrWhiteSpace(record.SessionId) &&
            !string.IsNullOrWhiteSpace(record.TurnId)) {
            keys.Add("acct-session-turn|" + record.ProviderId + "|" + record.ProviderAccountId + "|" + record.SessionId + "|" + record.TurnId);
        }
        if (!string.IsNullOrWhiteSpace(record.ResponseId)) {
            keys.Add("response|" + record.ProviderId + "|" + record.ResponseId);
        }
        if (!string.IsNullOrWhiteSpace(record.RawHash)) {
            keys.Add("raw|" + record.ProviderId + "|" + record.RawHash);
        }

        if (keys.Count == 0) {
            keys.Add("event|" + record.ProviderId + "|" + record.EventId);
        }

        return keys;
    }

    /// <summary>
    /// Computes a stable lowercase hexadecimal hash for a string.
    /// </summary>
    public static string ComputeStableHash(string value, int bytes = 16) {
        if (value is null) {
            throw new ArgumentNullException(nameof(value));
        }
        if (bytes <= 0 || bytes > 32) {
            throw new ArgumentOutOfRangeException(nameof(bytes), "bytes must be between 1 and 32.");
        }

        using (var sha = SHA256.Create()) {
            var input = Encoding.UTF8.GetBytes(value);
            var digest = sha.ComputeHash(input);
            var builder = new StringBuilder(bytes * 2);
            for (var i = 0; i < bytes; i++) {
                builder.Append(digest[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
