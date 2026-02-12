using System;
using System.IO;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Base class for file system tools with safe-by-default path resolution.
/// </summary>
public abstract class FileSystemToolBase : ToolBase {
    /// <summary>
    /// Shared options for file system tools.
    /// </summary>
    protected readonly FileSystemToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when option values are invalid.</exception>
    protected FileSystemToolBase(FileSystemToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Resolves an input path to a full path and enforces <see cref="FileSystemToolOptions.AllowedRoots"/>.
    /// </summary>
    /// <param name="inputPath">Input path from tool arguments.</param>
    /// <param name="fullPath">Resolved full path if allowed.</param>
    /// <param name="error">Error message when resolution fails.</param>
    /// <returns>True if resolution succeeded and the path is allowed; otherwise false.</returns>
    protected bool TryResolvePath(string inputPath, out string fullPath, out string error) {
        return PathResolver.TryResolvePath(inputPath, Options.AllowedRoots, out fullPath, out error);
    }

    /// <summary>
    /// Resolves and validates an existing directory path and maps failures to a standardized tool error response.
    /// </summary>
    /// <param name="inputPath">Input path from tool arguments.</param>
    /// <param name="fullPath">Resolved full path if validation succeeds.</param>
    /// <param name="errorResponse">Serialized tool error response when validation fails.</param>
    /// <returns>True when validation succeeded; otherwise false.</returns>
    protected bool TryResolveExistingDirectory(string inputPath, out string fullPath, out string errorResponse) {
        if (ToolPaths.TryResolveAllowedExistingDirectory(
                inputPath: inputPath,
                allowedRoots: Options.AllowedRoots,
                fullPath: out fullPath,
                errorCode: out var errorCode,
                error: out var error,
                hints: out var hints)) {
            errorResponse = string.Empty;
            return true;
        }

        errorResponse = ToPathError(errorCode, error, hints);
        return false;
    }

    /// <summary>
    /// Resolves and validates an existing file path and maps failures to a standardized tool error response.
    /// </summary>
    /// <param name="inputPath">Input path from tool arguments.</param>
    /// <param name="fullPath">Resolved full path if validation succeeds.</param>
    /// <param name="errorResponse">Serialized tool error response when validation fails.</param>
    /// <param name="requiredExtension">Optional required file extension (for example: <c>.evtx</c>).</param>
    /// <returns>True when validation succeeded; otherwise false.</returns>
    protected bool TryResolveExistingFile(string inputPath, out string fullPath, out string errorResponse, string? requiredExtension = null) {
        var ok = false;
        string errorCode;
        string error;
        string[]? hints;

        if (string.IsNullOrWhiteSpace(requiredExtension)) {
            ok = ToolPaths.TryResolveAllowedExistingFile(
                inputPath: inputPath,
                allowedRoots: Options.AllowedRoots,
                fullPath: out fullPath,
                errorCode: out errorCode,
                error: out error,
                hints: out hints);
        } else {
            ok = ToolPaths.TryResolveAllowedExistingFile(
                inputPath: inputPath,
                allowedRoots: Options.AllowedRoots,
                requiredExtension: requiredExtension,
                fullPath: out fullPath,
                errorCode: out errorCode,
                error: out error,
                hints: out hints);
        }

        if (ok) {
            errorResponse = string.Empty;
            return true;
        }

        errorResponse = ToPathError(errorCode, error, hints);
        return false;
    }

    private static string ToPathError(string errorCode, string error, string[]? hints) {
        return ToolResponse.Error(errorCode, error, hints: hints, isTransient: false);
    }
}
