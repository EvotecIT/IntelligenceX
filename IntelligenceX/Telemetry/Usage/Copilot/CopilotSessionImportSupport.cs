using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Copilot;

internal static class CopilotSessionImportSupport {
    public static bool IsCopilotProvider(string? providerId) {
        var normalized = UsageTelemetryQuickReportSupport.NormalizeOptional(providerId);
        return string.Equals(normalized, "copilot", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "copilot-cli", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "github-copilot", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "githubcopilot", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetFileName(rootPath), "events.jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }

            yield break;
        }

        foreach (var directory in EnumerateCandidateDirectories(rootPath)) {
            var files = UsageTelemetryQuickReportSupport
                .EnumerateFilesSafe(directory, "events.jsonl", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray();
            var orderedFiles = preferRecentArtifacts
                ? files.OrderByDescending(UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)
                    .ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);

            foreach (var file in orderedFiles) {
                yield return file;
            }
        }
    }

    public static string? ExtractSessionId(JsonObject? data, string? filePath = null) {
        var sessionId = UsageTelemetryQuickReportSupport.NormalizeOptional(
            data?.GetString("sessionId")
            ?? data?.GetString("session_id"));
        if (!string.IsNullOrWhiteSpace(sessionId)) {
            return sessionId;
        }

        if (string.IsNullOrWhiteSpace(filePath)) {
            return null;
        }

        var sessionDirectory = Directory.GetParent(filePath);
        return sessionDirectory is null
            ? null
            : UsageTelemetryQuickReportSupport.NormalizeOptional(sessionDirectory.Name);
    }

