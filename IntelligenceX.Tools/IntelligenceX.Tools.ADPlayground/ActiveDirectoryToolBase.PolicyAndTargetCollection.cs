using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Gpo;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public abstract partial class ActiveDirectoryToolBase : ToolBase {
    /// <summary>
    /// Executes a standard AD policy-attribution query tool pipeline:
    /// parse args, run query, shape attribution rows, and build the table response.
    /// </summary>
    protected Task<string> ExecutePolicyAttributionTool<TView, TResult>(
        JsonObject? arguments,
        CancellationToken cancellationToken,
        string title,
        string defaultErrorMessage,
        Func<string, TView> query,
        Func<TView, IReadOnlyList<PolicyAttribution>> attributionSelector,
        Func<PolicyAttributionToolRequest, TView, int, bool, IReadOnlyList<PolicyAttribution>, TResult> resultFactory,
        Action<JsonObject, PolicyAttributionToolRequest, TView, TResult>? additionalMetaMutate = null,
        IReadOnlyList<string>? additionalUnconfiguredValues = null,
        string invalidOperationErrorCode = "query_failed",
        int maxTop = DefaultPolicyAttributionMaxTop) {
        cancellationToken.ThrowIfCancellationRequested();

        if (query is null) {
            throw new ArgumentNullException(nameof(query));
        }
        if (attributionSelector is null) {
            throw new ArgumentNullException(nameof(attributionSelector));
        }
        if (resultFactory is null) {
            throw new ArgumentNullException(nameof(resultFactory));
        }

        if (!TryReadPolicyAttributionToolRequest(arguments, out var request, out var requestError)) {
            return Task.FromResult(requestError!);
        }

        if (!TryExecute(
                action: () => query(request.DomainName),
                result: out TView view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: defaultErrorMessage,
                invalidOperationErrorCode: invalidOperationErrorCode)) {
            return Task.FromResult(errorResponse!);
        }

        var rows = PreparePolicyAttributionRows(
            attribution: attributionSelector(view),
            includeAttribution: request.IncludeAttribution,
            configuredAttributionOnly: request.ConfiguredAttributionOnly,
            maxResults: request.MaxResults,
            additionalUnconfiguredValues: additionalUnconfiguredValues,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = resultFactory(request, view, scanned, truncated, rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "attribution_view",
            title: title,
            maxTop: maxTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddStandardPolicyAttributionMeta(
                    meta,
                    request.DomainName,
                    request.IncludeAttribution,
                    request.ConfiguredAttributionOnly,
                    request.MaxResults);
                additionalMetaMutate?.Invoke(meta, request, view, result);
            }));
    }

    /// <summary>
    /// Parses standard AD policy-attribution arguments.
    /// </summary>
    protected bool TryReadPolicyAttributionToolRequest(
        JsonObject? arguments,
        out PolicyAttributionToolRequest request,
        out string? errorResponse) {
        if (!TryReadRequiredDomainQueryRequest(arguments, out var domainQuery, out errorResponse)) {
            request = default;
            return false;
        }

        request = new PolicyAttributionToolRequest(
            DomainName: domainQuery.DomainName,
            IncludeAttribution: ToolArgs.GetBoolean(arguments, "include_attribution", defaultValue: true),
            ConfiguredAttributionOnly: ToolArgs.GetBoolean(arguments, "configured_attribution_only", defaultValue: false),
            MaxResults: domainQuery.MaxResults);
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Resolves target domains from an explicit domain name or forest discovery.
    /// </summary>
    protected static bool TryResolveTargetDomains(
        string? domainName,
        string? forestName,
        CancellationToken cancellationToken,
        string queryName,
        out string[] targetDomains,
        out string? errorResponse) {
        targetDomains = string.IsNullOrWhiteSpace(domainName)
            ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[] { domainName! };

        if (targetDomains.Length > 0) {
            errorResponse = null;
            return true;
        }

        var name = string.IsNullOrWhiteSpace(queryName) ? "query" : queryName.Trim();
        errorResponse = ToolResultV2.Error(
            "query_failed",
            $"No domains resolved for {name} query. Provide domain_name or ensure forest discovery is available.");
        return false;
    }

    /// <summary>
    /// Executes per-target collection action with per-target exception mapping.
    /// </summary>
    protected static void RunPerTargetCollection<TTarget, TError>(
        IEnumerable<TTarget> targets,
        Action<TTarget> collect,
        Func<TTarget, Exception, TError> errorFactory,
        ICollection<TError> errors,
        CancellationToken cancellationToken) {
        if (targets is null) {
            throw new ArgumentNullException(nameof(targets));
        }
        if (collect is null) {
            throw new ArgumentNullException(nameof(collect));
        }
        if (errorFactory is null) {
            throw new ArgumentNullException(nameof(errorFactory));
        }
        if (errors is null) {
            throw new ArgumentNullException(nameof(errors));
        }

        foreach (var target in targets) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                collect(target);
            } catch (Exception ex) {
                errors.Add(errorFactory(target, ex));
            }
        }
    }

    /// <summary>
    /// Executes per-target asynchronous collection action with per-target exception mapping.
    /// </summary>
    protected static async Task RunPerTargetCollectionAsync<TTarget, TError>(
        IEnumerable<TTarget> targets,
        Func<TTarget, Task> collectAsync,
        Func<TTarget, Exception, TError> errorFactory,
        ICollection<TError> errors,
        CancellationToken cancellationToken) {
        if (targets is null) {
            throw new ArgumentNullException(nameof(targets));
        }
        if (collectAsync is null) {
            throw new ArgumentNullException(nameof(collectAsync));
        }
        if (errorFactory is null) {
            throw new ArgumentNullException(nameof(errorFactory));
        }
        if (errors is null) {
            throw new ArgumentNullException(nameof(errors));
        }

        foreach (var target in targets) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await collectAsync(target).ConfigureAwait(false);
            } catch (Exception ex) {
                errors.Add(errorFactory(target, ex));
            }
        }
    }
}
