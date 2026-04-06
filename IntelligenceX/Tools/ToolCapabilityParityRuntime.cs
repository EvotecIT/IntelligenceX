using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared parity status identifiers used by runtime capability parity inventories.
/// </summary>
public static class ToolCapabilityParityStatuses {
    /// <summary>
    /// The current runtime surface matches the expected parity slice.
    /// </summary>
    public const string Healthy = "healthy";

    /// <summary>
    /// Upstream read-only capabilities are still missing from the live IX surface.
    /// </summary>
    public const string Gap = "parity_gap";

    /// <summary>
    /// The capability family intentionally stays outside autonomous phase-1 execution.
    /// </summary>
    public const string GovernedBacklog = "governed_backlog";

    /// <summary>
    /// Upstream source metadata was not available for inspection in this runtime.
    /// </summary>
    public const string SourceUnavailable = "source_unavailable";

    /// <summary>
    /// The associated pack is not currently surfaced in the active runtime.
    /// </summary>
    public const string PackUnavailable = "pack_unavailable";
}

/// <summary>
/// Pack-owned parity slice registration consumed by host/chat parity inventories.
/// </summary>
public sealed record ToolCapabilityParitySliceDescriptor {
    /// <summary>
    /// Stable upstream engine identifier represented by this slice.
    /// </summary>
    public string EngineId { get; init; } = string.Empty;

    /// <summary>
    /// Optional pack id overriding the containing pack descriptor id for this slice.
    /// </summary>
    public string? PackId { get; init; }

    /// <summary>
    /// Evaluates the current runtime parity state for the slice.
    /// Returning <see langword="null"/> omits the slice from the current runtime inventory.
    /// </summary>
    public Func<IReadOnlyList<ToolDefinition>, ToolCapabilityParitySliceEvaluation?> Evaluate { get; init; } =
        static _ => null;
}

/// <summary>
/// Evaluated parity slice state emitted by pack-owned parity descriptors.
/// </summary>
public sealed record ToolCapabilityParitySliceEvaluation {
    /// <summary>
    /// Stable parity status identifier.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Whether upstream source metadata was available when the slice was evaluated.
    /// </summary>
    public bool SourceAvailable { get; init; }

    /// <summary>
    /// Expected upstream capability identifiers for the slice.
    /// </summary>
    public IReadOnlyList<string> ExpectedCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Capability identifiers currently surfaced by the IX tool runtime.
    /// </summary>
    public IReadOnlyList<string> SurfacedCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional operator-facing note describing the slice or current limitation.
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>
/// Coverage snapshot for expectation-descriptor based parity slices.
/// </summary>
public sealed record ToolCapabilityParityExpectationCoverage {
    /// <summary>
    /// Whether at least one upstream source contract was available.
    /// </summary>
    public bool SourceAvailable { get; init; }

