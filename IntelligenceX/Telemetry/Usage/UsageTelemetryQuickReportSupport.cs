using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage;

internal static class UsageTelemetryQuickReportSupport {
    public static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static void AddAggregate(
        Dictionary<string, UsageAggregateBucket> aggregates,
        string providerId,
        string sourceRootId,
        DateTime dayUtc,
        string model,
        string surface,
        string? machineId,
        string? accountLabel,
        long inputTokens,
        long cachedInputTokens,
        long outputTokens,
        long reasoningTokens,
        long totalTokens) {
        var normalizedModel = NormalizeOptional(model) ?? "unknown-model";
        var normalizedSurface = NormalizeOptional(surface) ?? "unknown-surface";
        var key = string.Join("|", providerId, sourceRootId, dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), normalizedModel, normalizedSurface, NormalizeOptional(accountLabel) ?? string.Empty);
        if (!aggregates.TryGetValue(key, out var bucket)) {
            bucket = new UsageAggregateBucket(providerId, sourceRootId, dayUtc, normalizedModel, normalizedSurface, machineId, accountLabel);
            aggregates[key] = bucket;
        }

        bucket.InputTokens += Math.Max(0L, inputTokens);
        bucket.CachedInputTokens += Math.Max(0L, cachedInputTokens);
        bucket.OutputTokens += Math.Max(0L, outputTokens);
        bucket.ReasoningTokens += Math.Max(0L, reasoningTokens);
        bucket.TotalTokens += Math.Max(0L, totalTokens);
    }

    public static IReadOnlyList<UsageEventRecord> FinalizeAggregates(
        Dictionary<string, UsageAggregateBucket> aggregates,
        string adapterId) {
        return aggregates.Values
            .OrderBy(static value => value.DayUtc)
            .ThenBy(static value => value.Model, StringComparer.OrdinalIgnoreCase)
            .Select(bucket => new UsageEventRecord(
                "uev_" + UsageTelemetryIdentity.ComputeStableHash(
                    bucket.ProviderId + "|" +
                    bucket.SourceRootId + "|" +
                    bucket.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" +
                    bucket.Model + "|" +
                    bucket.Surface + "|" +
                    (bucket.AccountLabel ?? string.Empty)),
                bucket.ProviderId,
                adapterId,
                bucket.SourceRootId,
                new DateTimeOffset(bucket.DayUtc, TimeSpan.Zero)) {
                AccountLabel = bucket.AccountLabel,
                MachineId = bucket.MachineId,
                Model = bucket.Model,
                Surface = bucket.Surface,
                InputTokens = bucket.InputTokens,
                CachedInputTokens = bucket.CachedInputTokens,
                OutputTokens = bucket.OutputTokens,
                ReasoningTokens = bucket.ReasoningTokens,
                TotalTokens = bucket.TotalTokens,
                TruthLevel = UsageTruthLevel.Exact
            })
            .ToArray();
    }

    public static IEnumerable<string> ReadLinesShared(string filePath) {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        while (true) {
            var line = reader.ReadLine();
            if (line is null) {
                yield break;
            }

            yield return line;
        }
    }

    public static bool TryParseTimestampUtc(string? value, out DateTimeOffset timestampUtc) {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestampUtc)) {
            timestampUtc = timestampUtc.ToUniversalTime();
            return true;
        }

        timestampUtc = default;
        return false;
    }

    public static long? ReadInt64(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }

        foreach (var key in keys) {
            var value = obj.GetInt64(key);
            if (value.HasValue) {
                return value.Value;
            }

            var asDouble = obj.GetDouble(key);
            if (asDouble.HasValue) {
                return (long)Math.Round(asDouble.Value);
            }

            var asText = obj.GetString(key);
            if (long.TryParse(asText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                return parsed;
            }
        }

        return null;
    }

    public static IEnumerable<string> EnumerateFilesSafe(string directory, string searchPattern, SearchOption searchOption) {
        try {
            return Directory.EnumerateFiles(directory, searchPattern, searchOption);
        } catch {
            return Array.Empty<string>();
        }
    }

    public static IEnumerable<string> EnumerateDirectoriesSafe(string directory) {
        try {
            return Directory.EnumerateDirectories(directory);
        } catch {
            return Array.Empty<string>();
        }
    }

    public static bool IsIntegerPathSegment(string? value, int expectedLength) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = value ?? string.Empty;
        if (normalized.Length != expectedLength) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (!char.IsDigit(normalized[i])) {
                return false;
            }
        }

        return true;
    }

    public static long GetNumericPathSegment(string path) {
        return long.TryParse(
            Path.GetFileName(path),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : long.MinValue;
    }

    public static IEnumerable<string> OrderPathsByDirection<TOrder>(
        IEnumerable<string> source,
        bool descending,
        Func<string, TOrder> orderSelector) where TOrder : IComparable<TOrder> {
        return descending
            ? source.OrderByDescending(orderSelector).ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)
            : source.OrderBy(orderSelector).ThenBy(static value => value, StringComparer.OrdinalIgnoreCase);
    }

    public static DateTime GetLastWriteTimeUtcSafe(string path) {
        try {
            return File.GetLastWriteTimeUtc(path);
        } catch {
            return DateTime.MinValue;
        }
    }

    internal sealed class UsageAggregateBucket {
        public UsageAggregateBucket(
            string providerId,
            string sourceRootId,
            DateTime dayUtc,
            string model,
            string surface,
            string? machineId,
            string? accountLabel) {
            ProviderId = providerId;
            SourceRootId = sourceRootId;
            DayUtc = dayUtc;
            Model = model;
            Surface = surface;
            MachineId = machineId;
            AccountLabel = accountLabel;
        }

        public string ProviderId { get; }
        public string SourceRootId { get; }
        public DateTime DayUtc { get; }
        public string Model { get; }
        public string Surface { get; }
        public string? MachineId { get; }
        public string? AccountLabel { get; }
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long ReasoningTokens { get; set; }
        public long TotalTokens { get; set; }
    }
}
