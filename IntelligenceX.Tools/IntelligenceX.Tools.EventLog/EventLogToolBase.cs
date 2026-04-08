using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string PlatformNotSupportedErrorCode = "platform_not_supported";

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
        return ErrorFromCatalogFailure(
            failure: failure,
            machineName: null,
            listingKind: "event log catalog");
    }

    /// <summary>
    /// Maps EventViewerX inventory query failures to remote/local-aware tool error envelopes.
    /// </summary>
    protected static string ErrorFromCatalogFailure(
        EventCatalogFailure? failure,
        string? machineName,
        string listingKind) {
        var platformNotSupported = IsPlatformNotSupportedFailure(
            failureKindName: failure?.Kind.ToString(),
            failureMessage: failure?.Message);
        var errorCode = platformNotSupported
            ? PlatformNotSupportedErrorCode
            : ToolFailureMapper.MapCode(failure?.Kind.ToString(), fallbackErrorCode: "query_failed");
        var normalizedMachine = NormalizeOptionalMachineName(machineName);
        var remote = normalizedMachine is not null;
        var normalizedListingKind = string.IsNullOrWhiteSpace(listingKind) ? "event log catalog" : listingKind.Trim();

        var messagePrefix = remote
            ? $"Remote {normalizedListingKind} query failed on machine '{normalizedMachine}'."
            : $"Local {normalizedListingKind} query failed.";
        var safeReason = ToolExceptionMapper.SanitizeErrorMessage(failure?.Message, "Event log catalog query failed.");
        var error = string.IsNullOrWhiteSpace(safeReason)
            ? messagePrefix
            : $"{messagePrefix} Reason: {safeReason}";

        var hints = BuildRemoteLocalLiveHints(
            errorCode: errorCode,
            remote: remote,
            machineName: normalizedMachine,
            platformNotSupported: platformNotSupported,
            includeChannelCatalogHint: false,
            includeLiveScopeHint: false,
            includeEvtxFallbackHint: true);
        return ToolResponse.Error(
            errorCode,
            error,
            hints: hints,
            isTransient: string.Equals(errorCode, "timeout", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps EventViewerX live query failures to tool error envelopes.
    /// </summary>
    protected static string ErrorFromLiveQueryFailure(LiveEventQueryFailure? failure) {
        return ErrorFromLiveQueryFailure(
            failure: failure,
            machineName: null,
            logName: null);
    }

    /// <summary>
    /// Maps EventViewerX live query failures to remote/local-aware tool error envelopes.
    /// </summary>
    protected static string ErrorFromLiveQueryFailure(
        LiveEventQueryFailure? failure,
        string? machineName,
        string? logName) {
        return ErrorFromLiveOperationFailure(
            operationName: "query",
            failureKindName: failure?.Kind.ToString(),
            failureMessage: failure?.Message,
            machineName: machineName,
            logName: logName);
    }

    /// <summary>
    /// Maps EventViewerX live stats failures to tool error envelopes.
    /// </summary>
    protected static string ErrorFromLiveStatsFailure(LiveStatsQueryFailure? failure) {
        return ErrorFromLiveStatsFailure(
            failure: failure,
            machineName: null,
            logName: null);
    }

    /// <summary>
    /// Maps EventViewerX live stats failures to remote/local-aware tool error envelopes.
    /// </summary>
    protected static string ErrorFromLiveStatsFailure(
        LiveStatsQueryFailure? failure,
        string? machineName,
        string? logName) {
        return ErrorFromLiveOperationFailure(
            operationName: "stats query",
            failureKindName: failure?.Kind.ToString(),
            failureMessage: failure?.Message,
            machineName: machineName,
            logName: logName);
    }

    private static string ErrorFromLiveOperationFailure(
        string operationName,
        string? failureKindName,
        string? failureMessage,
        string? machineName,
        string? logName) {
        var platformNotSupported = IsPlatformNotSupportedFailure(failureKindName, failureMessage);
        var errorCode = platformNotSupported
            ? PlatformNotSupportedErrorCode
            : ToolFailureMapper.MapCode(failureKindName, fallbackErrorCode: "query_failed");
        var normalizedMachine = NormalizeOptionalMachineName(machineName);
        var normalizedLogName = string.IsNullOrWhiteSpace(logName) ? "requested event log channel" : logName.Trim();
        var remote = normalizedMachine is not null;

        var messagePrefix = remote
            ? $"Remote event log {operationName} failed for log '{normalizedLogName}' on machine '{normalizedMachine}'."
            : $"Local event log {operationName} failed for log '{normalizedLogName}'.";
        var safeReason = ToolExceptionMapper.SanitizeErrorMessage(failureMessage, "Event log query failed.");
        var error = string.IsNullOrWhiteSpace(safeReason)
            ? messagePrefix
            : $"{messagePrefix} Reason: {safeReason}";

        var hints = BuildRemoteLocalLiveHints(
            errorCode: errorCode,
            remote: remote,
            machineName: normalizedMachine,
            platformNotSupported: platformNotSupported,
            includeChannelCatalogHint: true,
            includeLiveScopeHint: true,
            includeEvtxFallbackHint: true);
        return ToolResponse.Error(
            errorCode,
            error,
            hints: hints,
            isTransient: string.Equals(errorCode, "timeout", StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeOptionalMachineName(string? machineName) {
        if (string.IsNullOrWhiteSpace(machineName)) {
            return null;
        }

        return machineName.Trim();
    }

    private static IReadOnlyList<string> BuildRemoteLocalLiveHints(
        string errorCode,
        bool remote,
        string? machineName,
        bool platformNotSupported,
        bool includeChannelCatalogHint,
        bool includeLiveScopeHint,
        bool includeEvtxFallbackHint) {
        var hints = new List<string>(6);
        if (platformNotSupported) {
            hints.Add("This runtime cannot perform live Windows Event Log reads on the current platform/runtime.");
            hints.Add(remote
                ? "Retry from a Windows-native runtime/host that supports Event Log APIs for remote live reads."
                : "Retry from a Windows-native runtime/host that supports Event Log APIs for local live reads.");
            if (includeEvtxFallbackHint) {
                hints.Add("If you only need analysis, export .evtx from the target and analyze locally with eventlog_evtx_query/eventlog_evtx_stats/eventlog_timeline_query.");
            }

            return hints
                .Where(static hint => !string.IsNullOrWhiteSpace(hint))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (remote) {
            if (!string.IsNullOrWhiteSpace(machineName)) {
                hints.Add($"Verify machine_name '{machineName}' resolves and is reachable from this host.");
            } else {
                hints.Add("Verify machine_name resolves and is reachable from this host.");
            }
            hints.Add("Ensure Remote Event Log Management / RPC access is enabled on the target host.");
            hints.Add("Use a runtime identity with permission to read target event logs.");
        } else {
            hints.Add("Verify this runtime identity can read the requested local event log channel.");
        }

        if (includeChannelCatalogHint) {
            hints.Add(remote
                ? "Call eventlog_channels_list with the same machine_name to confirm channel visibility."
                : "Call eventlog_channels_list to confirm the channel exists locally.");
        }

        if (string.Equals(errorCode, "timeout", StringComparison.OrdinalIgnoreCase)) {
            hints.Add("Increase session_timeout_ms and retry.");
        } else if (string.Equals(errorCode, "access_denied", StringComparison.OrdinalIgnoreCase)) {
            hints.Add("If access is denied, switch to an account/profile with Event Log read rights.");
        }

        if (includeLiveScopeHint) {
            hints.Add("Reduce query scope (for example smaller time window or lower max_events/max_events_scanned) and retry.");
        }

        if (includeEvtxFallbackHint) {
            hints.Add("If live remote access remains blocked, export .evtx from the target and analyze locally with eventlog_evtx_query/eventlog_evtx_stats/eventlog_timeline_query.");
        }

        return hints
            .Where(static hint => !string.IsNullOrWhiteSpace(hint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPlatformNotSupportedFailure(string? failureKindName, string? failureMessage) {
        if (string.Equals(
                ToolFailureMapper.MapCode(failureKindName, fallbackErrorCode: string.Empty),
                "unsupported_platform",
                StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.IsNullOrWhiteSpace(failureMessage)) {
            return false;
        }

        return failureMessage.IndexOf("not supported on this platform", StringComparison.OrdinalIgnoreCase) >= 0
            || failureMessage.IndexOf("supported only on Windows", StringComparison.OrdinalIgnoreCase) >= 0
            || failureMessage.IndexOf("platform not supported", StringComparison.OrdinalIgnoreCase) >= 0;
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
    /// Builds the standard auto-column table envelope with Event Log projection sanitization.
    /// </summary>
    protected static new string BuildAutoTableResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool baseTruncated,
        int scanned,
        int maxTop,
        Action<JsonObject>? metaMutate = null) {
        var sanitizedArguments = EventLogProjectionArgumentSanitizer.SanitizeProjectionArguments(
            arguments,
            ToolAutoTableColumns.GetColumnKeys<TRow>());
        return ToolQueryHelpers.BuildAutoTableResponse(
            arguments: sanitizedArguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: maxTop,
            baseTruncated: baseTruncated,
            scanned: scanned,
            metaMutate: metaMutate);
    }

    /// <summary>
    /// Builds the standard auto-column table envelope with scanned count inferred from source rows.
    /// </summary>
    protected static new string BuildAutoTableResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool baseTruncated,
        int maxTop,
        Action<JsonObject>? metaMutate = null) {
        return BuildAutoTableResponse(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            baseTruncated: baseTruncated,
            scanned: sourceRows.Count,
            maxTop: maxTop,
            metaMutate: metaMutate);
    }

    /// <summary>
    /// Sanitizes projection arguments (<c>columns</c>/<c>sort_by</c>) against the row model for Event Log tools.
    /// </summary>
    protected static JsonObject? SanitizeProjectionArguments<TRow>(JsonObject? arguments, IReadOnlyList<TRow> sourceRows) {
        _ = sourceRows;
        return EventLogProjectionArgumentSanitizer.SanitizeProjectionArguments(
            arguments,
            ToolAutoTableColumns.GetColumnKeys<TRow>());
    }

    /// <summary>
    /// Adds language-neutral chaining/discovery metadata for read-only event-log triage tools.
    /// </summary>
    protected static void AddReadOnlyTriageChainingMeta(
        JsonObject meta,
        string currentTool,
        string logName,
        string? machineName,
        int suggestedMaxEvents,
        int scanned,
        bool truncated,
        string queryMode) {
        if (meta is null) {
            throw new ArgumentNullException(nameof(meta));
        }

        var normalizedTool = string.IsNullOrWhiteSpace(currentTool) ? "eventlog_live_query" : currentTool.Trim();
        var normalizedLogName = string.IsNullOrWhiteSpace(logName) ? "System" : logName.Trim();
        var normalizedMachine = NormalizeOptionalMachineName(machineName);
        var scope = normalizedMachine is null ? "local" : "remote";
        var normalizedQueryMode = string.IsNullOrWhiteSpace(queryMode) ? "default" : queryMode.Trim();
        var boundedMaxEvents = Math.Clamp(suggestedMaxEvents <= 0 ? 100 : suggestedMaxEvents, 1, 5000);

        var nextActions = new List<ToolNextActionModel>();
        if (!string.Equals(normalizedTool, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase)) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "eventlog_live_stats",
                reason: "Summarize dominant Event IDs/providers before drilling deeper.",
                suggestedArguments: BuildEventLogSuggestedArguments(
                    logName: normalizedLogName,
                    machineName: normalizedMachine,
                    additional: ("max_events_scanned", Math.Clamp(boundedMaxEvents * 4, 200, 5000))),
                mutating: false));
        }

        if (!string.Equals(normalizedTool, "eventlog_live_query", StringComparison.OrdinalIgnoreCase)) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "eventlog_live_query",
                reason: "Expand into structured event rows for timeline and root-cause evidence.",
                suggestedArguments: BuildEventLogSuggestedArguments(
                    logName: normalizedLogName,
                    machineName: normalizedMachine,
                    additional: ("max_events", Math.Clamp(boundedMaxEvents, 50, 500))),
                mutating: false));
        }

        if (!string.Equals(normalizedTool, "eventlog_top_events", StringComparison.OrdinalIgnoreCase)) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "eventlog_top_events",
                reason: "Quickly verify latest startup/error signals before broader sweeps.",
                suggestedArguments: BuildEventLogSuggestedArguments(
                    logName: normalizedLogName,
                    machineName: normalizedMachine,
                    additional: ("max_events", Math.Clamp(boundedMaxEvents, 5, 20))),
                mutating: false));
        }

        var chain = ToolChainingHints.Create(
            nextActions: nextActions,
            confidence: truncated ? 0.68d : 0.86d,
            checkpoint: ToolChainingHints.Map(
                ("current_tool", normalizedTool),
                ("scope", scope),
                ("log_name", normalizedLogName),
                ("query_mode", normalizedQueryMode),
                ("rows", scanned),
                ("truncated", truncated)));

        var nextActionsJson = new JsonArray();
        for (var i = 0; i < chain.NextActions.Count; i++) {
            nextActionsJson.Add(ToolJson.ToJsonObjectSnakeCase(chain.NextActions[i]));
        }
        meta.Add("next_actions", nextActionsJson);
        meta.Add("discovery_status", ToolJson.ToJsonObjectSnakeCase(new {
            scope,
            log_name = normalizedLogName,
            machine_name = normalizedMachine ?? string.Empty,
            query_mode = normalizedQueryMode,
            rows = scanned,
            truncated
        }));
        meta.Add("chain_confidence", chain.Confidence);
    }

    private static IReadOnlyDictionary<string, string> BuildEventLogSuggestedArguments(
        string logName,
        string? machineName,
        (string Key, object? Value) additional) {
        var entries = new List<(string Key, object? Value)> {
            ("log_name", logName),
            additional
        };
        if (!string.IsNullOrWhiteSpace(machineName)) {
            entries.Add(("machine_name", machineName));
        }

        return ToolChainingHints.Map(entries.ToArray());
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
                return ErrorFromCatalogFailure(
                    failure: providersFailure,
                    machineName: machineName,
                    listingKind: "event log provider listing");
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
            return ErrorFromCatalogFailure(
                failure: channelsFailure,
                machineName: machineName,
                listingKind: "event log channel listing");
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
