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

/// <summary>
/// Base class for Active Directory tools.
/// </summary>
public abstract class ActiveDirectoryToolBase : ToolBase {
    private const int DefaultPolicyAttributionMaxTop = 5000;

    /// <summary>
    /// Controls how non-positive max-results values are normalized.
    /// </summary>
    protected enum MaxResultsNonPositiveBehavior {
        /// <summary>
        /// Non-positive values fall back to the configured option cap.
        /// </summary>
        DefaultToOptionCap,
        /// <summary>
        /// Non-positive values are clamped to <c>1</c>.
        /// </summary>
        ClampToOne
    }

    /// <summary>
    /// Standard argument set used by AD policy-attribution tools.
    /// </summary>
    protected readonly record struct PolicyAttributionToolRequest(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int MaxResults);

    /// <summary>
    /// Standard argument set used by tools that require a domain and max-results cap.
    /// </summary>
    protected readonly record struct RequiredDomainQueryRequest(
        string DomainName,
        int MaxResults);

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
    /// Resolves max-results with a configurable non-positive normalization strategy.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="argumentName">Argument name (defaults to <c>max_results</c>).</param>
    /// <param name="nonPositiveBehavior">Behavior used when provided value is zero or negative.</param>
    /// <returns>Normalized max-results value.</returns>
    protected int ResolveMaxResults(
        JsonObject? arguments,
        string argumentName = "max_results",
        MaxResultsNonPositiveBehavior nonPositiveBehavior = MaxResultsNonPositiveBehavior.ClampToOne) {
        var behavior = nonPositiveBehavior == MaxResultsNonPositiveBehavior.DefaultToOptionCap
            ? ToolArgs.NonPositiveInt32Behavior.UseDefault
            : ToolArgs.NonPositiveInt32Behavior.ClampToMinimum;

        return ToolArgs.GetOptionBoundedInt32(
            arguments,
            argumentName,
            Options.MaxResults,
            minInclusive: 1,
            nonPositiveBehavior: behavior,
            defaultValue: Options.MaxResults);
    }

