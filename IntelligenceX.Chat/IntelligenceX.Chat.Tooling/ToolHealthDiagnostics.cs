using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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
/// Shared helpers for probing pack-info role tools and normalizing health failures.
/// </summary>
public static class ToolHealthDiagnostics {
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private const int SmokePagingDefaultValue = 25;
    // Keep recursive schema traversal bounded so pathological contracts cannot blow up probe classification time.
    // Policy: fail-open past this depth (treat as no additional "required" discovery) to preserve probe availability.
    private const int MaxSchemaTraversalDepth = 6;
    private static readonly string[] SmokePagingArgumentCandidates = {
        "page_size",
        "limit",
        "top",
        "take",
        "first",
        "max_results"
    };
    private static readonly ConditionalWeakTable<ToolRegistry, SmokeProbePlanCache> SmokeProbePlanCaches = new();

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
    /// Returns registered pack-info role definitions (sorted, case-insensitive).
    /// Can optionally require explicit routing-source metadata for pack-info role tools.
    /// </summary>
    public static ToolDefinition[] GetPackInfoDefinitions(ToolRegistry registry, bool requireExplicitPackInfoRole = false) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }

        return registry.GetDefinitions()
            .Where(definition => IsPackInfoDefinition(definition, requireExplicitPackInfoRole))
            .OrderBy(static def => def.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns all registered pack-info tool names (sorted, case-insensitive).
    /// </summary>
    public static string[] GetPackInfoToolNames(ToolRegistry registry, bool requireExplicitPackInfoRole = false) {
        return GetPackInfoDefinitions(registry, requireExplicitPackInfoRole)
            .Select(static def => def.Name)
            .ToArray();
    }

    /// <summary>
    /// Probes a single tool and returns a normalized health result.
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(ToolRegistry registry, string toolName, int timeoutSeconds, CancellationToken cancellationToken,
        bool requireExplicitPackInfoRole = false) {
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

                if (TryResolveOperationalSmokeProbe(registry, normalizedToolName, requireExplicitPackInfoRole, out var smokeToolName, out var smokeArguments)
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

    private static bool TryResolveOperationalSmokeProbe(ToolRegistry registry, string packInfoToolName, bool requireExplicitPackInfoRole,
        out string smokeToolName, out JsonObject smokeArguments) {
        smokeToolName = string.Empty;
        smokeArguments = new JsonObject();

        var normalized = (packInfoToolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (!TryResolveOperationalSmokeProbePlan(registry, normalized, requireExplicitPackInfoRole, out var plan)) {
            return false;
        }

        smokeToolName = plan.SmokeToolName;
        smokeArguments = BuildSmokeArguments(plan.PagingArgumentName);
        return true;
    }

    /// <summary>
    /// Resolves canonical pack id for a tool definition from explicit routing metadata only.
    /// </summary>
    /// <param name="definition">Tool definition to inspect.</param>
    /// <param name="packId">Resolved canonical pack id when available.</param>
    /// <returns><c>true</c> when a pack id was resolved.</returns>
    public static bool TryResolvePackId(ToolDefinition definition, out string packId) {
        packId = ToolPackBootstrap.NormalizePackId(definition.Routing?.PackId);
        return packId.Length > 0;
    }

    private static bool TryResolveOperationalSmokeProbePlan(ToolRegistry registry, string packInfoToolName, bool requireExplicitPackInfoRole,
        out SmokeProbePlan plan) {
        var cache = SmokeProbePlanCaches.GetValue(registry, static _ => new SmokeProbePlanCache());
        return cache.TryResolvePlan(registry, packInfoToolName, requireExplicitPackInfoRole, out plan);
    }

    private static Dictionary<string, SmokeProbePlan> BuildSmokeProbePlanIndex(IReadOnlyList<ToolDefinition> definitions, bool requireExplicitPackInfoRole) {
        var plans = new Dictionary<string, SmokeProbePlan>(StringComparer.OrdinalIgnoreCase);
        var bestCandidatesByPack = new Dictionary<string, SmokeProbeCandidate>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || definition.WriteGovernance?.IsWriteCapable == true) {
                continue;
            }

            if (IsPackInfoDefinition(definition, requireExplicitPackInfoRole: false)
                || HasRequiredArguments(definition)
                || ToolSelectionMetadata.RequiresSelectionForFallback(definition)) {
                continue;
            }

            if (!TryResolvePackId(definition, out var packId) || packId.Length == 0) {
                continue;
            }

            var rolePriority = GetSmokeRolePriority(definition.Routing?.Role);
            _ = TryResolveSmokePagingArgumentName(definition, out var pagingArgumentName);
            var candidate = new SmokeProbeCandidate(definition.Name, rolePriority, pagingArgumentName);
            if (!bestCandidatesByPack.TryGetValue(packId, out var selected)
                || candidate.RolePriority < selected.RolePriority
                || (candidate.RolePriority == selected.RolePriority
                    && string.Compare(candidate.ToolName, selected.ToolName, StringComparison.OrdinalIgnoreCase) < 0)) {
                bestCandidatesByPack[packId] = candidate;
            }
        }

        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || !IsPackInfoDefinition(definition, requireExplicitPackInfoRole)) {
                continue;
            }

            if (!TryResolvePackId(definition, out var packId) || packId.Length == 0) {
                continue;
            }

            if (!bestCandidatesByPack.TryGetValue(packId, out var selected)) {
                continue;
            }

            plans[definition.Name] = new SmokeProbePlan(selected.ToolName, selected.PagingArgumentName);
        }

        return plans;
    }

    private static bool HasRequiredArguments(ToolDefinition definition) {
        return ContainsRequiredArguments(definition.Parameters, depth: 0);
    }

    private static bool ContainsRequiredArguments(JsonObject? schema, int depth) {
        if (schema is null || depth > MaxSchemaTraversalDepth) {
            return false;
        }

        var required = schema.GetArray("required");
        if (required is { Count: > 0 }) {
            return true;
        }

        return ContainsRequiredArgumentsInCombinators(schema.GetArray("allOf"), depth + 1)
               || ContainsRequiredArgumentsInCombinators(schema.GetArray("anyOf"), depth + 1)
               || ContainsRequiredArgumentsInCombinators(schema.GetArray("oneOf"), depth + 1);
    }

    private static bool ContainsRequiredArgumentsInCombinators(JsonArray? compositions, int depth) {
        if (compositions is null || compositions.Count == 0) {
            return false;
        }

        for (var i = 0; i < compositions.Count; i++) {
            var candidate = compositions[i]?.AsObject();
            if (candidate is not null && ContainsRequiredArguments(candidate, depth)) {
                return true;
            }
        }

        return false;
    }

    private static JsonObject BuildSmokeArguments(string pagingArgumentName) {
        var arguments = new JsonObject();
        if (string.IsNullOrWhiteSpace(pagingArgumentName)) {
            return arguments;
        }

        arguments.Add(pagingArgumentName, SmokePagingDefaultValue);
        return arguments;
    }

    private static bool TryResolveSmokePagingArgumentName(ToolDefinition definition, out string pagingArgumentName) {
        pagingArgumentName = string.Empty;
        var properties = definition.Parameters?.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return false;
        }

        var requiredNames = BuildRequiredArgumentNameSet(definition.Parameters);
        for (var i = 0; i < SmokePagingArgumentCandidates.Length; i++) {
            var candidate = SmokePagingArgumentCandidates[i];
            if (requiredNames.Contains(candidate)) {
                continue;
            }

            if (!TryGetSchemaProperty(properties, candidate, out var resolvedName, out var propertySchema)
                || !IsNumericLikeSchema(propertySchema)) {
                continue;
            }

            pagingArgumentName = resolvedName;
            return true;
        }

        return false;
    }

    private static HashSet<string> BuildRequiredArgumentNameSet(JsonObject? schema) {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendRequiredArgumentNames(schema, required, depth: 0);
        return required;
    }

    private static void AppendRequiredArgumentNames(JsonObject? schema, HashSet<string> required, int depth) {
        if (schema is null || depth > MaxSchemaTraversalDepth) {
            return;
        }

        var requiredValues = schema.GetArray("required");
        if (requiredValues is { Count: > 0 }) {
            for (var i = 0; i < requiredValues.Count; i++) {
                var name = (requiredValues[i]?.AsString() ?? string.Empty).Trim();
                if (name.Length > 0) {
                    required.Add(name);
                }
            }
        }

        AppendRequiredArgumentNames(schema.GetArray("allOf"), required, depth + 1);
        AppendRequiredArgumentNames(schema.GetArray("anyOf"), required, depth + 1);
        AppendRequiredArgumentNames(schema.GetArray("oneOf"), required, depth + 1);
    }

    private static void AppendRequiredArgumentNames(JsonArray? compositions, HashSet<string> required, int depth) {
        if (compositions is null || compositions.Count == 0 || depth > MaxSchemaTraversalDepth) {
            return;
        }

        for (var i = 0; i < compositions.Count; i++) {
            AppendRequiredArgumentNames(compositions[i]?.AsObject(), required, depth);
        }
    }

    private static bool TryGetSchemaProperty(JsonObject properties, string propertyName, out string resolvedName, out JsonObject? propertySchema) {
        if (properties.TryGetValue(propertyName, out var direct)) {
            resolvedName = propertyName;
            propertySchema = direct?.AsObject();
            return true;
        }

        foreach (var property in properties) {
            if (!string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            resolvedName = property.Key;
            propertySchema = property.Value?.AsObject();
            return true;
        }

        resolvedName = string.Empty;
        propertySchema = null;
        return false;
    }

    private static bool IsNumericLikeSchema(JsonObject? schema) {
        if (schema is null) {
            return false;
        }

        var type = (schema.GetString("type") ?? string.Empty).Trim();
        if (type.Length > 0) {
            return string.Equals(type, "integer", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(type, "number", StringComparison.OrdinalIgnoreCase);
        }

        var typeArray = schema.GetArray("type");
        if (typeArray is null || typeArray.Count == 0) {
            return false;
        }

        for (var i = 0; i < typeArray.Count; i++) {
            var candidate = (typeArray[i]?.AsString() ?? string.Empty).Trim();
            if (string.Equals(candidate, "integer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, "number", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private readonly record struct SmokeProbePlan(string SmokeToolName, string PagingArgumentName);
    private readonly record struct SmokeProbeCandidate(string ToolName, int RolePriority, string PagingArgumentName);

    private sealed class SmokeProbePlanCache {
        private readonly object _gate = new();
        private int _definitionCount = -1;
        private int _definitionFingerprint;
        private Dictionary<string, SmokeProbePlan> _loosePlans = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, SmokeProbePlan> _strictPlans = new(StringComparer.OrdinalIgnoreCase);

        internal bool TryResolvePlan(ToolRegistry registry, string packInfoToolName, bool requireExplicitPackInfoRole, out SmokeProbePlan plan) {
            var normalizedToolName = (packInfoToolName ?? string.Empty).Trim();
            if (normalizedToolName.Length == 0) {
                plan = default;
                return false;
            }

            lock (_gate) {
                var definitions = registry.GetDefinitions();
                var fingerprint = ComputeDefinitionFingerprint(definitions);
                if (_definitionCount != definitions.Count || _definitionFingerprint != fingerprint) {
                    _loosePlans = BuildSmokeProbePlanIndex(definitions, requireExplicitPackInfoRole: false);
                    _strictPlans = BuildSmokeProbePlanIndex(definitions, requireExplicitPackInfoRole: true);
                    _definitionCount = definitions.Count;
                    _definitionFingerprint = fingerprint;
                }

                var index = requireExplicitPackInfoRole ? _strictPlans : _loosePlans;
                return index.TryGetValue(normalizedToolName, out plan);
            }
        }
    }

    private static int ComputeDefinitionFingerprint(IReadOnlyList<ToolDefinition> definitions) {
        var hash = new HashCode();
        hash.Add(definitions.Count);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                hash.Add("<null>", StringComparer.Ordinal);
                continue;
            }

            hash.Add((definition.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
            hash.Add((definition.Routing?.Role ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
            hash.Add((definition.Routing?.RoutingSource ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
            hash.Add(ToolPackBootstrap.NormalizePackId(definition.Routing?.PackId), StringComparer.OrdinalIgnoreCase);
            hash.Add(definition.WriteGovernance?.IsWriteCapable ?? false);
            hash.Add(HasRequiredArguments(definition));
            _ = TryResolveSmokePagingArgumentName(definition, out var pagingArgumentName);
            hash.Add(pagingArgumentName, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    private static int GetSmokeRolePriority(string? role) {
        var normalized = (role ?? string.Empty).Trim();
        return normalized switch {
            var value when string.Equals(value, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase) => 0,
            var value when string.Equals(value, ToolRoutingTaxonomy.RoleDiagnostic, StringComparison.OrdinalIgnoreCase) => 1,
            var value when string.Equals(value, ToolRoutingTaxonomy.RoleResolver, StringComparison.OrdinalIgnoreCase) => 2,
            var value when string.Equals(value, ToolRoutingTaxonomy.RoleOperational, StringComparison.OrdinalIgnoreCase) => 3,
            _ => 4
        };
    }

    private static bool IsPackInfoDefinition(ToolDefinition definition, bool requireExplicitPackInfoRole) {
        if (definition is null) {
            return false;
        }

        var role = (definition.Routing?.Role ?? string.Empty).Trim();
        if (string.Equals(role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            if (!requireExplicitPackInfoRole) {
                return true;
            }

            var routingSource = (definition.Routing?.RoutingSource ?? string.Empty).Trim();
            return string.Equals(routingSource, ToolRoutingTaxonomy.SourceExplicit, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
