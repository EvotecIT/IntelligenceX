using System;
using System.IO;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class AdMonitoringArtifactHelper {
    internal static bool TryResolveMonitoringDirectory(
        ActiveDirectoryToolOptions options,
        string? inputPath,
        string toolName,
        out string fullPath,
        out string errorResponse) {
        fullPath = string.Empty;
        errorResponse = string.Empty;

        ArgumentNullException.ThrowIfNull(options);

        if (options.AllowedMonitoringRoots.Count == 0) {
            errorResponse = ToolResultV2.Error(
                errorCode: "access_denied",
                error: "AD monitoring artifact inspection is disabled (AllowedMonitoringRoots is empty).",
                hints: new[] {
                    $"Configure AllowedMonitoringRoots before calling {toolName}.",
                    "Provide a monitoring_directory inside an allowed root."
                },
                isTransient: false);
            return false;
        }

        if (!ToolPaths.TryResolveAllowedExistingDirectory(
                inputPath ?? string.Empty,
                options.AllowedMonitoringRoots,
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

    internal static bool TryResolveMonitoringFilePath(
        ActiveDirectoryToolOptions options,
        string? monitoringDirectoryInput,
        string fileName,
        string toolName,
        out string monitoringDirectory,
        out string filePath,
        out string errorResponse) {
        monitoringDirectory = string.Empty;
        filePath = string.Empty;
        errorResponse = string.Empty;

        if (!TryResolveMonitoringDirectory(options, monitoringDirectoryInput, toolName, out monitoringDirectory, out errorResponse)) {
            return false;
        }

        filePath = Path.Combine(monitoringDirectory, fileName);
        if (File.Exists(filePath)) {
            return true;
        }

        errorResponse = ToolResultV2.Error(
            errorCode: "not_found",
            error: $"Monitoring snapshot file '{fileName}' was not found in the requested monitoring_directory.",
            hints: new[] {
                $"Expected snapshot path: {filePath}",
                $"Point monitoring_directory at the monitoring folder that contains {fileName}."
            },
            isTransient: false);
        return false;
    }
}
