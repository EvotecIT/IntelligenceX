using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.PatchDetails;
using ComputerX.Services;
using ComputerX.Updates;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Base class for system tools with shared option validation.
/// </summary>
public abstract class SystemToolBase : ToolBase {
    private static readonly string[] AllowedPatchSeverities = {
        "Critical",
        "Important",
        "Moderate",
        "Low"
    };

    private static readonly IReadOnlyDictionary<string, ServiceEngine> ServiceEngineByName =
        new Dictionary<string, ServiceEngine>(StringComparer.OrdinalIgnoreCase) {
            ["auto"] = ServiceEngine.Auto,
            ["native"] = ServiceEngine.Native,
            ["wmi"] = ServiceEngine.Wmi,
            ["cim"] = ServiceEngine.Cim
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
        return ToolExceptionMapper.ErrorFromException(
            exception,
            defaultMessage: string.IsNullOrWhiteSpace(defaultMessage) ? "System query failed." : defaultMessage,
            unauthorizedMessage: "Access denied while querying system data.",
            timeoutMessage: "System query timed out.",
            fallbackErrorCode: fallbackErrorCode,
            invalidOperationErrorCode: invalidOperationErrorCode);
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
    /// Resolves a standard option-bounded limit argument (default + cap from <see cref="SystemToolOptions.MaxResults"/>).
    /// </summary>
    protected int ResolveBoundedOptionLimit(JsonObject? arguments, string argumentName, int minInclusive = 1) {
        return ToolArgs.GetOptionBoundedInt32(arguments, argumentName, Options.MaxResults, minInclusive);
    }

    /// <summary>
    /// Resolves max_results using the default option-bounded limit behavior.
    /// </summary>
    protected int ResolveMaxResults(JsonObject? arguments) {
        return ResolveBoundedOptionLimit(arguments, "max_results");
    }

    /// <summary>
    /// Adds computer_name metadata consistently across system tool responses.
    /// </summary>
    protected static void AddComputerNameMeta(JsonObject meta, string target) {
        meta.Add("computer_name", target);
    }

    /// <summary>
    /// Adds include/pending-local metadata consistently across system update responses.
    /// </summary>
    protected static void AddPendingLocalMeta(JsonObject meta, bool includePendingLocal, bool pendingIncluded) {
        meta.Add("include_pending_local", includePendingLocal);
        meta.Add("pending_included", pendingIncluded);
    }

    /// <summary>
    /// Adds language-neutral chaining/discovery metadata for read-only system posture tools.
    /// </summary>
    protected static void AddReadOnlyPostureChainingMeta(
        JsonObject meta,
        string currentTool,
        string targetComputer,
        bool isRemoteScope,
        int scanned,
        bool truncated) {
        if (meta is null) {
            throw new ArgumentNullException(nameof(meta));
        }

        var normalizedTool = string.IsNullOrWhiteSpace(currentTool) ? "system_info" : currentTool.Trim();
        var normalizedTarget = ResolveTargetComputerName(targetComputer);
        var scope = isRemoteScope ? "remote" : "local";

        var nextActions = new List<ToolNextActionModel>();
        if (string.Equals(normalizedTool, "system_updates_installed", StringComparison.OrdinalIgnoreCase)) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "system_patch_compliance",
                reason: "Correlate installed updates with monthly MSRC coverage and missing high-risk patches.",
                suggestedArguments: BuildSystemTargetSuggestedArguments(normalizedTarget, isRemoteScope),
                mutating: false));
        } else {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "system_updates_installed",
                reason: "Baseline installed updates before deeper security posture checks.",
                suggestedArguments: BuildSystemTargetSuggestedArguments(
                    normalizedTarget,
                    isRemoteScope,
                    ("include_pending_local", false)),
                mutating: false));
        }

        nextActions.Add(ToolChainingHints.NextAction(
            tool: "system_security_options",
            reason: "Capture registry-backed security-option posture for hardening review.",
            suggestedArguments: BuildSystemTargetSuggestedArguments(normalizedTarget, isRemoteScope),
            mutating: false));

        var chain = ToolChainingHints.Create(
            nextActions: nextActions,
            confidence: truncated ? 0.70d : 0.88d,
            checkpoint: ToolChainingHints.Map(
                ("current_tool", normalizedTool),
                ("scope", scope),
                ("computer_name", normalizedTarget),
                ("rows", scanned),
                ("truncated", truncated)));

        var nextActionsJson = new JsonArray();
        for (var i = 0; i < chain.NextActions.Count; i++) {
            nextActionsJson.Add(ToolJson.ToJsonObjectSnakeCase(chain.NextActions[i]));
        }
        meta.Add("next_actions", nextActionsJson);
        meta.Add("discovery_status", ToolJson.ToJsonObjectSnakeCase(new {
            scope,
            computer_name = normalizedTarget,
            current_tool = normalizedTool,
            rows = scanned,
            truncated
        }));
        meta.Add("chain_confidence", chain.Confidence);
    }

    private static IReadOnlyDictionary<string, string> BuildSystemTargetSuggestedArguments(
        string targetComputer,
        bool isRemoteScope,
        params (string Key, object? Value)[] extras) {
        var entries = new List<(string Key, object? Value)>();
        if (isRemoteScope) {
            entries.Add(("computer_name", targetComputer));
        }

        for (var i = 0; i < extras.Length; i++) {
            entries.Add(extras[i]);
        }

        return ToolChainingHints.Map(entries.ToArray());
    }

    /// <summary>
    /// Builds common facts-view metadata with computer_name and optional extra fields.
    /// </summary>
    protected static JsonObject BuildFactsMeta(
        int count,
        bool truncated,
        string target,
        Action<JsonObject>? mutate = null) {
        var meta = ToolOutputHints.Meta(count: count, truncated: truncated);
        AddComputerNameMeta(meta, target);
        mutate?.Invoke(meta);
        return meta;
    }

    /// <summary>
    /// Returns a standardized not-supported error when a system tool is invoked on non-Windows hosts.
    /// </summary>
    protected static string? ValidateWindowsSupport(string toolName) {
        if (OperatingSystem.IsWindows()) {
            return null;
        }

        var safeToolName = string.IsNullOrWhiteSpace(toolName) ? "This tool" : toolName.Trim();
        return ToolResponse.Error("not_supported", $"{safeToolName} is available only on Windows hosts.");
    }

    /// <summary>
    /// Resolves local/remote target display name from optional computer_name argument.
    /// </summary>
    protected static string ResolveTargetComputerName(string? computerName) {
        return string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName;
    }

    /// <summary>
    /// Resolves timeout arguments with shared bounds used by system inventory tools.
    /// </summary>
    protected static int ResolveTimeoutMs(
        JsonObject? arguments,
        string argumentName = "timeout_ms",
        int defaultValue = 10_000,
        int minInclusive = 200,
        int maxInclusive = 120_000) {
        return ToolArgs.GetCappedInt32(arguments, argumentName, defaultValue, minInclusive, maxInclusive);
    }

    /// <summary>
    /// Resolves an optional service-engine selector for tools backed by ComputerX.Services.
    /// </summary>
    protected static bool TryResolveServiceEngine(
        ToolArgumentReader reader,
        string argumentName,
        out ServiceEngine engine,
        out string? errorResponse) {
        if (!ToolEnumBinders.TryParseOptional(
                reader.OptionalString(argumentName),
                ServiceEngineByName,
                argumentName,
                out ServiceEngine? parsedEngine,
                out errorResponse)) {
            engine = ServiceEngine.Auto;
            return false;
        }

        engine = parsedEngine ?? ServiceEngine.Auto;
        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Normalizes a service-engine enum value into the lowercase contract form exposed by tool metadata.
    /// </summary>
    protected static string NormalizeServiceEngine(ServiceEngine engine) =>
        engine switch {
            ServiceEngine.Native => "native",
            ServiceEngine.Wmi => "wmi",
            ServiceEngine.Cim => "cim",
            _ => "auto"
        };

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

    /// <summary>
    /// Determines whether the requested target should be treated as local machine execution.
    /// </summary>
    protected static bool IsLocalTarget(string? computerName, string target) {
        return string.IsNullOrWhiteSpace(computerName)
               || string.Equals(computerName, ".", StringComparison.Ordinal)
               || string.Equals(target, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}
