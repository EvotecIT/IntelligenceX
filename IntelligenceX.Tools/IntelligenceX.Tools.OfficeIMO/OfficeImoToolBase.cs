using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Base class for OfficeIMO-backed tools with safe-by-default path resolution.
/// </summary>
public abstract class OfficeImoToolBase : ToolBase {
    /// <summary>
    /// Shared options for OfficeIMO tools.
    /// </summary>
    protected readonly OfficeImoToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfficeImoToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    protected OfficeImoToolBase(OfficeImoToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Resolves and validates an existing directory path and maps failures to a standardized tool error response.
    /// </summary>
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

        errorResponse = ToolResponse.Error(errorCode, error, hints: hints, isTransient: false);
        return false;
    }

    /// <summary>
    /// Resolves and validates an existing file path and maps failures to a standardized tool error response.
    /// </summary>
    protected bool TryResolveExistingFile(string inputPath, out string fullPath, out string errorResponse) {
        if (ToolPaths.TryResolveAllowedExistingFile(
                inputPath: inputPath,
                allowedRoots: Options.AllowedRoots,
                fullPath: out fullPath,
                errorCode: out var errorCode,
                error: out var error,
                hints: out var hints)) {
            errorResponse = string.Empty;
            return true;
        }

        errorResponse = ToolResponse.Error(errorCode, error, hints: hints, isTransient: false);
        return false;
    }
}

