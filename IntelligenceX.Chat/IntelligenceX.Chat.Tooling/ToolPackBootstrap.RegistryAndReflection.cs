using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

public static partial class ToolPackBootstrap {
    /// <summary>
    /// Resolves plugin search roots used by folder-based plugin loading.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Deterministic plugin search roots.</returns>
    public static IReadOnlyList<string> GetPluginSearchPaths(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return PluginFolderToolPackLoader.ResolvePluginSearchRoots(options)
            .Select(static root => root.Path)
            .ToArray();
    }

    private static IReadOnlyList<BuiltInPackRegistrationCandidate> DiscoverBuiltInPacks(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var candidates = new List<BuiltInPackRegistrationCandidate>();
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packTypes = DiscoverBuiltInPackTypes(options.OnBootstrapWarning);

        for (var i = 0; i < packTypes.Count; i++) {
            var packType = packTypes[i];
            if (!TryCreateBuiltInPack(packType, options, out var pack, out var error)) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='{NormalizeDisabledReason(error)}'",
                    shouldWarn: true);
                continue;
            }

            IToolPack normalizedPack;
            try {
                normalizedPack = RequireDeclaredSourceKind(pack, packType.FullName ?? packType.Name);
            } catch (Exception ex) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='{NormalizeDisabledReason(ex.Message)}'",
                    shouldWarn: true);
                continue;
            }

            var normalizedPackId = NormalizePackId(normalizedPack.Descriptor.Id);
            if (normalizedPackId.Length == 0) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='descriptor id is missing.'",
                    shouldWarn: true);
                continue;
            }

            EnsureNoPackIdNormalizationCollision(
                descriptorIdsByNormalizedPackId,
                normalizedPack.Descriptor.Id,
                normalizedPackId);

            var defaultEnabled = !normalizedPack.Descriptor.IsDangerous
                                 && normalizedPack.Descriptor.Tier != ToolCapabilityTier.DangerousWrite;
            candidates.Add(new BuiltInPackRegistrationCandidate(normalizedPackId, normalizedPack, defaultEnabled));
        }

        return candidates
            .OrderBy(static candidate => candidate.PackId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<Type> DiscoverBuiltInPackTypes(Action<string>? onWarning) {
        var toolPackTypes = new List<Type>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assemblyName in EnumerateToolAssemblyNamesForDiscovery()) {
            var assembly = TryLoadToolAssembly(assemblyName, onWarning);
            if (assembly is null) {
                continue;
            }

            foreach (var type in EnumerateLoadableTypes(assembly, onWarning)) {
                var fullName = type.FullName;
                if (string.IsNullOrWhiteSpace(fullName) || !seen.Add(fullName)) {
                    continue;
                }

                if (!type.IsClass
                    || type.IsAbstract
                    || type.ContainsGenericParameters
                    || !typeof(IToolPack).IsAssignableFrom(type)) {
                    continue;
                }

                toolPackTypes.Add(type);
            }
        }

        return toolPackTypes
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<AssemblyName> EnumerateToolAssemblyNamesForDiscovery() {
        var discovered = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);
        var bootstrapAssembly = typeof(ToolPackBootstrap).Assembly;
        var referencedToolAssemblies = bootstrapAssembly
            .GetReferencedAssemblies()
            .Where(static reference => !string.IsNullOrWhiteSpace(reference.Name) && IsBuiltInToolAssemblyName(reference.Name))
            .ToArray();
        var allowedAssemblyNames = new HashSet<string>(
            referencedToolAssemblies
                .Select(static reference => reference.Name ?? string.Empty)
                .Where(static name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        void AddAssemblyName(AssemblyName? candidate) {
            if (candidate is null
                || string.IsNullOrWhiteSpace(candidate.Name)
                || !IsBuiltInToolAssemblyName(candidate.Name)
                || !allowedAssemblyNames.Contains(candidate.Name)) {
                return;
            }

            if (!discovered.ContainsKey(candidate.Name)) {
                discovered[candidate.Name] = candidate;
            }
        }

        foreach (var reference in referencedToolAssemblies) {
            AddAssemblyName(reference);
        }

        foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
            AddAssemblyName(loadedAssembly.GetName());
        }

        return discovered.Values
            .OrderBy(static name => name.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBuiltInToolAssemblyName(string? assemblyName) {
        if (string.IsNullOrWhiteSpace(assemblyName)) {
            return false;
        }

        var normalized = assemblyName.Trim();
        if (!normalized.StartsWith("IntelligenceX.Tools.", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return normalized.IndexOf(".Tests", StringComparison.OrdinalIgnoreCase) < 0
               && normalized.IndexOf(".Benchmarks", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static Assembly? TryLoadToolAssembly(AssemblyName assemblyName, Action<string>? onWarning) {
        try {
            return Assembly.Load(assemblyName);
        } catch (Exception ex) {
            Warn(
                onWarning,
                $"[startup] built_in_pack_assembly_skipped assembly='{assemblyName.Name ?? "<unknown>"}' reason='{NormalizeDisabledReason(ex.Message)}'",
                shouldWarn: true);
            return null;
        }
    }

    private static IReadOnlyList<Type> EnumerateLoadableTypes(Assembly assembly, Action<string>? onWarning) {
        try {
            return assembly.GetTypes();
        } catch (ReflectionTypeLoadException ex) {
            var types = ex.Types
                .Where(static type => type is not null)
                .Cast<Type>()
                .ToArray();
            if (types.Length == 0) {
                var firstLoaderError = ex.LoaderExceptions?.FirstOrDefault();
                Warn(
                    onWarning,
                    $"[startup] built_in_pack_assembly_skipped assembly='{assembly.GetName().Name ?? "<unknown>"}' reason='{NormalizeDisabledReason(firstLoaderError?.Message)}'",
                    shouldWarn: true);
            }
            return types;
        }
    }

    private static bool TryCreateBuiltInPack(
        Type packType,
        ToolPackBootstrapOptions bootstrapOptions,
        out IToolPack pack,
        out string error) {
        pack = null!;
        error = string.Empty;

        try {
            var parameterlessCtor = packType.GetConstructor(Type.EmptyTypes);
            if (parameterlessCtor is not null) {
                var created = parameterlessCtor.Invoke(parameters: null);
                if (created is IToolPack parameterlessPack) {
                    pack = parameterlessPack;
                    return true;
                }
            }

            var constructors = packType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            for (var i = 0; i < constructors.Length; i++) {
                var constructor = constructors[i];
                var parameters = constructor.GetParameters();
                if (parameters.Length != 1) {
                    continue;
                }

                object? options;
                try {
                    options = Activator.CreateInstance(parameters[0].ParameterType);
                } catch {
                    continue;
                }

                if (options is null) {
                    continue;
                }

                ConfigurePackOptions(options, bootstrapOptions);
                var created = constructor.Invoke(new[] { options });
                if (created is IToolPack optionsPack) {
                    pack = optionsPack;
                    return true;
                }
            }

            error = "No supported constructor found (expected parameterless or single-options constructor).";
            return false;
        } catch (Exception ex) {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static void ConfigurePackOptions(object options, ToolPackBootstrapOptions bootstrapOptions) {
        AddStringListValuesIfPresent(options, "AllowedRoots", bootstrapOptions.AllowedRoots);
        SetPropertyIfPresent(options, "DomainController", bootstrapOptions.AdDomainController);
        SetPropertyIfPresent(options, "DefaultSearchBaseDn", bootstrapOptions.AdDefaultSearchBaseDn);
        SetPropertyIfPresent(options, "MaxResults", bootstrapOptions.AdMaxResults > 0 ? bootstrapOptions.AdMaxResults : 1000);
        SetPropertyIfPresent(options, "Enabled", true);
        SetPropertyIfPresent(options, "DefaultTimeoutMs", bootstrapOptions.PowerShellDefaultTimeoutMs);
        SetPropertyIfPresent(options, "MaxTimeoutMs", bootstrapOptions.PowerShellMaxTimeoutMs);
        SetPropertyIfPresent(options, "DefaultMaxOutputChars", bootstrapOptions.PowerShellDefaultMaxOutputChars);
        SetPropertyIfPresent(options, "MaxOutputChars", bootstrapOptions.PowerShellMaxOutputChars);
        SetPropertyIfPresent(options, "AllowWrite", bootstrapOptions.PowerShellAllowWrite);
        SetPropertyIfPresent(options, "IncludeMaintenancePath", bootstrapOptions.ReviewerSetupIncludeMaintenancePath);
        SetPropertyIfPresent(options, "AuthenticationProbeStore", bootstrapOptions.AuthenticationProbeStore);
        SetPropertyIfPresent(options, "RequireSuccessfulSmtpProbeForSend", bootstrapOptions.RequireSuccessfulSmtpProbeForSend);
        SetPropertyIfPresent(options, "SmtpProbeMaxAgeSeconds", bootstrapOptions.SmtpProbeMaxAgeSeconds);
        SetPropertyIfPresent(options, "RunAsProfilePath", bootstrapOptions.RunAsProfilePath);
        SetPropertyIfPresent(options, "AuthenticationProfilePath", bootstrapOptions.AuthenticationProfilePath);
    }

    /// <summary>
    /// Registers all provided packs into the registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    public static void RegisterAll(ToolRegistry registry, IEnumerable<IToolPack> packs) {
        RegisterAll(registry, packs, toolPackIdsByToolName: null, onRegistrationProgressWarning: null);
    }

    /// <summary>
    /// Registers all provided packs into the registry and optionally records tool-to-pack ownership.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    /// <param name="toolPackIdsByToolName">
    /// Optional sink populated with registered tool definition name to normalized pack id mappings.
    /// </param>
    public static void RegisterAll(ToolRegistry registry, IEnumerable<IToolPack> packs, IDictionary<string, string>? toolPackIdsByToolName) {
        RegisterAll(registry, packs, toolPackIdsByToolName, onRegistrationProgressWarning: null);
    }

    /// <summary>
    /// Registers all provided packs into the registry and optionally records tool-to-pack ownership/progress diagnostics.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    /// <param name="toolPackIdsByToolName">
    /// Optional sink populated with registered tool definition name to normalized pack id mappings.
    /// </param>
    /// <param name="onRegistrationProgressWarning">Optional startup warning sink for per-pack registration progress.</param>
    public static void RegisterAll(
        ToolRegistry registry,
        IEnumerable<IToolPack> packs,
        IDictionary<string, string>? toolPackIdsByToolName,
        Action<string>? onRegistrationProgressWarning) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        var packList = packs as IReadOnlyList<IToolPack> ?? packs.ToArray();
        var knownDefinitions = new HashSet<string>(
            registry.GetDefinitions().Select(static definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalPacks = Math.Max(1, packList.Count);

        for (var packIndex = 0; packIndex < packList.Count; packIndex++) {
            var pack = packList[packIndex];
            var descriptorId = (pack.Descriptor.Id ?? string.Empty).Trim();
            var normalizedPackId = NormalizePackId(descriptorId);
            EnsureNoPackIdNormalizationCollision(descriptorIdsByNormalizedPackId, descriptorId, normalizedPackId);
            if (normalizedPackId.Length == 0) {
                normalizedPackId = "pack_" + (packIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            EmitPackRegistrationProgress(
                onRegistrationProgressWarning,
                normalizedPackId,
                phase: "begin",
                index: packIndex + 1,
                total: totalPacks,
                elapsedMs: null,
                toolsRegistered: null,
                totalTools: null,
                failed: null);

            var registerStopwatch = Stopwatch.StartNew();
            var failed = false;
            var toolsRegistered = 0;
            try {
                pack.Register(registry);

                foreach (var definition in registry.GetDefinitions()) {
                    if (!knownDefinitions.Add(definition.Name)) {
                        continue;
                    }

                    toolsRegistered++;
                    if (toolPackIdsByToolName is not null) {
                        toolPackIdsByToolName[definition.Name] = normalizedPackId;
                    }
                }
            } catch {
                failed = true;
                throw;
            } finally {
                registerStopwatch.Stop();

                EmitPackRegistrationProgress(
                    onRegistrationProgressWarning,
                    normalizedPackId,
                    phase: "end",
                    index: packIndex + 1,
                    total: totalPacks,
                    elapsedMs: Math.Max(1, (long)registerStopwatch.Elapsed.TotalMilliseconds),
                    toolsRegistered: toolsRegistered,
                    totalTools: knownDefinitions.Count,
                    failed: failed);
            }
        }
    }

    /// <summary>
    /// Extracts pack descriptors.
    /// </summary>
    /// <param name="packs">Tool packs.</param>
    /// <returns>Descriptor list.</returns>
    public static IReadOnlyList<ToolPackDescriptor> GetDescriptors(IEnumerable<IToolPack> packs) {
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        var list = new List<ToolPackDescriptor>();
        foreach (var p in packs) {
            list.Add(p.Descriptor);
        }
        return list;
    }

    /// <summary>
    /// Normalizes a source-kind label to one of:
    /// <c>builtin</c>, <c>open_source</c>, or <c>closed_source</c>.
    /// Missing or unknown values are invalid.
    /// </summary>
    /// <param name="sourceKind">Raw source-kind value.</param>
    /// <param name="descriptorId">
    /// Optional descriptor id (accepted for compatibility; not used for inference).
    /// </param>
    /// <returns>Normalized source-kind label.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceKind"/> is missing or invalid.</exception>
    public static string NormalizeSourceKind(string? sourceKind, string? descriptorId = null) {
        _ = descriptorId;
        if (TryNormalizeSourceKind(sourceKind, out var normalized)) {
            return normalized;
        }

        throw new ArgumentException(
            $"SourceKind must be one of '{PackSourceBuiltin}', '{PackSourceOpenSource}', or '{PackSourceClosedSource}' (aliases: open/public, closed/private/internal).",
            nameof(sourceKind));
    }

    /// <summary>
    /// Attempts to normalize a source-kind label to one of:
    /// <c>builtin</c>, <c>open_source</c>, or <c>closed_source</c>.
    /// </summary>
    /// <param name="sourceKind">Raw source-kind value.</param>
    /// <param name="normalized">Normalized source-kind when parsing succeeds.</param>
    /// <returns><see langword="true"/> when normalization succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryNormalizeSourceKind(string? sourceKind, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceKind)) {
            return false;
        }

        var raw = sourceKind.Trim().ToLowerInvariant();
        if (raw is PackSourceBuiltin or PackSourceOpenSource or PackSourceClosedSource) {
            normalized = raw;
            return true;
        }

        if (raw is "open" or "opensource" or "public") {
            normalized = PackSourceOpenSource;
            return true;
        }

        if (raw is "closed" or "private" or "internal") {
            normalized = PackSourceClosedSource;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes descriptor ids into canonical pack ids used across policy and filtering.
    /// </summary>
    /// <param name="descriptorId">Descriptor id.</param>
    /// <returns>Canonical pack id, or empty string when input is empty.</returns>
    public static string NormalizePackId(string? descriptorId) {
        return ToolSelectionMetadata.NormalizePackId(descriptorId);
    }

    private static void EnsureNoPackIdNormalizationCollision(
        IDictionary<string, string> descriptorIdsByNormalizedPackId,
        string descriptorId,
        string normalizedPackId) {
        if (normalizedPackId.Length == 0) {
            return;
        }

        var normalizedDescriptorId = NormalizeCollisionDescriptorId(descriptorId);
        if (descriptorIdsByNormalizedPackId.TryGetValue(normalizedPackId, out var existingDescriptorId)
            && !string.Equals(existingDescriptorId, normalizedDescriptorId, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Tool pack ids '{existingDescriptorId}' and '{normalizedDescriptorId}' both normalize to '{normalizedPackId}'.");
        }

        descriptorIdsByNormalizedPackId[normalizedPackId] = normalizedDescriptorId;
    }

    private static string NormalizeCollisionDescriptorId(string descriptorId) {
        var normalized = (descriptorId ?? string.Empty).Trim();
        return normalized.Length == 0 ? "<empty>" : normalized;
    }

    private static void EmitPackRegistrationProgress(
        Action<string>? onRegistrationProgressWarning,
        string normalizedPackId,
        string phase,
        int index,
        int total,
        long? elapsedMs,
        int? toolsRegistered,
        int? totalTools,
        bool? failed) {
        if (onRegistrationProgressWarning is null) {
            return;
        }

        var packId = string.IsNullOrWhiteSpace(normalizedPackId) ? "pack" : normalizedPackId.Trim();
        var boundedIndex = Math.Max(1, index);
        var boundedTotal = Math.Max(boundedIndex, total);
        if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
            var boundedElapsedMs = Math.Max(1, elapsedMs ?? 1);
            var boundedToolsRegistered = Math.Max(0, toolsRegistered ?? 0);
            var boundedTotalTools = Math.Max(boundedToolsRegistered, totalTools ?? 0);
            onRegistrationProgressWarning(
                $"[startup] pack_register_progress pack='{packId}' phase='end' index='{boundedIndex}' total='{boundedTotal}' " +
                $"elapsed_ms='{boundedElapsedMs}' tools_registered='{boundedToolsRegistered}' total_tools='{boundedTotalTools}' failed='{(failed.GetValueOrDefault() ? 1 : 0)}'");
            return;
        }

        onRegistrationProgressWarning(
            $"[startup] pack_register_progress pack='{packId}' phase='begin' index='{boundedIndex}' total='{boundedTotal}'");
    }

    internal static IToolPack WithSourceKind(IToolPack pack, string sourceKind) {
        if (pack is null) {
            throw new ArgumentNullException(nameof(pack));
        }

        var descriptor = pack.Descriptor;
        var normalized = NormalizeSourceKind(sourceKind, descriptor.Id);
        if (string.Equals(descriptor.SourceKind, normalized, StringComparison.OrdinalIgnoreCase)) {
            return pack;
        }

        return new DescriptorOverrideToolPack(pack, descriptor with { SourceKind = normalized });
    }

    private static ToolPackAvailabilityInfo CreateAvailabilityFromDescriptor(ToolPackDescriptor descriptor, bool enabled, string? disabledReason) {
        if (descriptor is null) {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var normalizedId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedId : descriptor.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);

        return new ToolPackAvailabilityInfo {
            Id = normalizedId.Length == 0 ? descriptor.Id : normalizedId,
            Name = normalizedName,
            Description = normalizedDescription,
            Tier = descriptor.Tier,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            SourceKind = normalizedSourceKind,
            Enabled = enabled,
            DisabledReason = enabled ? null : normalizedReason
        };
    }

    private static void UpsertAvailability(Dictionary<string, ToolPackAvailabilityInfo> availabilityById, ToolPackAvailabilityInfo availability) {
        var normalizedPackId = NormalizePackId(availability.Id);
        if (normalizedPackId.Length == 0) {
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(availability.Name) ? normalizedPackId : availability.Name.Trim();
        availabilityById[normalizedPackId] = availability with { Id = normalizedPackId, Name = normalizedName };
    }

    private static IToolPack RequireDeclaredSourceKind(IToolPack pack, string packLabel) {
        var descriptorSourceKind = (pack.Descriptor.SourceKind ?? string.Empty).Trim();
        if (descriptorSourceKind.Length == 0) {
            throw new InvalidOperationException($"{packLabel} pack is missing descriptor SourceKind.");
        }

        return WithSourceKind(pack, descriptorSourceKind);
    }

    private static void SetPropertyIfPresent(object instance, string propertyName, object? value) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite) {
            return;
        }

        if (value is null) {
            property.SetValue(instance, null);
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var valueType = value.GetType();

        if (targetType.IsAssignableFrom(valueType)) {
            property.SetValue(instance, value);
            return;
        }

        try {
            var converted = Convert.ChangeType(value, targetType);
            property.SetValue(instance, converted);
        } catch {
            // Ignore conversion failures; keep pack defaults.
        }
    }

    private static void AddStringListValuesIfPresent(object instance, string propertyName, IEnumerable<string> values) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead) {
            return;
        }

        if (property.GetValue(instance) is not System.Collections.IList list) {
            return;
        }

        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            list.Add(value);
        }
    }

    private static string NormalizeDisabledReason(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return UnavailableReasonFallback;
        }

        normalized = normalized.Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return normalized.Length == 0 ? UnavailableReasonFallback : normalized;
    }

    private static void Warn(Action<string>? onWarning, string message, bool shouldWarn) {
        if (!shouldWarn) {
            return;
        }
        onWarning?.Invoke(message);
    }

    private sealed class DescriptorOverrideToolPack : IToolPack {
        private readonly IToolPack _inner;

        public DescriptorOverrideToolPack(IToolPack inner, ToolPackDescriptor descriptor) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            _inner.Register(registry);
        }
    }

    private sealed record BuiltInPackRegistrationCandidate(
        string PackId,
        IToolPack Pack,
        bool DefaultEnabled);

}
