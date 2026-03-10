using System;
using System.IO;
using ADPlayground.Monitoring.Config;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXMonitoringHistoryHelper {
    private const string DefaultSqliteDatabaseFileName = "monitoring.sqlite";
    private const int DefaultPreviewChars = 1200;

    internal static bool TryResolveHistoryDirectory(
        TestimoXToolOptions options,
        string? inputPath,
        string toolName,
        out string fullPath,
        out string errorResponse) {
        fullPath = string.Empty;
        errorResponse = string.Empty;

        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.AllowedHistoryRoots.Count == 0) {
            errorResponse = ToolResultV2.Error(
                errorCode: "access_denied",
                error: "Monitoring history inspection is disabled (AllowedHistoryRoots is empty).",
                hints: new[] {
                    $"Configure AllowedHistoryRoots before calling {toolName}.",
                    "Provide a history_directory inside an allowed root."
                },
                isTransient: false);
            return false;
        }

        if (!ToolPaths.TryResolveAllowedExistingDirectory(
                inputPath ?? string.Empty,
                options.AllowedHistoryRoots,
                out fullPath,
                out var errorCode,
                out var error,
                out var hints)) {
            errorResponse = ToolResultV2.Error(
                errorCode: errorCode,
                error: error,
                hints: hints,
                isTransient: false);
            return false;
        }

        return true;
    }

    internal static bool TryResolveHistoryDatabasePath(
        TestimoXToolOptions options,
        string? historyDirectoryInput,
        string toolName,
        out string historyDirectory,
        out string databasePath,
        out string errorResponse) {
        historyDirectory = string.Empty;
        databasePath = string.Empty;
        errorResponse = string.Empty;

        if (!TryResolveHistoryDirectory(options, historyDirectoryInput, toolName, out historyDirectory, out errorResponse)) {
            return false;
        }

        databasePath = Path.Combine(historyDirectory, DefaultSqliteDatabaseFileName);
        if (File.Exists(databasePath)) {
            return true;
        }

        errorResponse = ToolResultV2.Error(
            errorCode: "not_found",
            error: $"Monitoring history database '{DefaultSqliteDatabaseFileName}' was not found in the requested history_directory.",
            hints: new[] {
                "Point history_directory at the monitoring history folder that contains monitoring.sqlite.",
                $"Expected database path: {databasePath}"
            },
            isTransient: false);
        return false;
    }

    internal static bool TryResolveHistoryFilePath(
        TestimoXToolOptions options,
        string? historyDirectoryInput,
        string fileName,
        string toolName,
        out string historyDirectory,
        out string filePath,
        out string errorResponse) {
        historyDirectory = string.Empty;
        filePath = string.Empty;
        errorResponse = string.Empty;

        if (!TryResolveHistoryDirectory(options, historyDirectoryInput, toolName, out historyDirectory, out errorResponse)) {
            return false;
        }

        filePath = Path.Combine(historyDirectory, fileName);
        if (File.Exists(filePath)) {
            return true;
        }

        errorResponse = ToolResultV2.Error(
            errorCode: "not_found",
            error: $"History snapshot file '{fileName}' was not found in the requested history_directory.",
            hints: new[] {
                $"Expected snapshot path: {filePath}",
                $"Point history_directory at the monitoring history folder that contains {fileName}."
            },
            isTransient: false);
        return false;
    }

    internal static HistoryDatabaseConfig CreateSqliteDatabaseConfig(string databasePath) {
        return new HistoryDatabaseConfig {
            Provider = HistoryDatabaseProvider.Sqlite,
            DatabasePath = databasePath
        };
    }

    internal static SqliteHistoryOptions CreateSqliteOptions() {
        return new SqliteHistoryOptions();
    }

    internal static int ResolveContentCharLimit(JsonObject? arguments, int maxChars) {
        return ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "max_chars",
            defaultValue: maxChars,
            minInclusive: 128,
            maxInclusive: maxChars);
    }

    internal static SnapshotTextProjection ProjectText(string? value, bool includeContent, int maxChars) {
        var text = value ?? string.Empty;
        var preview = text.Length <= DefaultPreviewChars
            ? text
            : text.Substring(0, DefaultPreviewChars) + "...";

        if (!includeContent) {
            return new SnapshotTextProjection(
                Preview: preview,
                Content: string.Empty,
                Included: false,
                Truncated: text.Length > DefaultPreviewChars,
                ReturnedChars: preview.Length);
        }

        var returned = text.Length <= maxChars
            ? text
            : text.Substring(0, maxChars);
        return new SnapshotTextProjection(
            Preview: preview,
            Content: returned,
            Included: true,
            Truncated: text.Length > returned.Length,
            ReturnedChars: returned.Length);
    }

    internal static string NormalizeJsonPreview(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return string.Empty;
        }

        return json.Trim();
    }

    internal sealed record SnapshotTextProjection(
        string Preview,
        string Content,
        bool Included,
        bool Truncated,
        int ReturnedChars);
}