    /// <summary>
    /// Expected capability identifiers backed by available upstream source contracts.
    /// </summary>
    public IReadOnlyList<string> ExpectedCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Capability identifiers currently surfaced by the IX tool runtime.
    /// </summary>
    public IReadOnlyList<string> SurfacedCapabilities { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Shared helper methods for pack-owned runtime capability parity descriptors.
/// </summary>
public static class ToolCapabilityParityRuntime {
    /// <summary>
    /// Creates a standard expectation-backed parity slice descriptor.
    /// </summary>
    public static ToolCapabilityParitySliceDescriptor CreateExpectationSliceDescriptor(
        string engineId,
        string packId,
        IReadOnlyList<ToolCapabilityParityExpectationDescriptor> descriptors,
        string? note,
        string? sourceUnavailableNote) {
        return new ToolCapabilityParitySliceDescriptor {
            EngineId = engineId ?? string.Empty,
            PackId = packId ?? string.Empty,
            Evaluate = definitions => EvaluateExpectationSlice(definitions, descriptors, note, sourceUnavailableNote)
        };
    }

    /// <summary>
    /// Creates a governed-backlog slice descriptor that is emitted only when the source contract is available.
    /// </summary>
    public static ToolCapabilityParitySliceDescriptor CreateGovernedBacklogSliceDescriptor(
        string engineId,
        string packId,
        Func<bool> isApplicable,
        string? note) {
        return new ToolCapabilityParitySliceDescriptor {
            EngineId = engineId ?? string.Empty,
            PackId = packId ?? string.Empty,
            Evaluate = _ => isApplicable()
                ? CreateStatusEvaluation(ToolCapabilityParityStatuses.GovernedBacklog, sourceAvailable: true, note)
                : null
        };
    }

    /// <summary>
    /// Evaluates an expectation-backed parity slice using shared source/surface contracts.
    /// </summary>
    public static ToolCapabilityParitySliceEvaluation EvaluateExpectationSlice(
        IReadOnlyList<ToolDefinition> definitions,
        IReadOnlyList<ToolCapabilityParityExpectationDescriptor> descriptors,
        string? note,
        string? sourceUnavailableNote,
        IReadOnlyList<ToolDefinition>? surfacedDefinitions = null) {
        var coverage = EvaluateAvailableExpectations(definitions, descriptors, surfacedDefinitions);
        if (!coverage.SourceAvailable) {
            return CreateStatusEvaluation(
                ToolCapabilityParityStatuses.SourceUnavailable,
                sourceAvailable: false,
                sourceUnavailableNote ?? note);
        }

        return CreateCapabilityEvaluation(
            coverage.ExpectedCapabilities,
            coverage.SurfacedCapabilities,
            note,
            sourceAvailable: true);
    }

    /// <summary>
    /// Evaluates available expectation descriptors without forcing a source-unavailable status when none are present.
    /// </summary>
    public static ToolCapabilityParityExpectationCoverage EvaluateAvailableExpectations(
        IReadOnlyList<ToolDefinition> definitions,
        IReadOnlyList<ToolCapabilityParityExpectationDescriptor> descriptors,
        IReadOnlyList<ToolDefinition>? surfacedDefinitions = null) {
        var availableExpectations = BuildAvailableExpectations(descriptors);
        if (availableExpectations.Length == 0) {
            return new ToolCapabilityParityExpectationCoverage {
                SourceAvailable = false
            };
        }

        var definitionsByName = BuildDefinitionsByName(surfacedDefinitions ?? definitions);
        return new ToolCapabilityParityExpectationCoverage {
            SourceAvailable = true,
            ExpectedCapabilities = availableExpectations
                .Select(static expectation => expectation.CapabilityId)
                .ToArray(),
            SurfacedCapabilities = availableExpectations
                .Where(expectation => expectation.IsSurfaced(definitionsByName))
                .Select(static expectation => expectation.CapabilityId)
                .ToArray()
        };
    }

    /// <summary>
    /// Builds a capability-comparison evaluation from expected and surfaced capability ids.
    /// </summary>
    public static ToolCapabilityParitySliceEvaluation CreateCapabilityEvaluation(
        IEnumerable<string> expectedCapabilities,
        IEnumerable<string> surfacedCapabilities,
        string? note,
        bool sourceAvailable = true) {
        var expected = NormalizeDistinctValues(expectedCapabilities, maxItems: 0);
        var surfaced = NormalizeDistinctValues(surfacedCapabilities, maxItems: 0);
        var missing = expected
            .Except(surfaced, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ToolCapabilityParitySliceEvaluation {
            Status = missing.Length == 0
                ? ToolCapabilityParityStatuses.Healthy
                : ToolCapabilityParityStatuses.Gap,
            SourceAvailable = sourceAvailable,
            ExpectedCapabilities = expected,
            SurfacedCapabilities = surfaced,
            Note = note
        };
    }

    /// <summary>
    /// Builds a status-only evaluation for governed/source-unavailable style slices.
    /// </summary>
    public static ToolCapabilityParitySliceEvaluation CreateStatusEvaluation(
        string status,
        bool sourceAvailable,
        string? note) {
        return new ToolCapabilityParitySliceEvaluation {
            Status = (status ?? string.Empty).Trim(),
            SourceAvailable = sourceAvailable,
            Note = note
        };
    }

    /// <summary>
    /// Returns tool definitions currently attributed to the provided normalized pack id.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> GetDefinitionsByPackId(
        IReadOnlyList<ToolDefinition> definitions,
        string packId) {
        if (definitions is not { Count: > 0 }) {
            return Array.Empty<ToolDefinition>();
        }

        var normalizedPackId = ToolSelectionMetadata.NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var result = new List<ToolDefinition>();
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || !ToolSelectionMetadata.TryResolvePackId(definition, out var definitionPackId)) {
                continue;
            }

            if (string.Equals(definitionPackId, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                result.Add(definition);
            }
        }

        return result.Count == 0 ? Array.Empty<ToolDefinition>() : result.ToArray();
    }

    /// <summary>
    /// Discovers AD monitoring probe kinds from upstream ADPlayground.Monitoring metadata.
    /// </summary>
    public static string[] DiscoverAdMonitoringProbeKinds() {
        var baseType = TryResolveType(
            ToolCapabilityParityCatalog.AdMonitoringProbeDefinitionTypeName,
            ToolCapabilityParityCatalog.AdMonitoringAssemblyName);
        var directoryBaseType = TryResolveType(
            ToolCapabilityParityCatalog.AdMonitoringDirectoryHealthProbeDefinitionBaseTypeName,
            ToolCapabilityParityCatalog.AdMonitoringAssemblyName);
        if (baseType is null || directoryBaseType is null) {
            return Array.Empty<string>();
        }

        var assembly = baseType.Assembly;
        try {
            return assembly.GetTypes()
                .Where(type => !type.IsAbstract && baseType.IsAssignableFrom(type))
                .Select(type => directoryBaseType.IsAssignableFrom(type)
                    ? "directory"
                    : NormalizeCapabilityId(type.Name, "ProbeDefinition"))
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (ReflectionTypeLoadException ex) {
            return ex.Types
                .Where(static type => type is not null)
                .Where(type => !type!.IsAbstract && baseType.IsAssignableFrom(type))
                .Select(type => directoryBaseType.IsAssignableFrom(type!)
                    ? "directory"
                    : NormalizeCapabilityId(type!.Name, "ProbeDefinition"))
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
    }

    /// <summary>
    /// Discovers surfaced AD monitoring probe kinds from the live tool definition schema.
    /// </summary>
    public static string[] DiscoverSurfacedAdMonitoringProbeKinds(
        IReadOnlyList<ToolDefinition> definitions,
        string toolName = "ad_monitoring_probe_run",
        string parameterName = "probe_kind") {
        var definition = (definitions ?? Array.Empty<ToolDefinition>())
            .FirstOrDefault(candidate => string.Equals(candidate?.Name, toolName, StringComparison.OrdinalIgnoreCase));
        var enumValues = definition?.Parameters?
            .GetObject("properties")?
            .GetObject(parameterName)?
            .GetArray("enum");
        if (enumValues is null || enumValues.Count == 0) {
            return Array.Empty<string>();
        }

        return NormalizeDistinctValues(
            enumValues
                .Select(static value => value?.AsString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))!,
            maxItems: 0);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the upstream type exists in the current runtime.
    /// </summary>
    public static bool HasType(string fullTypeName, string assemblyName) {
        return TryResolveType(fullTypeName, assemblyName) is not null;
    }

    private static CapabilityExpectation[] BuildAvailableExpectations(IReadOnlyList<ToolCapabilityParityExpectationDescriptor> descriptors) {
        if (descriptors is not { Count: > 0 }) {
            return Array.Empty<CapabilityExpectation>();
        }

        var expectations = new List<CapabilityExpectation>(descriptors.Count);
        for (var i = 0; i < descriptors.Count; i++) {
            var descriptor = descriptors[i];
            if (!IsSourceContractAvailable(descriptor)) {
                continue;
            }

            expectations.Add(CreateCapabilityExpectation(descriptor));
        }

        return expectations.Count == 0 ? Array.Empty<CapabilityExpectation>() : expectations.ToArray();
    }

    private static CapabilityExpectation CreateCapabilityExpectation(ToolCapabilityParityExpectationDescriptor descriptor) {
        Func<IReadOnlyDictionary<string, ToolDefinition>, bool> isSurfaced = descriptor.SurfaceContractKind switch {
            ToolCapabilityParitySurfaceContractKind.ToolPresent =>
                definitionsByName => definitionsByName.ContainsKey((descriptor.ToolName ?? string.Empty).Trim()),
            ToolCapabilityParitySurfaceContractKind.ToolParameterPresent =>
                definitionsByName => HasToolParameter(definitionsByName, (descriptor.ToolName ?? string.Empty).Trim(), descriptor.SurfaceParameterName),
            _ => static _ => false
        };

        return new CapabilityExpectation(
            CapabilityId: descriptor.CapabilityId,
            IsSurfaced: isSurfaced);
    }

    private static bool IsSourceContractAvailable(ToolCapabilityParityExpectationDescriptor descriptor) {
        return descriptor.SourceContractKind switch {
            ToolCapabilityParitySourceContractKind.TypeExists =>
                HasType(descriptor.TypeName, descriptor.AssemblyName),
            ToolCapabilityParitySourceContractKind.PublicInstanceProperty =>
                HasPublicInstanceProperty(descriptor.TypeName, descriptor.PropertyName, descriptor.AssemblyName),
            ToolCapabilityParitySourceContractKind.PublicStaticMethod =>
                HasPublicStaticMethod(descriptor.TypeName, descriptor.MethodNames.FirstOrDefault() ?? string.Empty, descriptor.AssemblyName),
            ToolCapabilityParitySourceContractKind.AnyPublicStaticMethod =>
                HasAnyPublicStaticMethod(descriptor.TypeName, descriptor.AssemblyName, descriptor.MethodNames?.ToArray() ?? Array.Empty<string>()),
            ToolCapabilityParitySourceContractKind.AllPublicStaticMethods =>
                HasAllPublicStaticMethods(descriptor.TypeName, descriptor.AssemblyName, descriptor.MethodNames?.ToArray() ?? Array.Empty<string>()),
            _ => false
        };
    }

    private static Dictionary<string, ToolDefinition> BuildDefinitionsByName(IReadOnlyList<ToolDefinition> definitions) {
        var result = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var name = (definition?.Name ?? string.Empty).Trim();
            if (name.Length == 0 || result.ContainsKey(name)) {
                continue;
            }

            result[name] = definition!;
        }

        return result;
    }

    private static bool HasPublicInstanceProperty(string fullTypeName, string propertyName, string assemblyName) {
        var type = TryResolveType(fullTypeName, assemblyName);
        return type?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase) is not null;
    }

    private static bool HasPublicStaticMethod(string fullTypeName, string methodName, string assemblyName) {
        var type = TryResolveType(fullTypeName, assemblyName);
        if (type is null) {
            return false;
        }

        var normalizedMethodName = (methodName ?? string.Empty).Trim();
        if (normalizedMethodName.Length == 0) {
            return false;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(method => string.Equals(method.Name, normalizedMethodName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAnyPublicStaticMethod(string fullTypeName, string assemblyName, params string[] methodNames) {
        if (methodNames is null || methodNames.Length == 0) {
            return false;
        }

        for (var i = 0; i < methodNames.Length; i++) {
            if (HasPublicStaticMethod(fullTypeName, methodNames[i], assemblyName)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasAllPublicStaticMethods(string fullTypeName, string assemblyName, params string[] methodNames) {
        if (methodNames is null || methodNames.Length == 0) {
            return false;
        }

        for (var i = 0; i < methodNames.Length; i++) {
            if (!HasPublicStaticMethod(fullTypeName, methodNames[i], assemblyName)) {
                return false;
            }
        }

        return true;
    }

    private static Type? TryResolveType(string fullTypeName, string assemblyName) {
        var normalizedTypeName = (fullTypeName ?? string.Empty).Trim();
        var normalizedAssemblyName = (assemblyName ?? string.Empty).Trim();
        if (normalizedTypeName.Length == 0) {
            return null;
        }

        if (normalizedAssemblyName.Length > 0) {
            var assemblyQualifiedName = normalizedTypeName + ", " + normalizedAssemblyName;
            var directlyResolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
            if (directlyResolved is not null) {
                return directlyResolved;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var loadedAssemblyName = (assembly.GetName().Name ?? string.Empty).Trim();
            if (normalizedAssemblyName.Length > 0
                && !string.Equals(loadedAssemblyName, normalizedAssemblyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var resolved = assembly.GetType(normalizedTypeName, throwOnError: false, ignoreCase: false);
            if (resolved is not null) {
                return resolved;
            }
        }

        return null;
    }

    private static bool HasToolParameter(IReadOnlyDictionary<string, ToolDefinition> definitionsByName, string toolName, string parameterName) {
        if (!definitionsByName.TryGetValue(toolName, out var definition) || definition.Parameters is null) {
            return false;
        }

        var properties = definition.Parameters.GetObject("properties");
        return properties is not null && properties.TryGetValue(parameterName, out _);
    }

    private static string NormalizeCapabilityId(string? value, string suffixToTrim) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(suffixToTrim)
            && normalized.EndsWith(suffixToTrim, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(0, normalized.Length - suffixToTrim.Length);
        }

        return string.Concat(
                normalized.SelectMany(static (ch, index) =>
                    char.IsUpper(ch) && index > 0
                        ? new[] { '_', char.ToLowerInvariant(ch) }
                        : new[] { char.ToLowerInvariant(ch) }))
            .Trim('_');
    }

    private static string[] NormalizeDistinctValues(IEnumerable<string> values, int maxItems) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<string>()) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            result.Add(normalized);
            if (maxItems > 0 && result.Count >= maxItems) {
                break;
            }
        }

        return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
    }

    private readonly record struct CapabilityExpectation(
        string CapabilityId,
        Func<IReadOnlyDictionary<string, ToolDefinition>, bool> IsSurfaced);
}
