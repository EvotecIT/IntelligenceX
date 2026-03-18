using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Telemetry.Usage.Codex;

internal static class CodexSessionImportSupport {
    public static bool IsCodexProvider(string? providerId) {
        var normalized = UsageTelemetryQuickReportSupport.NormalizeOptional(providerId);
        return string.Equals(normalized, "codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "openai-codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "chatgpt-codex", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetExtension(rootPath), ".jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }
            yield break;
        }

        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in EnumerateCandidateDirectories(Path.GetFullPath(rootPath))) {
            foreach (var file in EnumerateFilesFromDirectory(directory, preferRecentArtifacts)) {
                if (seenFiles.Add(file)) {
                    yield return file;
                }
            }
        }
    }

    public static string? ExtractSessionId(JsonObject? payload) {
        if (payload is null) {
            return null;
        }

        var meta = payload.GetObject("meta");
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            meta?.GetString("id")
            ?? meta?.GetString("conversation_id")
            ?? payload.GetString("session_id")
            ?? payload.GetString("sessionId")
            ?? payload.GetString("thread_id")
            ?? payload.GetString("threadId"));
    }

    public static string? TryExtractSessionIdFromFileName(string filePath) {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        if (name.Length >= 36) {
            var suffix = name.Substring(name.Length - 36);
            if (Guid.TryParse(suffix, out _)) {
                return suffix;
            }
        }

        return UsageTelemetryQuickReportSupport.NormalizeOptional(name);
    }

    public static string? ExtractTurnId(JsonObject? payload, JsonObject? info) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            payload?.GetString("turn_id")
            ?? payload?.GetString("turnId")
            ?? info?.GetString("turn_id")
            ?? info?.GetString("turnId"));
    }

    public static string? ExtractResponseId(JsonObject? payload, JsonObject? info) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            payload?.GetString("response_id")
            ?? payload?.GetString("responseId")
            ?? info?.GetString("response_id")
            ?? info?.GetString("responseId"));
    }

    public static string? ExtractModel(JsonObject? payload) {
        if (payload is null) {
            return null;
        }

        var directModel = UsageTelemetryQuickReportSupport.NormalizeOptional(payload.GetString("model"))
                          ?? UsageTelemetryQuickReportSupport.NormalizeOptional(payload.GetString("model_name"));
        if (!string.IsNullOrWhiteSpace(directModel)) {
            return directModel;
        }

        var info = payload.GetObject("info");
        var infoModel = UsageTelemetryQuickReportSupport.NormalizeOptional(info?.GetString("model"))
                        ?? UsageTelemetryQuickReportSupport.NormalizeOptional(info?.GetString("model_name"));
        if (!string.IsNullOrWhiteSpace(infoModel)) {
            return infoModel;
        }

        var infoMetadata = info?.GetObject("metadata");
        var infoMetadataModel = UsageTelemetryQuickReportSupport.NormalizeOptional(infoMetadata?.GetString("model"));
        if (!string.IsNullOrWhiteSpace(infoMetadataModel)) {
            return infoMetadataModel;
        }

        return UsageTelemetryQuickReportSupport.NormalizeOptional(payload.GetObject("metadata")?.GetString("model"));
    }

    public static CodexNormalizedUsage? NormalizeUsage(JsonObject? obj) {
        if (obj is null) {
            return null;
        }

        var inputTokens = UsageTelemetryQuickReportSupport.ReadInt64(obj, "input_tokens", "inputTokens") ?? 0;
        var cachedInputTokens =
            UsageTelemetryQuickReportSupport.ReadInt64(
                obj,
                "cached_input_tokens",
                "cachedInputTokens",
                "cache_read_input_tokens",
                "cacheReadInputTokens")
            ?? 0;
        var outputTokens = UsageTelemetryQuickReportSupport.ReadInt64(obj, "output_tokens", "outputTokens") ?? 0;
        var reasoningTokens =
            UsageTelemetryQuickReportSupport.ReadInt64(
                obj,
                "reasoning_output_tokens",
                "reasoningOutputTokens",
                "reasoning_tokens",
                "reasoningTokens")
            ?? 0;
        var totalTokens = UsageTelemetryQuickReportSupport.ReadInt64(obj, "total_tokens", "totalTokens")
                          ?? Math.Max(0, inputTokens + outputTokens);

        return new CodexNormalizedUsage(
            InputTokens: Math.Max(0, inputTokens),
            CachedInputTokens: Math.Max(0, cachedInputTokens),
            OutputTokens: Math.Max(0, outputTokens),
            ReasoningTokens: Math.Max(0, reasoningTokens),
            TotalTokens: Math.Max(0, totalTokens));
    }

    public static CodexNormalizedUsage SubtractUsage(CodexNormalizedUsage current, CodexNormalizedUsage? previous) {
        return new CodexNormalizedUsage(
            InputTokens: Math.Max(0, current.InputTokens - (previous?.InputTokens ?? 0)),
            CachedInputTokens: Math.Max(0, current.CachedInputTokens - (previous?.CachedInputTokens ?? 0)),
            OutputTokens: Math.Max(0, current.OutputTokens - (previous?.OutputTokens ?? 0)),
            ReasoningTokens: Math.Max(0, current.ReasoningTokens - (previous?.ReasoningTokens ?? 0)),
            TotalTokens: Math.Max(0, current.TotalTokens - (previous?.TotalTokens ?? 0)));
    }

    public static ResolvedUsageAccount ResolveAccount(string artifactPath, string rootPath) {
        var candidateDirectories = new List<string>();
        AddSearchDirectory(candidateDirectories, Path.GetDirectoryName(Path.GetFullPath(artifactPath)));

        if (File.Exists(rootPath)) {
            AddSearchDirectory(candidateDirectories, Path.GetDirectoryName(Path.GetFullPath(rootPath)));
        } else if (Directory.Exists(rootPath)) {
            AddSearchDirectory(candidateDirectories, Path.GetFullPath(rootPath));
        }

        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startDirectory in candidateDirectories) {
            if (string.IsNullOrWhiteSpace(startDirectory)) {
                continue;
            }

            var current = startDirectory;
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(current); depth++) {
                if (!seenDirectories.Add(current)) {
                    break;
                }

                var authPath = Path.Combine(current, "auth.json");
                var profile = CodexAuthStore.TryReadProfile(authPath);
                if (profile is not null) {
                    return new ResolvedUsageAccount {
                        ProviderAccountId = UsageTelemetryQuickReportSupport.NormalizeOptional(profile.AccountId),
                        AccountLabel = UsageTelemetryQuickReportSupport.NormalizeOptional(profile.AccountLabel)
                    };
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                current = parent;
            }
        }

        return new ResolvedUsageAccount();
    }

    public static string? ResolveProviderAccountId(string artifactPath, string rootPath) {
        return ResolveAccount(artifactPath, rootPath).ProviderAccountId;
    }

    private static IEnumerable<string> EnumerateFilesFromDirectory(string directory, bool preferRecentArtifacts) {
        foreach (var file in UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                     UsageTelemetryQuickReportSupport.EnumerateFilesSafe(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                         .Where(IsSessionFile)
                         .Select(Path.GetFullPath),
                     preferRecentArtifacts,
                     UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)) {
            yield return file;
        }

        var partitionRoots = UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                UsageTelemetryQuickReportSupport.EnumerateDirectoriesSafe(directory)
                    .Where(static value => UsageTelemetryQuickReportSupport.IsIntegerPathSegment(Path.GetFileName(value), 4)),
                preferRecentArtifacts,
                UsageTelemetryQuickReportSupport.GetNumericPathSegment)
            .ToArray();
        if (partitionRoots.Length == 0) {
            foreach (var file in UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                         UsageTelemetryQuickReportSupport.EnumerateFilesSafe(directory, "*.jsonl", SearchOption.AllDirectories)
                             .Where(IsSessionFile)
                             .Select(Path.GetFullPath),
                         preferRecentArtifacts,
                         UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)) {
                yield return file;
            }

            yield break;
        }

        foreach (var yearDirectory in partitionRoots) {
            var monthDirectories = UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                    UsageTelemetryQuickReportSupport.EnumerateDirectoriesSafe(yearDirectory)
                        .Where(static value => UsageTelemetryQuickReportSupport.IsIntegerPathSegment(Path.GetFileName(value), 2)),
                    preferRecentArtifacts,
                    UsageTelemetryQuickReportSupport.GetNumericPathSegment)
                .ToArray();

            if (monthDirectories.Length == 0) {
                foreach (var file in UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                             UsageTelemetryQuickReportSupport.EnumerateFilesSafe(yearDirectory, "*.jsonl", SearchOption.AllDirectories)
                                 .Where(IsSessionFile)
                                 .Select(Path.GetFullPath),
                             preferRecentArtifacts,
                             UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)) {
                    yield return file;
                }

                continue;
            }

            foreach (var monthDirectory in monthDirectories) {
                var dayDirectories = UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                        UsageTelemetryQuickReportSupport.EnumerateDirectoriesSafe(monthDirectory)
                            .Where(static value => UsageTelemetryQuickReportSupport.IsIntegerPathSegment(Path.GetFileName(value), 2)),
                        preferRecentArtifacts,
                        UsageTelemetryQuickReportSupport.GetNumericPathSegment)
                    .ToArray();

                if (dayDirectories.Length == 0) {
                    foreach (var file in UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                                 UsageTelemetryQuickReportSupport.EnumerateFilesSafe(monthDirectory, "*.jsonl", SearchOption.AllDirectories)
                                     .Where(IsSessionFile)
                                     .Select(Path.GetFullPath),
                                 preferRecentArtifacts,
                                 UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)) {
                        yield return file;
                    }

                    continue;
                }

                foreach (var dayDirectory in dayDirectories) {
                    foreach (var file in UsageTelemetryQuickReportSupport.OrderPathsByDirection(
                                 UsageTelemetryQuickReportSupport.EnumerateFilesSafe(dayDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                                     .Where(IsSessionFile)
                                     .Select(Path.GetFullPath),
                                 preferRecentArtifacts,
                                 UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)) {
                        yield return file;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string rootPath) {
        if (!Directory.Exists(rootPath)) {
            yield break;
        }

        var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(rootName, "sessions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "archived_sessions", StringComparison.OrdinalIgnoreCase)) {
            yield return rootPath;
            yield break;
        }

        var sessionsPath = Path.Combine(rootPath, "sessions");
        if (Directory.Exists(sessionsPath)) {
            yield return sessionsPath;
        }

        var archivedPath = Path.Combine(rootPath, "archived_sessions");
        if (Directory.Exists(archivedPath)) {
            yield return archivedPath;
        }

        if (!Directory.Exists(sessionsPath) && !Directory.Exists(archivedPath)) {
            yield return rootPath;
        }
    }

    private static bool IsSessionFile(string path) {
        var name = Path.GetFileName(path);
        return name.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase) &&
               name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddSearchDirectory(List<string> directories, string? path) {
        if (!string.IsNullOrWhiteSpace(path)) {
            directories.Add(path!);
        }
    }

    internal sealed record CodexNormalizedUsage(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningTokens,
        long TotalTokens);
}
