using System;
using System.IO;
using ADPlayground.Monitoring.History;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsHistoryHelper {
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

    internal static bool TryResolveHistoryReadContext(
        TestimoXToolOptions options,
        string? historyDirectoryInput,
        string toolName,
        out MonitoringHistoryReadContext historyContext,
        out string errorResponse) {
        historyContext = null!;
        errorResponse = string.Empty;

        if (!TryResolveHistoryDirectory(options, historyDirectoryInput, toolName, out var historyDirectory, out errorResponse)) {
            return false;
        }

        historyContext = new MonitoringHistoryReadContext(historyDirectory);
        if (File.Exists(historyContext.DatabasePath)) {
            return true;
        }

        errorResponse = ToolResultV2.Error(
            errorCode: "not_found",
            error: $"Monitoring history database '{MonitoringHistoryReadContext.DefaultSqliteDatabaseFileName}' was not found in the requested history_directory.",
            hints: new[] {
                $"Point history_directory at the monitoring history folder that contains {MonitoringHistoryReadContext.DefaultSqliteDatabaseFileName}.",
                $"Expected database path: {historyContext.DatabasePath}"
            },
            isTransient: false);
        return false;
    }

    internal static bool TryResolveExistingHistoryArtifactPath(
        TestimoXToolOptions options,
        string? historyDirectoryInput,
        string fileName,
        string toolName,
        out MonitoringHistoryReadContext historyContext,
        out string filePath,
        out string errorResponse) {
        historyContext = null!;
        filePath = string.Empty;
        errorResponse = string.Empty;

        if (!TryResolveHistoryDirectory(options, historyDirectoryInput, toolName, out var historyDirectory, out errorResponse)) {
            return false;
        }

        historyContext = new MonitoringHistoryReadContext(historyDirectory);
        filePath = historyContext.GetArtifactPath(fileName);
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

    internal static int ResolveContentCharLimit(JsonObject? arguments, int maxChars) {
        return ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "max_chars",
            defaultValue: maxChars,
            minInclusive: 128,
            maxInclusive: maxChars);
    }
}
