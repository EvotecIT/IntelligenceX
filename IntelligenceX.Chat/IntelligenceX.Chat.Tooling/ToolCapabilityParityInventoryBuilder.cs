using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Builds a runtime parity inventory from live tool registration and upstream engine contracts.
/// </summary>
public static class ToolCapabilityParityInventoryBuilder {
    /// <summary>
    /// Status used when the current runtime surface matches the phase-1 parity slice.
    /// </summary>
    public const string HealthyStatus = "healthy";

    /// <summary>
    /// Status used when upstream read-only capabilities are still missing from the live IX surface.
    /// </summary>
    public const string GapStatus = "parity_gap";

    /// <summary>
    /// Status used when a capability family is intentionally kept outside autonomous phase-1 execution.
    /// </summary>
    public const string GovernedBacklogStatus = "governed_backlog";

    /// <summary>
    /// Status used when upstream source metadata was not available for inspection in this runtime.
    /// </summary>
    public const string SourceUnavailableStatus = "source_unavailable";

    /// <summary>
    /// Status used when the associated pack is not currently surfaced in the active runtime.
    /// </summary>
    public const string PackUnavailableStatus = "pack_unavailable";

    private const int MaxMissingCapabilities = 12;

    /// <summary>
    /// Builds the phase-1 parity inventory. Returns an empty array when no live tool definitions are available.
    /// </summary>
    public static SessionCapabilityParityEntryDto[] Build(
        IReadOnlyList<ToolDefinition>? definitions,
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability = null) {
        if (definitions is not { Count: > 0 }) {
            return Array.Empty<SessionCapabilityParityEntryDto>();
        }

        var packEnabledIds = new HashSet<string>(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled)
            .Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Id))
            .Where(static packId => packId.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var definitionNames = new HashSet<string>(
            definitions
                .Where(static definition => definition is not null)
                .Select(static definition => (definition.Name ?? string.Empty).Trim())
                .Where(static name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var definitionsByName = BuildDefinitionsByName(definitions);
        var definitionsByPackId = BuildDefinitionsByPackId(definitions);

        var entries = new List<SessionCapabilityParityEntryDto>(4);
        TryAddEntry(entries, BuildAdMonitoringEntry(definitionNames, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildComputerXEntry(definitionsByName, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildTestimoXCoreEntry(definitionsByName, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildTestimoXAnalyticsEntry(definitionsByName, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildTestimoXPowerShellEntry(definitionsByPackId, packEnabledIds));

        return entries
            .OrderBy(static entry => entry.EngineId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Formats a one-line parity summary for host diagnostics.
    /// </summary>
    public static string FormatSummary(IReadOnlyList<SessionCapabilityParityEntryDto>? entries) {
        if (entries is not { Count: > 0 }) {
            return "engines=0, healthy=0, gaps=0, governed_backlog=0, missing_readonly=0";
        }

        var healthy = 0;
        var gaps = 0;
        var governedBacklog = 0;
        var missingReadonly = 0;
        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            if (entry is null) {
                continue;
            }

            missingReadonly += Math.Max(0, entry.MissingCapabilityCount);
            if (string.Equals(entry.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)) {
                healthy++;
            } else if (string.Equals(entry.Status, GapStatus, StringComparison.OrdinalIgnoreCase)) {
                gaps++;
            } else if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                governedBacklog++;
            }
        }

        return
            $"engines={entries.Count}, " +
            $"healthy={healthy}, " +
            $"gaps={gaps}, " +
            $"governed_backlog={governedBacklog}, " +
            $"missing_readonly={missingReadonly}";
    }

    /// <summary>
    /// Builds compact attention summaries for non-healthy parity entries.
    /// </summary>
    public static IReadOnlyList<string> BuildAttentionSummaries(IReadOnlyList<SessionCapabilityParityEntryDto>? entries, int maxItems = 6) {
        if (entries is not { Count: > 0 } || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, entries.Count));
        for (var i = 0; i < entries.Count && lines.Count < maxItems; i++) {
            var entry = entries[i];
            if (entry is null
                || string.Equals(entry.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, PackUnavailableStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{entry.EngineId}: governed backlog ({entry.Note ?? "intentionally not autonomous in phase 1."})");
                continue;
            }

            var suffix = entry.MissingCapabilityCount > 0
                ? $"missing {entry.MissingCapabilityCount} ({string.Join(", ", entry.MissingCapabilities)})"
                : entry.Note ?? entry.Status;
            lines.Add($"{entry.EngineId}: {suffix}");
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    /// <summary>
    /// Builds operator-facing per-engine parity detail summaries.
    /// </summary>
    public static IReadOnlyList<string> BuildDetailSummaries(IReadOnlyList<SessionCapabilityParityEntryDto>? entries, int maxItems = 8) {
        if (entries is not { Count: > 0 } || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, entries.Count));
        for (var i = 0; i < entries.Count && lines.Count < maxItems; i++) {
            var entry = entries[i];
            if (entry is null) {
                continue;
            }

            var prefix = $"{entry.EngineId} [{entry.Status}]";
            if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: registered_tools={entry.RegisteredToolCount}; {entry.Note ?? "governed backlog."}");
                continue;
            }

            if (string.Equals(entry.Status, SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: source metadata unavailable; registered_tools={entry.RegisteredToolCount}.");
                continue;
            }

            if (string.Equals(entry.Status, PackUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: pack unavailable.");
                continue;
            }

            var detail = $"{prefix}: surfaced={entry.SurfacedCapabilityCount}/{entry.ExpectedCapabilityCount}, registered_tools={entry.RegisteredToolCount}";
            if (entry.MissingCapabilityCount > 0) {
                detail += $", missing={entry.MissingCapabilityCount}";
                if (entry.MissingCapabilities.Length > 0) {
                    detail += $" ({FormatCapabilityList(entry.MissingCapabilities, entry.MissingCapabilityCount)})";
                }
            }

            lines.Add(detail);
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    private static SessionCapabilityParityEntryDto? BuildAdMonitoringEntry(
        HashSet<string> definitionNames,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "active_directory";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var upstreamKinds = DiscoverAdMonitoringProbeKinds();
        if (upstreamKinds.Length == 0) {
            return CreateStatusEntry(
                engineId: "adplayground_monitoring",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "ADPlayground.Monitoring probe metadata was not available in this runtime.");
        }

        var surfacedKinds = DiscoverSurfacedAdMonitoringProbeKinds(definitionNames, definitionsByPackId);
        var availableReadOnlyExpectations = BuildAvailableExpectations(ToolCapabilityParityCatalog.AdMonitoringReadOnlyExpectations);
        var surfacedReadOnlyCapabilities = availableReadOnlyExpectations
            .Where(expectation => expectation.IsSurfaced(BuildDefinitionsByName(definitionsByPackId.TryGetValue(packId, out var packDefinitions) ? packDefinitions : Array.Empty<ToolDefinition>())))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "adplayground_monitoring",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: upstreamKinds.Concat(availableReadOnlyExpectations.Select(static expectation => expectation.CapabilityId)),
            surfacedCapabilities: surfacedKinds.Concat(surfacedReadOnlyCapabilities),
            note: "Probe-kind and persisted runtime-state parity between ADPlayground.Monitoring and IX AD monitoring tools.");
    }

    private static SessionCapabilityParityEntryDto? BuildComputerXEntry(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "system";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var availableExpectations = BuildAvailableExpectations(ToolCapabilityParityCatalog.ComputerXReadOnlyExpectations);
        if (availableExpectations.Length == 0) {
            return CreateStatusEntry(
                engineId: "computerx",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "ComputerX remote read-only contracts were not available in this runtime.");
        }

        var surfacedCapabilities = availableExpectations
            .Where(expectation => expectation.IsSurfaced(definitionsByName))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "computerx",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: availableExpectations.Select(static expectation => expectation.CapabilityId),
            surfacedCapabilities: surfacedCapabilities,
            note: "Expanded remote read-only parity for ComputerX operator surfaces.");
    }

    private static SessionCapabilityParityEntryDto? BuildTestimoXCoreEntry(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "testimox";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var availableExpectations = BuildAvailableExpectations(ToolCapabilityParityCatalog.TestimoXCoreReadOnlyExpectations);
        if (availableExpectations.Length == 0) {
            return CreateStatusEntry(
                engineId: "testimox",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "TestimoX rule tooling contracts were not available in this runtime.");
        }

        var surfacedCapabilities = availableExpectations
            .Where(expectation => expectation.IsSurfaced(definitionsByName))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "testimox",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: availableExpectations.Select(static expectation => expectation.CapabilityId),
            surfacedCapabilities: surfacedCapabilities,
            note: "Profiles, inventory, baseline crosswalk, catalog, and execution parity for TestimoX tooling service.");
    }

    private static SessionCapabilityParityEntryDto? BuildTestimoXAnalyticsEntry(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "testimox_analytics";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var availableExpectations = BuildAvailableExpectations(ToolCapabilityParityCatalog.TestimoXAnalyticsReadOnlyExpectations);
        if (availableExpectations.Length == 0) {
            return CreateStatusEntry(
                engineId: "testimox_analytics",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "TestimoX analytics artifact contracts were not available in this runtime.");
        }

        var surfacedCapabilities = availableExpectations
            .Where(expectation => expectation.IsSurfaced(definitionsByName))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "testimox_analytics",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: availableExpectations.Select(static expectation => expectation.CapabilityId),
            surfacedCapabilities: surfacedCapabilities,
            note: "Persisted analytics, report, snapshot, and maintenance artifact parity for TestimoX analytics tooling.");
    }

    private static SessionCapabilityParityEntryDto? BuildTestimoXPowerShellEntry(
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "testimox";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var sourceAvailable = HasType(
            ToolCapabilityParityCatalog.TestimoXPowerShellProviderTypeName,
            ToolCapabilityParityCatalog.TestimoXAssemblyName);
        if (!sourceAvailable) {
            return null;
        }

        return CreateStatusEntry(
            engineId: "testimox_powershell",
            packId,
            status: GovernedBacklogStatus,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilityCount: 0,
            surfacedCapabilityCount: 0,
            missingCapabilities: Array.Empty<string>(),
            note: "PowerShell/provider-backed TestimoX service-management flows stay governed outside autonomous phase 1.");
    }

    private static Dictionary<string, List<ToolDefinition>> BuildDefinitionsByPackId(IReadOnlyList<ToolDefinition> definitions) {
        var result = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || !ToolHealthDiagnostics.TryResolvePackId(definition, out var packId) || packId.Length == 0) {
                continue;
            }

            if (!result.TryGetValue(packId, out var list)) {
                list = new List<ToolDefinition>();
                result[packId] = list;
            }

            list.Add(definition);
        }

        return result;
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

    private static int GetRegisteredToolCount(Dictionary<string, List<ToolDefinition>> definitionsByPackId, string packId) {
        return definitionsByPackId.TryGetValue(packId, out var definitions)
            ? definitions.Count
            : 0;
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

    private static SessionCapabilityParityEntryDto CreateCapabilityEntry(
        string engineId,
        string packId,
        bool sourceAvailable,
        int registeredToolCount,
        IEnumerable<string> expectedCapabilities,
        IEnumerable<string> surfacedCapabilities,
        string? note) {
        var expected = NormalizeDistinctValues(expectedCapabilities, maxItems: 0);
        var surfaced = NormalizeDistinctValues(surfacedCapabilities, maxItems: 0);
        var missing = expected
            .Except(surfaced, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = missing.Length > 0 ? GapStatus : HealthyStatus;

        return new SessionCapabilityParityEntryDto {
            EngineId = engineId,
            PackId = packId,
            Status = status,
            SourceAvailable = sourceAvailable,
            RegisteredToolCount = Math.Max(0, registeredToolCount),
            ExpectedCapabilityCount = expected.Length,
            SurfacedCapabilityCount = surfaced.Length,
            MissingCapabilityCount = missing.Length,
            MissingCapabilities = NormalizeDistinctValues(missing, MaxMissingCapabilities),
            Note = note
        };
    }

    private static SessionCapabilityParityEntryDto CreateStatusEntry(
        string engineId,
        string packId,
        string status,
        bool sourceAvailable,
        int registeredToolCount,
        int expectedCapabilityCount,
        int surfacedCapabilityCount,
        IEnumerable<string> missingCapabilities,
        string? note) {
        var missing = NormalizeDistinctValues(missingCapabilities, MaxMissingCapabilities);
        return new SessionCapabilityParityEntryDto {
            EngineId = engineId,
            PackId = packId,
            Status = status,
            SourceAvailable = sourceAvailable,
            RegisteredToolCount = Math.Max(0, registeredToolCount),
            ExpectedCapabilityCount = Math.Max(0, expectedCapabilityCount),
            SurfacedCapabilityCount = Math.Max(0, surfacedCapabilityCount),
            MissingCapabilityCount = missing.Length,
            MissingCapabilities = missing,
            Note = note
        };
    }

    private static string[] DiscoverAdMonitoringProbeKinds() {
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

    private static string[] DiscoverSurfacedAdMonitoringProbeKinds(
        HashSet<string> definitionNames,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId) {
        if (!definitionNames.Contains("ad_monitoring_probe_run")) {
            return Array.Empty<string>();
        }

        if (!definitionsByPackId.TryGetValue("active_directory", out var definitions) || definitions.Count == 0) {
            return Array.Empty<string>();
        }

        var definition = definitions.FirstOrDefault(static candidate =>
            string.Equals(candidate.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase));
        var enumValues = definition?.Parameters?
            .GetObject("properties")?
            .GetObject("probe_kind")?
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

    private static bool HasType(string fullTypeName, string assemblyName) {
        return TryResolveType(fullTypeName, assemblyName) is not null;
    }

    private static bool HasPublicInstanceProperty(string fullTypeName, string propertyName, string assemblyName) {
        var type = TryResolveType(fullTypeName, assemblyName);
        return type?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase) is not null;
    }

    private static bool HasPublicStaticMethod(string fullTypeName, string methodName, string assemblyName) {
        var type = TryResolveType(fullTypeName, assemblyName);
        return type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase) is not null;
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

    private static string FormatCapabilityList(IReadOnlyList<string> capabilities, int totalCount) {
        var shown = capabilities?.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? Array.Empty<string>();
        if (shown.Length == 0) {
            return string.Empty;
        }

        var suffix = totalCount > shown.Length ? $", +{totalCount - shown.Length} more" : string.Empty;
        return string.Join(", ", shown) + suffix;
    }

    private static string NormalizeCapabilityId(string? value, string suffixToTrim) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(suffixToTrim)
            && normalized.EndsWith(suffixToTrim, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[..^suffixToTrim.Length];
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

    private static void TryAddEntry(ICollection<SessionCapabilityParityEntryDto> entries, SessionCapabilityParityEntryDto? entry) {
        if (entry is not null) {
            entries.Add(entry);
        }
    }

    private readonly record struct CapabilityExpectation(
        string CapabilityId,
        Func<IReadOnlyDictionary<string, ToolDefinition>, bool> IsSurfaced);
}
