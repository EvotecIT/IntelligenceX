using System;
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
        var max = ToolArgs.GetCappedInt32(arguments, maxArgumentName, Options.MaxResults, 1, Options.MaxResults);

        var request = new EventCatalogQueryRequest {
            NameContains = nameContains,
            MaxResults = max
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
