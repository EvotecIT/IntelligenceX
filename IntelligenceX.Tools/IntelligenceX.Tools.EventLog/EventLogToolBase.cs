using System;
using System.Collections.Generic;
using System.Threading;
using EventViewerX.Reports.Inventory;
using EventViewerX.Reports.Evtx;
using EventViewerX.Reports.Live;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Base class for event log tools with safe-by-default path resolution.
/// </summary>
public abstract class EventLogToolBase : ToolBase {
    /// <summary>
    /// Shared default remote session timeout in milliseconds.
    /// </summary>
    protected const int DefaultRemoteSessionTimeoutMs = 30_000;

    /// <summary>
    /// Shared minimum session timeout in milliseconds.
    /// </summary>
    protected const int MinSessionTimeoutMs = 250;

    /// <summary>
    /// Shared maximum session timeout in milliseconds.
    /// </summary>
    protected const int MaxSessionTimeoutMs = 300_000;

    /// <summary>
    /// Shared options for event log tools.
    /// </summary>
    protected readonly EventLogToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    protected EventLogToolBase(EventLogToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Resolves and validates an EVTX file path (safe-by-default; requires AllowedRoots).
    /// </summary>
    protected bool TryResolveEvtxPath(string inputPath, out string fullPath, out string errorCode, out string error, out string[]? hints) {
        // Keep EVTX semantics centralized so both live and EVTX tools map path failures consistently.
        return ToolPaths.TryResolveAllowedExistingFile(
            inputPath: inputPath,
            allowedRoots: Options.AllowedRoots,
            requiredExtension: ".evtx",
            fullPath: out fullPath,
            errorCode: out errorCode,
            error: out error,
            hints: out hints);
    }

    /// <summary>
    /// Maps EventViewerX EVTX query failures to tool error envelopes.
    /// </summary>
    protected static string ErrorFromEvtxFailure(EvtxQueryFailure? failure) {
        return ToolFailureMapper.ErrorFromFailure(
            failure,
            static x => x.Kind,
            static x => x.Message,
            defaultMessage: "EVTX query failed.",
            fallbackErrorCode: "exception");
    }

    /// <summary>
    /// Maps EventViewerX inventory query failures to tool error envelopes.
    /// </summary>
    protected static string ErrorFromCatalogFailure(EventCatalogFailure? failure) {
        return ToolFailureMapper.ErrorFromFailure(
            failure,
            static x => x.Kind,
            static x => x.Message,
            defaultMessage: "Catalog query failed.",
            fallbackErrorCode: "exception");
    }

    /// <summary>
    /// Maps EventViewerX live query failures to tool error envelopes.
    /// </summary>
    protected static string ErrorFromLiveQueryFailure(LiveEventQueryFailure? failure) {
        return ToolFailureMapper.ErrorFromFailure(
            failure,
            static x => x.Kind,
            static x => x.Message,
            defaultMessage: "Live query failed.",
            fallbackErrorCode: "exception");
    }

    /// <summary>
    /// Maps EventViewerX live stats failures to tool error envelopes.
    /// </summary>
    protected static string ErrorFromLiveStatsFailure(LiveStatsQueryFailure? failure) {
        return ToolFailureMapper.ErrorFromFailure(
            failure,
            static x => x.Kind,
            static x => x.Message,
            defaultMessage: "Live stats query failed.",
            fallbackErrorCode: "exception");
    }

    /// <summary>
    /// Maps common runtime exceptions to stable event-log tool error envelopes.
    /// </summary>
    protected static string ErrorFromException(
        Exception exception,
        string defaultMessage = "Event log query failed.",
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "query_failed") {
        return ToolExceptionMapper.ErrorFromException(
            exception,
            defaultMessage: string.IsNullOrWhiteSpace(defaultMessage) ? "Event log query failed." : defaultMessage,
            unauthorizedMessage: "Access denied while querying event log data.",
            timeoutMessage: "Event log query timed out.",
            fallbackErrorCode: fallbackErrorCode,
            invalidOperationErrorCode: invalidOperationErrorCode);
    }

    /// <summary>
    /// Resolves a standard option-bounded limit argument (default + cap from <see cref="EventLogToolOptions.MaxResults"/>).
    /// </summary>
    protected int ResolveBoundedOptionLimit(JsonObject? arguments, string argumentName, int minInclusive = 1) {
        return ToolArgs.GetOptionBoundedInt32(arguments, argumentName, Options.MaxResults, minInclusive);
    }

    /// <summary>
    /// Resolves <c>max_results</c> using the standard option-bounded behavior.
    /// </summary>
    protected int ResolveOptionBoundedMaxResults(JsonObject? arguments, int minInclusive = 1) {
        return ResolveBoundedOptionLimit(arguments, "max_results", minInclusive);
    }

    /// <summary>
    /// Resolves <c>max_results</c> using an explicit default and optional explicit cap.
    /// </summary>
    protected int ResolveCappedMaxResults(
        JsonObject? arguments,
        int defaultValue,
        int minInclusive = 1,
        int? maxInclusive = null) {
        var normalizedMax = maxInclusive ?? Options.MaxResults;
        return ToolArgs.GetCappedInt32(arguments, "max_results", defaultValue, minInclusive, normalizedMax);
    }

    /// <summary>
    /// Resolves optional session timeout values with shared positive/min/max clamping.
    /// </summary>
    protected static int? ResolveSessionTimeoutMs(long? timeoutRaw, int minInclusive = 250, int maxInclusive = 300_000) {
        var sessionTimeoutMs = ToolArgs.ToPositiveInt32OrNull(timeoutRaw, maxInclusive: maxInclusive);
        if (sessionTimeoutMs.HasValue && sessionTimeoutMs.Value < minInclusive) {
            return minInclusive;
        }

        return sessionTimeoutMs;
    }

    /// <summary>
    /// Resolves optional session timeout values from tool arguments with shared clamping.
    /// </summary>
    protected static int? ResolveSessionTimeoutMs(
        JsonObject? arguments,
        string argumentName = "session_timeout_ms",
        int minInclusive = 250,
        int maxInclusive = 300_000) {
        return ResolveSessionTimeoutMs(arguments?.GetInt64(argumentName), minInclusive, maxInclusive);
    }

    /// <summary>
    /// Resolves an optional XPath argument, defaulting to <c>*</c> when missing/blank.
    /// </summary>
    protected static string ResolveXPathOrDefault(
        JsonObject? arguments,
        string argumentName = "xpath",
        string defaultXPath = "*") {
        var xpath = arguments?.GetString(argumentName);
        return string.IsNullOrWhiteSpace(xpath) ? defaultXPath : xpath;
    }

    /// <summary>
    /// Shared catalog listing flow for channel/provider list tools.
    /// </summary>
    protected string RunCatalogNameList(
        JsonObject? arguments,
        bool providers,
        string maxArgumentName,
        string title,
        string rowsPath,
        string header,
        string columnName,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var nameContains = arguments?.GetString("name_contains");
        var max = ResolveBoundedOptionLimit(arguments, maxArgumentName);

        var machineNameNormalized = (arguments?.GetString("machine_name") ?? string.Empty).Trim();
        string? machineName = machineNameNormalized.Length == 0 ? null : machineNameNormalized;
        if (machineName is { Length: > 260 }) {
            machineName = machineName.Substring(0, 260);
        }

        var sessionTimeoutMs = ResolveSessionTimeoutMs(arguments, minInclusive: 1000, maxInclusive: 600_000);

        var request = new EventCatalogQueryRequest {
            NameContains = nameContains,
            MaxResults = max,
            MachineName = machineName,
            SessionTimeoutMs = sessionTimeoutMs
        };

        if (providers) {
            if (!EventCatalogQueryExecutor.TryListProviders(
                    request: request,
                    result: out var providersRoot,
                    failure: out var providersFailure,
                    cancellationToken: cancellationToken)) {
                return ErrorFromCatalogFailure(providersFailure);
            }

            var preview = ToolPreview.Table(maxRows: 20, maxCellChars: null);
            foreach (var row in providersRoot.Providers) {
                preview.TryAdd(row.Name);
            }

            return ToolResponse.OkTablePreviewModel(
                model: providersRoot,
                title: title,
                rowsPath: rowsPath,
                headers: new[] { header },
                previewRows: preview.Rows,
                count: providersRoot.Count,
                truncated: providersRoot.Truncated,
                columns: new[] { new ToolColumn(columnName, header, "string") });
        }

        if (!EventCatalogQueryExecutor.TryListChannels(
                request: request,
                result: out var channelsRoot,
                failure: out var channelsFailure,
                cancellationToken: cancellationToken)) {
            return ErrorFromCatalogFailure(channelsFailure);
        }

        var channelPreview = ToolPreview.Table(maxRows: 20, maxCellChars: null);
        foreach (var row in channelsRoot.Channels) {
            channelPreview.TryAdd(row.Name);
        }

        return ToolResponse.OkTablePreviewModel(
            model: channelsRoot,
            title: title,
            rowsPath: rowsPath,
            headers: new[] { header },
            previewRows: channelPreview.Rows,
            count: channelsRoot.Count,
            truncated: channelsRoot.Truncated,
            columns: new[] { new ToolColumn(columnName, header, "string") });
    }
}