    public static string? ExtractTurnId(JsonObject? data) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            data?.GetString("turnId")
            ?? data?.GetString("turn_id")
            ?? data?.GetString("interactionId")
            ?? data?.GetString("interaction_id"));
    }

    public static string? ExtractInteractionId(JsonObject? data) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            data?.GetString("interactionId")
            ?? data?.GetString("interaction_id"));
    }

    public static string? ExtractProducer(JsonObject? data) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            data?.GetString("producer")
            ?? data?.GetString("source"));
    }

    public static string? ExtractCopilotVersion(JsonObject? data) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(data?.GetString("copilotVersion"));
    }

    public static string? ExtractSelectedModel(JsonObject? data) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            data?.GetString("selectedModel")
            ?? data?.GetString("selected_model")
            ?? data?.GetString("currentModel")
            ?? data?.GetString("current_model")
            ?? data?.GetString("model"));
    }

    public static string? ExtractAuthenticatedLogin(JsonObject? data) {
        if (!string.Equals(
                UsageTelemetryQuickReportSupport.NormalizeOptional(data?.GetString("infoType")),
                "authentication",
                StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var message = UsageTelemetryQuickReportSupport.NormalizeOptional(data?.GetString("message"));
        if (string.IsNullOrWhiteSpace(message)) {
            return null;
        }

        const string prefix = "Signed in successfully as ";
        if (!message!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var login = message.Substring(prefix.Length).Trim().TrimEnd('!', '.');
        return UsageTelemetryQuickReportSupport.NormalizeOptional(login);
    }

    public static string? ResolveProviderAccountId(string filePath, string rootPath) {
        var configPath = ResolveConfigPath(filePath, rootPath);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) {
            return null;
        }

        try {
            var configFilePath = configPath!;
            var obj = JsonLite.Parse(LmStudio.LmStudioConversationImportSupport.ReadAllTextShared(configFilePath)).AsObject();
            var lastUser = obj?.GetObject("last_logged_in_user");
            var login = UsageTelemetryQuickReportSupport.NormalizeOptional(lastUser?.GetString("login"));
            if (!string.IsNullOrWhiteSpace(login)) {
                return login;
            }

            foreach (var entry in obj?.GetArray("logged_in_users") ?? new JsonArray()) {
                var user = entry.AsObject();
                login = UsageTelemetryQuickReportSupport.NormalizeOptional(user?.GetString("login"));
                if (!string.IsNullOrWhiteSpace(login)) {
                    return login;
                }
            }
        } catch {
            // Ignore malformed local config and fall back to session events.
        }

        return null;
    }

    public static string? ResolveConfigPath(string filePath, string rootPath) {
        var normalizedRoot = UsageTelemetryIdentity.NormalizePath(rootPath);
        if (File.Exists(normalizedRoot)) {
            normalizedRoot = Path.GetDirectoryName(normalizedRoot) ?? normalizedRoot;
        }

        if (string.Equals(Path.GetFileName(normalizedRoot), ".copilot", StringComparison.OrdinalIgnoreCase)) {
            var directConfig = Path.Combine(normalizedRoot, "config.json");
            if (File.Exists(directConfig)) {
                return directConfig;
            }
        }

        var current = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? rootPath);
        while (current is not null) {
            if (string.Equals(current.Name, ".copilot", StringComparison.OrdinalIgnoreCase)) {
                var candidate = Path.Combine(current.FullName, "config.json");
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }

            current = current.Parent;
        }

        return null;
    }

    public static string BuildDefaultModelLabel(string? producer, string? copilotVersion) {
        var normalizedProducer = UsageTelemetryQuickReportSupport.NormalizeOptional(producer) ?? "copilot-cli";
        var normalizedVersion = UsageTelemetryQuickReportSupport.NormalizeOptional(copilotVersion);
        return string.IsNullOrWhiteSpace(normalizedVersion)
            ? normalizedProducer
            : normalizedProducer + "/" + normalizedVersion;
    }

    public static string BuildEffectiveModelLabel(
        string? selectedModel,
        string? producer,
        string? copilotVersion) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(selectedModel)
               ?? BuildDefaultModelLabel(producer, copilotVersion);
    }

    public static string? ResolveDefaultModelFromLogs(
        string filePath,
        string rootPath,
        string? sessionId) {
        var logsDirectory = ResolveLogsDirectory(filePath, rootPath);
        if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory)) {
            return null;
        }

        string? fallbackModel = null;
        foreach (var logFile in UsageTelemetryQuickReportSupport
                     .EnumerateFilesSafe(logsDirectory!, "process-*.log", SearchOption.TopDirectoryOnly)
                     .Select(Path.GetFullPath)
                     .OrderByDescending(UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)
                     .ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)) {
            var sawSessionId = false;
            string? lastModel = null;
            foreach (var line in UsageTelemetryQuickReportSupport.ReadLinesShared(logFile)) {
                if (!string.IsNullOrWhiteSpace(sessionId) &&
                    line.IndexOf(sessionId, StringComparison.OrdinalIgnoreCase) >= 0) {
                    sawSessionId = true;
                }

                var parsedModel = TryExtractDefaultModelFromLogLine(line);
                if (!string.IsNullOrWhiteSpace(parsedModel)) {
                    lastModel = parsedModel;
                }
            }

            if (string.IsNullOrWhiteSpace(lastModel)) {
                continue;
            }

            fallbackModel ??= lastModel;
            if (sawSessionId) {
                return lastModel;
            }
        }

        return fallbackModel;
    }

    public static bool TryComputeDurationMs(
        DateTimeOffset? startedAtUtc,
        DateTimeOffset completedAtUtc,
        out long? durationMs) {
        durationMs = null;
        if (!startedAtUtc.HasValue) {
            return false;
        }

        var elapsed = completedAtUtc - startedAtUtc.Value;
        if (elapsed < TimeSpan.Zero) {
            return false;
        }

        durationMs = Math.Max(0L, (long)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero));
        return true;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string rootPath) {
        if (!Directory.Exists(rootPath)) {
            yield break;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var rootName = Path.GetFileName(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(rootName, "session-state", StringComparison.OrdinalIgnoreCase)) {
            yield return normalizedRoot;
            yield break;
        }

        var sessionState = Path.Combine(normalizedRoot, "session-state");
        yield return Directory.Exists(sessionState) ? sessionState : normalizedRoot;
    }

    private static string? ResolveLogsDirectory(string filePath, string rootPath) {
        var copilotHome = ResolveCopilotHome(filePath, rootPath);
        if (string.IsNullOrWhiteSpace(copilotHome)) {
            return null;
        }

        var logsDirectory = Path.Combine(copilotHome!, "logs");
        return Directory.Exists(logsDirectory) ? logsDirectory : null;
    }

    private static string? ResolveCopilotHome(string filePath, string rootPath) {
        var normalizedRoot = UsageTelemetryIdentity.NormalizePath(rootPath);
        if (File.Exists(normalizedRoot)) {
            normalizedRoot = Path.GetDirectoryName(normalizedRoot) ?? normalizedRoot;
        }

        if (string.Equals(Path.GetFileName(normalizedRoot), ".copilot", StringComparison.OrdinalIgnoreCase)) {
            return normalizedRoot;
        }

        var current = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? rootPath);
        while (current is not null) {
            if (string.Equals(current.Name, ".copilot", StringComparison.OrdinalIgnoreCase)) {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? TryExtractDefaultModelFromLogLine(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return null;
        }

        const string marker = "Using default model:";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) {
            return null;
        }

        var model = line[(index + marker.Length)..].Trim();
        return UsageTelemetryQuickReportSupport.NormalizeOptional(model);
    }
}
