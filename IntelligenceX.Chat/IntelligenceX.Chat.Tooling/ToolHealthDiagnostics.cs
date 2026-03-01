using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using JsonValueKind = System.Text.Json.JsonValueKind;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared helpers for probing <c>*_pack_info</c> tools and normalizing health failures.
/// </summary>
public static class ToolHealthDiagnostics {
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private const string PackInfoSuffix = "_pack_info";

    /// <summary>
    /// Result of a single tool-health probe.
    /// </summary>
    public readonly record struct ProbeResult(
        string ToolName,
        bool Ok,
        string? ErrorCode,
        string? Error,
        long DurationMs);

    /// <summary>
    /// Returns registered pack-info definitions (sorted, case-insensitive).
    /// Prefers routing-role metadata and keeps suffix-based discovery as compatibility fallback.
    /// </summary>
    public static ToolDefinition[] GetPackInfoDefinitions(ToolRegistry registry) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }

        return registry.GetDefinitions()
            .Where(IsPackInfoDefinition)
            .OrderBy(static def => def.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns all registered pack-info tool names (sorted, case-insensitive).
    /// </summary>
    public static string[] GetPackInfoToolNames(ToolRegistry registry) {
        return GetPackInfoDefinitions(registry)
            .Select(static def => def.Name)
            .ToArray();
    }

    /// <summary>
    /// Probes a single tool and returns a normalized health result.
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(ToolRegistry registry, string toolName, int timeoutSeconds, CancellationToken cancellationToken) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }

        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));
        }

        if (timeoutSeconds < ChatRequestOptionLimits.MinTimeoutSeconds || timeoutSeconds > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds),
                $"Timeout must be between {ChatRequestOptionLimits.MinTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds} seconds.");
        }

        var sw = Stopwatch.StartNew();
        try {
            if (!registry.TryGet(normalizedToolName, out var tool)) {
                return new ProbeResult(normalizedToolName, Ok: false, ErrorCode: "tool_not_registered", Error: "Probe tool is not registered.", DurationMs: sw.ElapsedMilliseconds);
            }

            using var timeoutCts = CreateTimeoutCts(cancellationToken, timeoutSeconds);
            var toolToken = timeoutCts?.Token ?? cancellationToken;
            try {
                var raw = await tool.InvokeAsync(new JsonObject(), toolToken).ConfigureAwait(false) ?? string.Empty;
                if (TryReadFailure(raw, out var errorCode, out var error)) {
                    return new ProbeResult(normalizedToolName, Ok: false, ErrorCode: errorCode, Error: error, DurationMs: sw.ElapsedMilliseconds);
                }

                if (TryResolveOperationalSmokeProbe(normalizedToolName, out var smokeToolName, out var smokeArguments)
                    && registry.TryGet(smokeToolName, out var smokeTool)) {
                    var smokeRaw = await smokeTool.InvokeAsync(smokeArguments, toolToken).ConfigureAwait(false) ?? string.Empty;
                    if (TryReadFailure(smokeRaw, out var smokeErrorCode, out var smokeError)) {
                        var resolvedCode = string.IsNullOrWhiteSpace(smokeErrorCode) ? "smoke_probe_failed" : "smoke_" + smokeErrorCode;
                        var resolvedError = CompactOneLine($"{smokeToolName}: {smokeError}");
                        return new ProbeResult(normalizedToolName, Ok: false, ErrorCode: resolvedCode, Error: resolvedError, DurationMs: sw.ElapsedMilliseconds);
                    }
                }

                return new ProbeResult(normalizedToolName, Ok: true, ErrorCode: null, Error: null, DurationMs: sw.ElapsedMilliseconds);
            } catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                var timeoutError = timeoutSeconds <= 0 ? "Tool probe timed out." : $"Tool probe timed out after {timeoutSeconds}s.";
                return new ProbeResult(normalizedToolName, Ok: false, ErrorCode: "tool_timeout", Error: timeoutError, DurationMs: sw.ElapsedMilliseconds);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            return new ProbeResult(normalizedToolName, Ok: false, ErrorCode: ex.GetType().Name, Error: CompactOneLine(ex.Message), DurationMs: sw.ElapsedMilliseconds);
        } finally {
            sw.Stop();
        }
    }

    /// <summary>
    /// Parses standard tool-output envelopes and reports whether they represent a failure.
    /// </summary>
    public static bool TryReadFailure(string output, out string errorCode, out string error) {
        errorCode = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(output)) {
            errorCode = "empty_output";
            error = "Tool returned empty output.";
            return true;
        }

        try {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                errorCode = "invalid_output";
                error = "Tool returned JSON that is not an object.";
                return true;
            }

            if (root.TryGetProperty("ok", out var okProp)) {
                if (okProp.ValueKind == JsonValueKind.True) {
                    return false;
                }

                if (okProp.ValueKind == JsonValueKind.False) {
                    errorCode = root.TryGetProperty("error_code", out var codeProp)
                        ? (codeProp.GetString() ?? "tool_failed")
                        : "tool_failed";
                    error = root.TryGetProperty("error", out var errorProp)
                        ? (errorProp.GetString() ?? "Tool returned ok=false.")
                        : "Tool returned ok=false.";
                    error = CompactOneLine(error);
                    return true;
                }
            }

            if (root.TryGetProperty("error_code", out var explicitCode)) {
                errorCode = explicitCode.GetString() ?? "tool_failed";
                error = root.TryGetProperty("error", out var explicitError)
                    ? (explicitError.GetString() ?? "Tool reported an error code.")
                    : "Tool reported an error code.";
                error = CompactOneLine(error);
                return true;
            }

            return false;
        } catch (JsonException ex) {
            errorCode = "invalid_json";
            error = CompactOneLine(ex.Message);
            return true;
        }
    }

    /// <summary>
    /// Converts arbitrary text into a compact single-line string.
    /// </summary>
    public static string CompactOneLine(string? text, int maxChars = 220) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        var compact = WhitespaceRegex.Replace(text.Trim(), " ");
        if (maxChars <= 0 || compact.Length <= maxChars) {
            return compact;
        }

        return compact[..maxChars];
    }

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken cancellationToken, int timeoutSeconds) {
        if (timeoutSeconds <= 0) {
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return cts;
    }

    private static bool TryResolveOperationalSmokeProbe(string packInfoToolName, out string smokeToolName, out JsonObject smokeArguments) {
        smokeToolName = string.Empty;
        smokeArguments = new JsonObject();

        var normalized = (packInfoToolName ?? string.Empty).Trim();
        if (normalized.Length == 0 || !normalized.EndsWith(PackInfoSuffix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var packId = ToolPackBootstrap.NormalizePackId(normalized[..^PackInfoSuffix.Length]);
        switch (packId) {
            case "system":
                smokeToolName = "system_info";
                return true;
            case "ad":
            case "active_directory":
                smokeToolName = "ad_environment_discover";
                return true;
            case "eventlog":
                smokeToolName = "eventlog_named_events_catalog";
                return true;
            case "testimox":
                smokeToolName = "testimox_rules_list";
                smokeArguments.Add("page_size", 25);
                return true;
            case "powershell":
                smokeToolName = "powershell_environment_discover";
                return true;
            default:
                return false;
        }
    }

    private static bool IsPackInfoDefinition(ToolDefinition definition) {
        if (definition is null) {
            return false;
        }

        var role = (definition.Routing?.Role ?? string.Empty).Trim();
        if (string.Equals(role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return definition.Name.EndsWith(PackInfoSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
