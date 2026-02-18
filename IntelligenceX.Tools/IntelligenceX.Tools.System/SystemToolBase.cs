using System;
using System.Collections.Generic;
using ComputerX.Updates;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Base class for system tools with shared option validation.
/// </summary>
public abstract class SystemToolBase : ToolBase {
    private const int MaxErrorMessageLength = 300;

    /// <summary>
    /// Shared options for system tools.
    /// </summary>
    protected readonly SystemToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    protected SystemToolBase(SystemToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Maps typed ComputerX failures to stable tool error envelopes.
    /// </summary>
    protected static string ErrorFromFailure<TFailure, TCode>(
        TFailure? failure,
        Func<TFailure, TCode> codeSelector,
        Func<TFailure, string?> messageSelector,
        string defaultMessage,
        string fallbackErrorCode = "query_failed")
        where TFailure : class {
        return ToolFailureMapper.ErrorFromFailure(
            failure,
            codeSelector,
            messageSelector,
            defaultMessage,
            fallbackErrorCode);
    }

    /// <summary>
    /// Maps common runtime exceptions to stable tool error envelopes.
    /// </summary>
    protected static string ErrorFromException(
        Exception exception,
        string defaultMessage = "System query failed.",
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "invalid_argument") {
        if (exception is null) {
            throw new ArgumentNullException(nameof(exception));
        }

        var safeDefault = string.IsNullOrWhiteSpace(defaultMessage)
            ? "System query failed."
            : defaultMessage.Trim();

        return exception switch {
            OperationCanceledException => ToolResponse.Error("cancelled", "Operation was cancelled.", isTransient: true),
            ArgumentException => ToolResponse.Error("invalid_argument", SanitizeErrorMessage(exception.Message, safeDefault)),
            InvalidOperationException => ToolResponse.Error(
                string.IsNullOrWhiteSpace(invalidOperationErrorCode) ? "invalid_argument" : invalidOperationErrorCode,
                SanitizeErrorMessage(exception.Message, safeDefault)),
            FormatException => ToolResponse.Error("invalid_argument", SanitizeErrorMessage(exception.Message, safeDefault)),
            UnauthorizedAccessException => ToolResponse.Error("access_denied", SanitizeErrorMessage(exception.Message, "Access denied while querying system data.")),
            TimeoutException => ToolResponse.Error("timeout", SanitizeErrorMessage(exception.Message, "System query timed out."), isTransient: true),
            NotSupportedException => ToolResponse.Error("not_supported", SanitizeErrorMessage(exception.Message, safeDefault)),
            _ => ToolResponse.Error(
                string.IsNullOrWhiteSpace(fallbackErrorCode) ? "query_failed" : fallbackErrorCode,
                safeDefault)
        };
    }

    /// <summary>
    /// Gets installed updates and optionally augments with pending local updates.
    /// </summary>
    protected static bool TryGetInstalledAndPendingUpdates(
        string? computerName,
        string target,
        bool includePendingLocal,
        out IReadOnlyList<UpdateInfo> updates,
        out bool pendingIncluded,
        out string? errorResponse) {
        pendingIncluded = false;
        var rows = new List<UpdateInfo>();
        try {
            rows.AddRange(Updates.GetInstalled(computerName));
        } catch (Exception ex) {
            updates = Array.Empty<UpdateInfo>();
            errorResponse = ErrorFromException(ex, defaultMessage: "Installed updates query failed.");
            return false;
        }

        if (includePendingLocal && IsLocalTarget(computerName, target)) {
            try {
                rows.AddRange(Updates.GetPending());
                pendingIncluded = true;
            } catch {
                pendingIncluded = false;
            }
        }

        updates = rows;
        errorResponse = null;
        return true;
    }

    private static bool IsLocalTarget(string? computerName, string target) {
        return string.IsNullOrWhiteSpace(computerName)
               || string.Equals(computerName, ".", StringComparison.Ordinal)
               || string.Equals(target, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeErrorMessage(string? message, string fallback) {
        var safeFallback = string.IsNullOrWhiteSpace(fallback) ? "Operation failed." : fallback.Trim();
        if (string.IsNullOrWhiteSpace(message)) {
            return safeFallback;
        }

        var compact = message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        while (compact.Contains("  ", StringComparison.Ordinal)) {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (compact.Length == 0) {
            return safeFallback;
        }

        return compact.Length <= MaxErrorMessageLength
            ? compact
            : compact.Substring(0, MaxErrorMessageLength).TrimEnd() + "...";
    }
}
