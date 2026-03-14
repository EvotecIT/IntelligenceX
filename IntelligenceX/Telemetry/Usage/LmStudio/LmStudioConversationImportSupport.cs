using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.LmStudio;

internal static class LmStudioConversationImportSupport {
    public static bool IsLmStudioProvider(string? providerId) {
        var normalized = NormalizeOptional(providerId);
        return string.Equals(normalized, LmStudioConversationImport.StableProviderId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "lm-studio", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "lm studio", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (IsConversationFile(rootPath)) {
                yield return Path.GetFullPath(rootPath);
            }

            yield break;
        }

        foreach (var directory in EnumerateCandidateDirectories(rootPath)) {
            var files = Directory.EnumerateFiles(directory, "*.conversation.json", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray();
            var orderedFiles = preferRecentArtifacts
                ? files.OrderByDescending(GetLastWriteTimeUtcSafe)
                    .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

            foreach (var file in orderedFiles) {
                yield return file;
            }
        }
    }

    public static JsonObject? GetSelectedVersion(JsonObject message) {
        var versions = message.GetArray("versions");
        if (versions is null || versions.Count == 0) {
            return null;
        }

        var selectedIndex = ReadInt32(message, "currentlySelected");
        if (selectedIndex < 0 || selectedIndex >= versions.Count) {
            selectedIndex = 0;
        }

        return versions[selectedIndex].AsObject();
    }

    public static LmStudioNormalizedUsage? NormalizeUsage(JsonObject? stats) {
        if (stats is null) {
            return null;
        }

        var inputTokens = ReadInt64(stats, "promptTokensCount") ?? 0L;
        var outputTokens = ReadInt64(stats, "predictedTokensCount") ?? 0L;
        var totalTokens = ReadInt64(stats, "totalTokensCount") ?? Math.Max(0L, inputTokens + outputTokens);
        var totalTimeSec = ReadDouble(stats, "totalTimeSec");
        var durationMs = totalTimeSec.HasValue
            ? Math.Max(0L, (long)Math.Round(totalTimeSec.Value * 1000d, MidpointRounding.AwayFromZero))
            : (long?)null;

        return new LmStudioNormalizedUsage(
            InputTokens: Math.Max(0L, inputTokens),
            OutputTokens: Math.Max(0L, outputTokens),
            TotalTokens: Math.Max(0L, totalTokens),
            DurationMs: durationMs);
    }

    public static bool TryParseStepTimestampUtc(string stepIdentifier, out DateTimeOffset timestampUtc) {
        var normalized = NormalizeOptional(stepIdentifier);
        if (!string.IsNullOrWhiteSpace(normalized)) {
            var normalizedValue = normalized!;
            var separator = normalizedValue.IndexOf('-');
            var prefix = separator >= 0 ? normalizedValue.Substring(0, separator) : normalizedValue;
            if (long.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds)) {
                try {
                    timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToUniversalTime();
                    return true;
                } catch (ArgumentOutOfRangeException) {
                    // Ignore malformed timestamps and fall back to file/conversation timestamps.
                }
            }
        }

        timestampUtc = default(DateTimeOffset);
        return false;
    }

    public static DateTimeOffset? TryReadUnixTimeMilliseconds(JsonObject obj, string key) {
        var value = ReadInt64(obj, key);
        if (!value.HasValue) {
            return null;
        }

        try {
            return DateTimeOffset.FromUnixTimeMilliseconds(value.Value).ToUniversalTime();
        } catch (ArgumentOutOfRangeException) {
            return null;
        }
    }

    public static string? ExtractSessionId(string filePath) {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName)) {
            return null;
        }

        const string suffix = ".conversation.json";
        if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
            return fileName.Substring(0, fileName.Length - suffix.Length);
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    public static long? ReadInt64(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }

        for (var i = 0; i < keys.Length; i++) {
            var value = obj.GetInt64(keys[i]);
            if (value.HasValue) {
                return value.Value;
            }

            var asDouble = obj.GetDouble(keys[i]);
            if (asDouble.HasValue) {
                return (long)Math.Round(asDouble.Value, MidpointRounding.AwayFromZero);
            }

            var asText = obj.GetString(keys[i]);
            if (long.TryParse(asText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                return parsed;
            }
        }

        return null;
    }

    public static int ReadInt32(JsonObject? obj, params string[] keys) {
        var value = ReadInt64(obj, keys);
        if (!value.HasValue || value.Value < int.MinValue || value.Value > int.MaxValue) {
            return 0;
        }

        return (int)value.Value;
    }

    public static double? ReadDouble(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }

        for (var i = 0; i < keys.Length; i++) {
            var value = obj.GetDouble(keys[i]);
            if (value.HasValue) {
                return value.Value;
            }

            var asInt64 = obj.GetInt64(keys[i]);
            if (asInt64.HasValue) {
                return asInt64.Value;
            }

            var asText = obj.GetString(keys[i]);
            if (double.TryParse(asText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)) {
                return parsed;
            }
        }

        return null;
    }

    public static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static DateTime GetLastWriteTimeUtcSafe(string path) {
        try {
            return File.GetLastWriteTimeUtc(path);
        } catch {
            return DateTime.MinValue;
        }
    }

    public static string ReadAllTextShared(string filePath) {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string rootPath) {
        if (!Directory.Exists(rootPath)) {
            yield break;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var rootName = Path.GetFileName(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(rootName, "conversations", StringComparison.OrdinalIgnoreCase)) {
            yield return normalizedRoot;
            yield break;
        }

        var conversationsPath = Path.Combine(normalizedRoot, "conversations");
        if (Directory.Exists(conversationsPath)) {
            yield return conversationsPath;
            yield break;
        }

        yield return normalizedRoot;
    }

    private static bool IsConversationFile(string path) {
        return path.EndsWith(".conversation.json", StringComparison.OrdinalIgnoreCase);
    }

    internal readonly record struct LmStudioNormalizedUsage(
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        long? DurationMs);
}
