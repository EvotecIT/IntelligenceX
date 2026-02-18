using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADPlayground.Gpo;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Base class for Active Directory tools.
/// </summary>
public abstract class ActiveDirectoryToolBase : ToolBase {
    private const int MaxErrorMessageLength = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveDirectoryToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    protected ActiveDirectoryToolBase(ActiveDirectoryToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Gets the configured tool options.
    /// </summary>
    protected ActiveDirectoryToolOptions Options { get; }

    /// <summary>
    /// Resolves optional Active Directory scope arguments with safe fallbacks.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token used for RootDSE reads.</param>
    /// <returns>Resolved domain controller and search base DN.</returns>
    protected (string? DomainController, string? SearchBaseDn) ResolveDomainControllerAndSearchBase(
        JsonObject? arguments,
        CancellationToken cancellationToken) {
        var context = LdapToolContextHelper.ResolveSearchContext(
            explicitDomainController: ToolArgs.GetOptionalTrimmed(arguments, "domain_controller"),
            explicitBaseDn: ToolArgs.GetOptionalTrimmed(arguments, "search_base_dn"),
            defaultDomainController: ToolArgs.NormalizeOptional(Options.DomainController),
            defaultBaseDn: ToolArgs.NormalizeOptional(Options.DefaultSearchBaseDn),
            cancellationToken: cancellationToken);

        return (context.DomainController, context.BaseDn);
    }

    /// <summary>
    /// Resolves tool-requested AD attribute list via ADPlayground policy helper.
    /// </summary>
    protected static List<string> ResolveAttributes(
        JsonObject? arguments,
        string attributesKey,
        IEnumerable<string>? allowedAttributes,
        IEnumerable<string>? defaultAttributes,
        IEnumerable<string>? requiredAttributes,
        int? maxAttributeCount = null) {
        var requested = ToolArgs.ReadStringArray(arguments?.GetArray(attributesKey));
        var resolved = LdapToolContextHelper.ResolveAttributeList(
            requestedAttributes: requested,
            allowedAttributes: allowedAttributes,
            defaultAttributes: defaultAttributes,
            requiredAttributes: requiredAttributes,
            maxAttributeCount: maxAttributeCount);

        return new List<string>(resolved);
    }

    /// <summary>
    /// Serializes a JSON object result with <c>ok=true</c>.
    /// </summary>
    /// <param name="obj">Result object.</param>
    /// <returns>JSON string.</returns>
    protected static string Ok(JsonObject obj) {
        return ToolResponse.Ok(root: obj);
    }

    /// <summary>
    /// Serializes a JSON object result with <c>ok=true</c> and optional UI hints.
    /// </summary>
    /// <param name="root">Root payload fields (kept at the tool output root).</param>
    /// <param name="meta">Optional metadata object.</param>
    /// <param name="summaryMarkdown">Optional markdown summary for UI traces.</param>
    /// <param name="render">Optional render hint object.</param>
    /// <returns>JSON string.</returns>
    protected static string Ok(JsonObject root, JsonObject? meta, string? summaryMarkdown, JsonObject? render) {
        return ToolResponse.Ok(root, meta, summaryMarkdown, render);
    }

    /// <summary>
    /// Serializes a JSON error result with <c>ok=false</c>.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <returns>JSON string.</returns>
    protected static string Error(string? message) {
        var msg = string.IsNullOrWhiteSpace(message) ? "Unknown error" : message!;
        return ToolResponse.Error("error", msg);
    }

    /// <summary>
    /// Serializes a JSON error result with a stable error code.
    /// </summary>
    protected static string Error(string errorCode, string error, IEnumerable<string>? hints = null, bool isTransient = false) {
        return ToolResponse.Error(errorCode, error, hints, isTransient);
    }

    /// <summary>
    /// Executes a query delegate and maps exceptions into a stable error envelope.
    /// </summary>
    protected static bool TryExecute<T>(
        Func<T> action,
        out T result,
        out string? errorResponse,
        string defaultErrorMessage,
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "invalid_argument") {
        if (action is null) {
            throw new ArgumentNullException(nameof(action));
        }

        try {
            result = action();
            errorResponse = null;
            return true;
        } catch (Exception ex) {
            result = default!;
            errorResponse = ErrorFromException(ex, defaultErrorMessage, fallbackErrorCode, invalidOperationErrorCode);
            return false;
        }
    }

    /// <summary>
    /// Builds the standard auto-column table envelope used by AD read tools.
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
        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: maxTop,
            baseTruncated: baseTruncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                metaMutate?.Invoke(meta);
            });
        return response;
    }

    /// <summary>
    /// Caps a row collection by max-results and returns standard scanned/truncated counters.
    /// </summary>
    protected static IReadOnlyList<TRow> CapRows<TRow>(
        IReadOnlyList<TRow> allRows,
        int maxResults,
        out int scanned,
        out bool truncated) {
        scanned = allRows.Count;
        if (scanned <= maxResults) {
            truncated = false;
            return allRows;
        }

        truncated = true;
        return allRows.Take(maxResults).ToArray();
    }

    /// <summary>
    /// Builds filtered + capped policy-attribution rows using consistent configured-value semantics.
    /// </summary>
    protected static IReadOnlyList<PolicyAttribution> PreparePolicyAttributionRows(
        IReadOnlyList<PolicyAttribution> attribution,
        bool includeAttribution,
        bool configuredAttributionOnly,
        int maxResults,
        out int scanned,
        out bool truncated) {
        return PreparePolicyAttributionRows(
            attribution,
            includeAttribution,
            configuredAttributionOnly,
            maxResults,
            additionalUnconfiguredValues: null,
            out scanned,
            out truncated);
    }

    /// <summary>
    /// Builds filtered + capped policy-attribution rows using consistent configured-value semantics.
    /// </summary>
    protected static IReadOnlyList<PolicyAttribution> PreparePolicyAttributionRows(
        IReadOnlyList<PolicyAttribution> attribution,
        bool includeAttribution,
        bool configuredAttributionOnly,
        int maxResults,
        IReadOnlyList<string>? additionalUnconfiguredValues,
        out int scanned,
        out bool truncated) {
        var filtered = includeAttribution
            ? attribution
                .Where(static row => row is not null)
                .Where(row => !configuredAttributionOnly || IsConfiguredAttributionValue(row.Effective, additionalUnconfiguredValues))
                .ToArray()
            : Array.Empty<PolicyAttribution>();

        scanned = filtered.Length;
        if (scanned <= maxResults) {
            truncated = false;
            return filtered;
        }

        truncated = true;
        return filtered.Take(maxResults).ToArray();
    }

    /// <summary>
    /// Adds standard policy-attribution query metadata keys.
    /// </summary>
    protected static void AddStandardPolicyAttributionMeta(
        JsonObject meta,
        string domainName,
        bool includeAttribution,
        bool configuredAttributionOnly,
        int maxResults) {
        meta.Add("domain_name", domainName);
        meta.Add("include_attribution", includeAttribution);
        meta.Add("configured_attribution_only", configuredAttributionOnly);
        meta.Add("max_results", maxResults);
    }

    /// <summary>
    /// Determines whether policy-attribution effective value is configured.
    /// </summary>
    protected static bool IsConfiguredAttributionValue(string? effectiveValue, IReadOnlyList<string>? additionalUnconfiguredValues = null) {
        if (string.IsNullOrWhiteSpace(effectiveValue)) {
            return false;
        }

        var trimmed = effectiveValue.Trim();
        if (trimmed.StartsWith("Not configured", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (additionalUnconfiguredValues is null || additionalUnconfiguredValues.Count == 0) {
            return true;
        }

        for (var i = 0; i < additionalUnconfiguredValues.Count; i++) {
            var candidate = additionalUnconfiguredValues[i];
            if (string.IsNullOrWhiteSpace(candidate)) {
                continue;
            }

            if (string.Equals(trimmed, candidate.Trim(), StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Maps exceptions to stable tool-error envelopes and sanitized messages.
    /// </summary>
    protected static string ErrorFromException(
        Exception exception,
        string defaultMessage = "Active Directory query failed.",
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "invalid_argument") {
        if (exception is null) {
            throw new ArgumentNullException(nameof(exception));
        }

        var safeDefault = string.IsNullOrWhiteSpace(defaultMessage)
            ? "Active Directory query failed."
            : defaultMessage.Trim();

        return exception switch {
            OperationCanceledException => Error("cancelled", "Operation was cancelled.", isTransient: true),
            ArgumentException => Error("invalid_argument", SanitizeErrorMessage(exception.Message, safeDefault)),
            InvalidOperationException => Error(
                string.IsNullOrWhiteSpace(invalidOperationErrorCode) ? "invalid_argument" : invalidOperationErrorCode,
                SanitizeErrorMessage(exception.Message, safeDefault)),
            FormatException => Error("invalid_argument", SanitizeErrorMessage(exception.Message, safeDefault)),
            UnauthorizedAccessException => Error("access_denied", SanitizeErrorMessage(exception.Message, "Access denied while querying Active Directory.")),
            TimeoutException => Error("timeout", SanitizeErrorMessage(exception.Message, "Active Directory query timed out."), isTransient: true),
            NotSupportedException => Error("not_supported", SanitizeErrorMessage(exception.Message, safeDefault)),
            _ => Error(
                string.IsNullOrWhiteSpace(fallbackErrorCode) ? "query_failed" : fallbackErrorCode,
                safeDefault)
        };
    }

    /// <summary>
    /// Compacts and bounds an error message to keep tool envelopes stable/safe.
    /// </summary>
    protected static string SanitizeErrorMessage(string? message, string fallback) {
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

    /// <summary>
    /// Normalizes a collector exception message before adding it to per-domain/per-target error rows.
    /// </summary>
    protected static string ToCollectorErrorMessage(Exception? exception, string fallback = "Active Directory query failed.") {
        return SanitizeErrorMessage(exception?.Message, fallback);
    }
}
