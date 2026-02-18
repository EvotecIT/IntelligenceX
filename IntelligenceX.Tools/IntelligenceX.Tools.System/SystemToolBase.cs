using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.PatchDetails;
using ComputerX.Updates;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Base class for system tools with shared option validation.
/// </summary>
public abstract class SystemToolBase : ToolBase {
    private const int MaxErrorMessageLength = 300;
    private static readonly string[] AllowedPatchSeverities = {
        "Critical",
        "Important",
        "Moderate",
        "Low"
    };

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

    /// <summary>
    /// Resolves patch release year/month from arguments with defaults and validation.
    /// </summary>
    protected static bool TryResolvePatchReleaseWindow(
        JsonObject? arguments,
        out int year,
        out int month,
        out string? errorResponse) {
        var nowUtc = DateTime.UtcNow;
        year = nowUtc.Year;
        month = nowUtc.Month;
        errorResponse = null;

        var yearRaw = arguments?.GetInt64("year");
        var monthRaw = arguments?.GetInt64("month");
        if (yearRaw.HasValue) {
            if (yearRaw.Value < 2000 || yearRaw.Value > 2100) {
                errorResponse = ToolResponse.Error("invalid_argument", "year must be between 2000 and 2100.");
                return false;
            }
            year = (int)yearRaw.Value;
        }

        if (monthRaw.HasValue) {
            if (monthRaw.Value < 1 || monthRaw.Value > 12) {
                errorResponse = ToolResponse.Error("invalid_argument", "month must be between 1 and 12.");
                return false;
            }
            month = (int)monthRaw.Value;
        }

        return true;
    }

    /// <summary>
    /// Resolves optional mapped product filter arguments used by patch tools.
    /// </summary>
    protected static bool TryResolvePatchProductFilter(
        JsonObject? arguments,
        out string? productFamily,
        out string? productVersion,
        out string? productBuild,
        out string? productEdition,
        out string? errorResponse) {
        productFamily = ToolArgs.GetOptionalTrimmed(arguments, "product_family");
        productVersion = ToolArgs.GetOptionalTrimmed(arguments, "product_version");
        productBuild = ToolArgs.GetOptionalTrimmed(arguments, "product_build");
        productEdition = ToolArgs.GetOptionalTrimmed(arguments, "product_edition");
        errorResponse = null;

        if (string.IsNullOrWhiteSpace(productFamily)
            && (!string.IsNullOrWhiteSpace(productVersion)
                || !string.IsNullOrWhiteSpace(productBuild)
                || !string.IsNullOrWhiteSpace(productEdition))) {
            errorResponse = ToolResponse.Error(
                "invalid_argument",
                "product_family is required when product_version/product_build/product_edition is provided.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves optional severity allowlist for patch tools.
    /// </summary>
    protected static bool TryResolvePatchSeverityAllowlist(
        JsonObject? arguments,
        out IReadOnlyList<string> severity,
        out string? errorResponse) {
        var severityRaw = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("severity"));
        var normalized = new List<string>(severityRaw.Count);
        foreach (var item in severityRaw) {
            if (!TryNormalizePatchSeverity(item, out var allowedSeverity)) {
                severity = Array.Empty<string>();
                errorResponse = ToolResponse.Error(
                    "invalid_argument",
                    "severity contains unsupported value. Allowed: Critical, Important, Moderate, Low.");
                return false;
            }

            normalized.Add(allowedSeverity);
        }

        severity = normalized;
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Loads monthly patch details with optional mapped product filtering.
    /// </summary>
    protected static async Task<(IReadOnlyList<PatchDetailsInfo> Data, string? ErrorResponse)> TryGetMonthlyPatchDetailsAsync(
        int year,
        int month,
        string? productFamily,
        string? productVersion,
        string? productBuild,
        string? productEdition,
        CancellationToken cancellationToken) {
        try {
            if (!string.IsNullOrWhiteSpace(productFamily)) {
                var descriptor = new ProductDescriptor {
                    Family = productFamily,
                    Version = productVersion ?? string.Empty,
                    Build = productBuild,
                    Edition = productEdition
                };

                var filtered = await PatchDetails.GetForProductsAsync(
                    products: new[] { descriptor },
                    since: new DateTime(year, month, 1),
                    ct: cancellationToken).ConfigureAwait(false);
                return (filtered, null);
            }

            var monthly = await PatchDetails.GetMonthlyAsync(year, month, cancellationToken).ConfigureAwait(false);
            return (monthly, null);
        } catch (Exception ex) {
            return (Array.Empty<PatchDetailsInfo>(), ErrorFromException(ex, defaultMessage: "Patch details query failed."));
        }
    }

    /// <summary>
    /// Adds standard patch filter metadata fields shared by patch tools.
    /// </summary>
    protected static void AddPatchFilterMeta(
        JsonObject meta,
        int year,
        int month,
        string release,
        string? productFamily,
        string? productVersion,
        string? productBuild,
        string? productEdition,
        IReadOnlyList<string> severity,
        bool exploitedOnly,
        bool publiclyDisclosedOnly,
        string? cveContains,
        string? kbContains) {
        meta.Add("year", year);
        meta.Add("month", month);
        meta.Add("release", release);
        meta.Add("product_mapped_filter_applied", !string.IsNullOrWhiteSpace(productFamily));
        if (!string.IsNullOrWhiteSpace(productFamily)) {
            meta.Add("product_family", productFamily);
        }
        if (!string.IsNullOrWhiteSpace(productVersion)) {
            meta.Add("product_version", productVersion);
        }
        if (!string.IsNullOrWhiteSpace(productBuild)) {
            meta.Add("product_build", productBuild);
        }
        if (!string.IsNullOrWhiteSpace(productEdition)) {
            meta.Add("product_edition", productEdition);
        }
        if (severity.Count > 0) {
            meta.Add("severity", string.Join(", ", severity));
        }
        if (exploitedOnly) {
            meta.Add("exploited_only", true);
        }
        if (publiclyDisclosedOnly) {
            meta.Add("publicly_disclosed_only", true);
        }
        if (!string.IsNullOrWhiteSpace(cveContains)) {
            meta.Add("cve_contains", cveContains);
        }
        if (!string.IsNullOrWhiteSpace(kbContains)) {
            meta.Add("kb_contains", kbContains);
        }
    }

    /// <summary>
    /// Caps a row collection by max-results and returns standard scanned/truncated counters.
    /// </summary>
    protected static IReadOnlyList<TRow> CapRows<TRow>(
        IReadOnlyList<TRow> allRows,
        int maxResults,
        out int scanned,
        out bool truncated) {
        return ToolQueryHelpers.CapRows(allRows, maxResults, out scanned, out truncated);
    }

    /// <summary>
    /// Builds the standard auto-column table envelope used by System read tools.
    /// </summary>
    protected static string BuildAutoTableResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool baseTruncated,
        int scanned,
        int maxTop,
        Action<JsonObject>? metaMutate = null) {
        return ToolQueryHelpers.BuildAutoTableResponse(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: maxTop,
            baseTruncated: baseTruncated,
            scanned: scanned,
            metaMutate: metaMutate);
    }

    private static bool TryNormalizePatchSeverity(string input, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        foreach (var allowed in AllowedPatchSeverities) {
            if (allowed.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase)) {
                normalized = allowed;
                return true;
            }
        }

        return false;
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
