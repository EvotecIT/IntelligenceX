using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Opinionated helpers for resolving tool input paths using <see cref="PathResolver"/> and mapping failures
/// to consistent tool error codes and hints.
/// </summary>
public static class ToolPaths {
    /// <summary>
    /// Resolves a file path, enforces <paramref name="allowedRoots"/>, optionally checks the extension, and ensures the file exists.
    /// </summary>
    /// <param name="inputPath">Input path (absolute or relative) from tool arguments.</param>
    /// <param name="allowedRoots">Allowed root directories for safe-by-default access.</param>
    /// <param name="fullPath">Resolved full path when successful.</param>
    /// <param name="errorCode">Tool error code when unsuccessful.</param>
    /// <param name="error">Human-readable error message when unsuccessful.</param>
    /// <param name="hints">Optional hints for remediation when unsuccessful.</param>
    /// <param name="requiredExtension">Optional required extension (for example: <c>.evtx</c>).</param>
    /// <returns>True when the path is allowed and exists; otherwise false.</returns>
    public static bool TryResolveAllowedExistingFile(
        string inputPath,
        IReadOnlyList<string> allowedRoots,
        out string fullPath,
        out string errorCode,
        out string error,
        out string[]? hints,
        string? requiredExtension = null) {

        fullPath = string.Empty;
        errorCode = "error";
        error = string.Empty;
        hints = null;

        if (string.IsNullOrWhiteSpace(inputPath)) {
            errorCode = "invalid_argument";
            error = "path is required.";
            return false;
        }

        if (!PathResolver.TryResolvePath(inputPath, allowedRoots, out fullPath, out var resolveErr)) {
            errorCode = ClassifyResolveErrorCode(resolveErr);
            error = resolveErr;
            hints = new[] {
                "Adjust AllowedRoots to include the requested file.",
                "Use an absolute path inside an allowed root."
            };
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredExtension) &&
            !fullPath.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase)) {
            errorCode = "invalid_argument";
            error = $"Only {requiredExtension} files are supported.";
            hints = new[] { $"Provide a path ending with {requiredExtension}." };
            return false;
        }

        if (!File.Exists(fullPath)) {
            errorCode = "not_found";
            error = "File not found.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a directory path, enforces <paramref name="allowedRoots"/>, and ensures the directory exists.
    /// </summary>
    /// <param name="inputPath">Input path (absolute or relative) from tool arguments.</param>
    /// <param name="allowedRoots">Allowed root directories for safe-by-default access.</param>
    /// <param name="fullPath">Resolved full path when successful.</param>
    /// <param name="errorCode">Tool error code when unsuccessful.</param>
    /// <param name="error">Human-readable error message when unsuccessful.</param>
    /// <param name="hints">Optional hints for remediation when unsuccessful.</param>
    /// <returns>True when the path is allowed and exists; otherwise false.</returns>
    public static bool TryResolveAllowedExistingDirectory(
        string inputPath,
        IReadOnlyList<string> allowedRoots,
        out string fullPath,
        out string errorCode,
        out string error,
        out string[]? hints) {

        fullPath = string.Empty;
        errorCode = "error";
        error = string.Empty;
        hints = null;

        if (string.IsNullOrWhiteSpace(inputPath)) {
            errorCode = "invalid_argument";
            error = "path is required.";
            return false;
        }

        if (!PathResolver.TryResolvePath(inputPath, allowedRoots, out fullPath, out var resolveErr)) {
            errorCode = ClassifyResolveErrorCode(resolveErr);
            error = resolveErr;
            hints = new[] {
                "Adjust AllowedRoots to include the requested directory.",
                "Use an absolute path inside an allowed root."
            };
            return false;
        }

        if (!Directory.Exists(fullPath)) {
            errorCode = "not_found";
            error = "Directory not found.";
            return false;
        }

        return true;
    }

    private static string ClassifyResolveErrorCode(string error) {
        if (string.IsNullOrWhiteSpace(error)) {
            return "error";
        }
        if (error.StartsWith("Invalid path", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Path is required", StringComparison.OrdinalIgnoreCase)) {
            return "invalid_argument";
        }
        if (error.StartsWith("Access denied", StringComparison.OrdinalIgnoreCase)) {
            return "access_denied";
        }
        return "error";
    }
}