    /// <summary>
    /// Reads a required trimmed domain argument and returns a standard invalid-argument envelope on failure.
    /// </summary>
    protected static bool TryReadRequiredDomainName(
        JsonObject? arguments,
        out string domainName,
        out string? errorResponse,
        string argumentName = "domain_name") {
        var key = string.IsNullOrWhiteSpace(argumentName) ? "domain_name" : argumentName.Trim();
        domainName = ToolArgs.GetOptionalTrimmed(arguments, key) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(domainName)) {
            errorResponse = ToolResponse.Error("invalid_argument", $"{key} is required.");
            return false;
        }

        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Reads required domain + max-results arguments using a single validation path.
    /// </summary>
    protected bool TryReadRequiredDomainQueryRequest(
        JsonObject? arguments,
        out RequiredDomainQueryRequest request,
        out string? errorResponse,
        string domainArgumentName = "domain_name",
        string maxResultsArgumentName = "max_results",
        MaxResultsNonPositiveBehavior nonPositiveBehavior = MaxResultsNonPositiveBehavior.ClampToOne) {
        if (!TryReadRequiredDomainName(arguments, out var domainName, out errorResponse, argumentName: domainArgumentName)) {
            request = default;
            return false;
        }

        request = new RequiredDomainQueryRequest(
            DomainName: domainName,
            MaxResults: ResolveMaxResults(arguments, maxResultsArgumentName, nonPositiveBehavior));
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Reads optional domain/forest scope arguments using shared key names and trimming semantics.
    /// </summary>
    protected static void ReadDomainAndForestScope(
        JsonObject? arguments,
        out string? domainName,
        out string? forestName) {
        domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
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
    /// Maps AD collection-view success/error fields to a standardized query_failed envelope.
    /// </summary>
    protected static bool TryMapCollectionFailure(
        bool collectionSucceeded,
        string? collectionError,
        string defaultErrorMessage,
        out string? errorResponse) {
        if (collectionSucceeded) {
            errorResponse = null;
            return true;
        }

        var message = string.IsNullOrWhiteSpace(collectionError)
            ? defaultErrorMessage
            : collectionError!;
        errorResponse = ToolResponse.Error("query_failed", message);
        return false;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when collection did not succeed.
    /// </summary>
    protected static void ThrowIfCollectionFailed(
        bool collectionSucceeded,
        string? collectionError,
        string defaultErrorMessage) {
        if (collectionSucceeded) {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(collectionError)
                ? defaultErrorMessage
                : collectionError!);
    }

    /// <summary>
    /// Executes a query that returns a conventional AD collection view and maps failures consistently.
    /// </summary>
    protected static bool TryExecuteCollectionQuery<T>(
        Func<T> query,
        out T result,
        out string? errorResponse,
        string defaultErrorMessage,
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "query_failed") {
        if (!TryExecute(
                action: query,
                result: out result,
                errorResponse: out errorResponse,
                defaultErrorMessage: defaultErrorMessage,
                fallbackErrorCode: fallbackErrorCode,
                invalidOperationErrorCode: invalidOperationErrorCode)) {
            return false;
        }

        return TryMapCollectionFailureByConvention(result, defaultErrorMessage, out errorResponse);
    }

    /// <summary>
    /// Executes a query that returns an AD collection view and maps failures using typed selectors.
    /// </summary>
    protected static bool TryExecuteCollectionQuery<T>(
        Func<T> query,
        Func<T, bool> collectionSucceededSelector,
        Func<T, string?> collectionErrorSelector,
        out T result,
        out string? errorResponse,
        string defaultErrorMessage,
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "query_failed") {
        if (collectionSucceededSelector is null) {
            throw new ArgumentNullException(nameof(collectionSucceededSelector));
        }
        if (collectionErrorSelector is null) {
            throw new ArgumentNullException(nameof(collectionErrorSelector));
        }

        if (!TryExecute(
                action: query,
                result: out result,
                errorResponse: out errorResponse,
                defaultErrorMessage: defaultErrorMessage,
                fallbackErrorCode: fallbackErrorCode,
                invalidOperationErrorCode: invalidOperationErrorCode)) {
            return false;
        }

        bool collectionSucceeded;
        string? collectionError;
        try {
            collectionSucceeded = collectionSucceededSelector(result);
            collectionError = collectionErrorSelector(result);
        } catch (Exception ex) {
            errorResponse = ErrorFromException(ex, defaultErrorMessage, fallbackErrorCode, invalidOperationErrorCode);
            return false;
        }

        return TryMapCollectionFailure(collectionSucceeded, collectionError, defaultErrorMessage, out errorResponse);
    }

    /// <summary>
    /// Maps failures from views exposing <c>CollectionSucceeded</c>/<c>CollectionError</c> members.
    /// </summary>
    protected static bool TryMapCollectionFailureByConvention<T>(
        T view,
        string defaultErrorMessage,
        out string? errorResponse) {
        if (!TryReadCollectionViewState(view, out var collectionSucceeded, out var collectionError)) {
            errorResponse = ToolResponse.Error("query_failed", "Collection view contract is invalid.");
            return false;
        }

        return TryMapCollectionFailure(
            collectionSucceeded,
            collectionError,
            defaultErrorMessage,
            out errorResponse);
    }

    private static bool TryReadCollectionViewState<T>(
        T view,
        out bool collectionSucceeded,
        out string? collectionError) {
        collectionSucceeded = false;
        collectionError = null;
        if (view is null) {
            return false;
        }

        var type = view.GetType();
        var collectionSucceededProperty = type.GetProperty("CollectionSucceeded");
        var collectionErrorProperty = type.GetProperty("CollectionError");
        if (collectionSucceededProperty is null ||
            collectionSucceededProperty.PropertyType != typeof(bool) ||
            collectionErrorProperty is null ||
            collectionErrorProperty.PropertyType != typeof(string)) {
            return false;
        }

        var succeededValue = collectionSucceededProperty.GetValue(view);
        if (succeededValue is not bool succeeded) {
            return false;
        }

        collectionSucceeded = succeeded;
        collectionError = collectionErrorProperty.GetValue(view) as string;
        return true;
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
    /// Executes the common AD rows-view pipeline:
    /// parse required domain, run typed collection query, cap rows, build result, and emit table response.
    /// </summary>
    protected Task<string> ExecuteDomainRowsViewTool<TView, TRow, TResult>(
        JsonObject? arguments,
        CancellationToken cancellationToken,
        string title,
        string defaultErrorMessage,
        Func<string, TView> query,
        Func<TView, bool> collectionSucceededSelector,
        Func<TView, string?> collectionErrorSelector,
        Func<TView, IReadOnlyList<TRow>> allRowsSelector,
        Func<string, TView, IReadOnlyList<TRow>, IReadOnlyList<TRow>, int, bool, TResult> resultFactory,
        Action<JsonObject, string, int, TView>? additionalMetaMutate = null,
        int maxTop = DefaultPolicyAttributionMaxTop,
        string viewRowsPath = "rows_view",
        string invalidOperationErrorCode = "query_failed") {
        cancellationToken.ThrowIfCancellationRequested();

        if (query is null) {
            throw new ArgumentNullException(nameof(query));
        }
        if (collectionSucceededSelector is null) {
            throw new ArgumentNullException(nameof(collectionSucceededSelector));
        }
        if (collectionErrorSelector is null) {
            throw new ArgumentNullException(nameof(collectionErrorSelector));
        }
        if (allRowsSelector is null) {
            throw new ArgumentNullException(nameof(allRowsSelector));
        }
        if (resultFactory is null) {
            throw new ArgumentNullException(nameof(resultFactory));
        }

        if (!TryReadRequiredDomainQueryRequest(arguments, out var request, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var domainName = request.DomainName;
        var maxResults = request.MaxResults;

        if (!TryExecuteCollectionQuery(
                query: () => query(domainName),
                collectionSucceededSelector: collectionSucceededSelector,
                collectionErrorSelector: collectionErrorSelector,
                result: out TView view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: defaultErrorMessage,
                invalidOperationErrorCode: invalidOperationErrorCode)) {
            return Task.FromResult(errorResponse!);
        }

        var allRows = allRowsSelector(view) ?? Array.Empty<TRow>();
        var rows = CapRows(allRows, maxResults, out var scanned, out var truncated);
        var result = resultFactory(domainName, view, allRows, rows, scanned, truncated);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: maxTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                additionalMetaMutate?.Invoke(meta, domainName, maxResults, view);
            }));
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
    /// Builds a projected key set from row values using case-insensitive matching.
    /// </summary>
    protected static HashSet<string> BuildProjectedSet<TRow>(
        IReadOnlyList<TRow> rows,
        Func<TRow, string?> keySelector) {
        return ToolQueryHelpers.BuildProjectedSet(rows, keySelector);
    }

    /// <summary>
    /// Filters details by a projected key set.
    /// </summary>
    protected static IReadOnlyList<TDetail> FilterByProjectedSet<TDetail>(
        IReadOnlyList<TDetail> details,
        IReadOnlySet<string> projectedKeys,
        Func<TDetail, string?> keySelector) {
        return ToolQueryHelpers.FilterByProjectedSet(details, projectedKeys, keySelector);
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
        AddMaxResultsMeta(meta, maxResults);
    }

    /// <summary>
    /// Adds optional <c>domain_name</c> and <c>forest_name</c> metadata keys when values are present.
    /// </summary>
    protected static void AddDomainAndForestMeta(JsonObject meta, string? domainName, string? forestName) {
        AddOptionalStringMeta(meta, "domain_name", domainName);
        AddOptionalStringMeta(meta, "forest_name", forestName);
    }

    /// <summary>
    /// Adds standard <c>max_results</c> plus optional <c>domain_name</c>/<c>forest_name</c> metadata keys.
    /// </summary>
    protected static void AddDomainAndForestAndMaxResultsMeta(
        JsonObject meta,
        string? domainName,
        string? forestName,
        int maxResults) {
        AddMaxResultsMeta(meta, maxResults);
        AddDomainAndForestMeta(meta, domainName, forestName);
    }

    /// <summary>
    /// Adds standard <c>domain_name</c> and <c>max_results</c> metadata keys.
    /// </summary>
    protected static void AddDomainAndMaxResultsMeta(JsonObject meta, string domainName, int maxResults) {
        AddOptionalStringMeta(meta, "domain_name", domainName);
        AddMaxResultsMeta(meta, maxResults);
    }

    /// <summary>
    /// Adds an optional string metadata key when value is non-empty.
    /// </summary>
    protected static void AddOptionalStringMeta(JsonObject meta, string key, string? value) {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key)) {
            return;
        }

        meta.Add(key, value);
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
        return ToolExceptionMapper.ErrorFromException(
            exception,
            defaultMessage: string.IsNullOrWhiteSpace(defaultMessage) ? "Active Directory query failed." : defaultMessage,
            unauthorizedMessage: "Access denied while querying Active Directory.",
            timeoutMessage: "Active Directory query timed out.",
            fallbackErrorCode: fallbackErrorCode,
            invalidOperationErrorCode: invalidOperationErrorCode);
    }

    /// <summary>
    /// Compacts and bounds an error message to keep tool envelopes stable/safe.
    /// </summary>
    protected static string SanitizeErrorMessage(string? message, string fallback) {
        return ToolExceptionMapper.SanitizeErrorMessage(message, fallback);
    }

    /// <summary>
    /// Normalizes a collector exception message before adding it to per-domain/per-target error rows.
    /// </summary>
    protected static string ToCollectorErrorMessage(Exception? exception, string fallback = "Active Directory query failed.") {
        return SanitizeErrorMessage(exception?.Message, fallback);
    }

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
        errorResponse = ToolResponse.Error(
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
