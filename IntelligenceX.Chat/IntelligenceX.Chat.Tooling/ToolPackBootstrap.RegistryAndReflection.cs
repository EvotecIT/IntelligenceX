using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.ReviewerSetup;

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

    /// <summary>
    /// Registers all provided packs into the registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    public static void RegisterAll(ToolRegistry registry, IEnumerable<IToolPack> packs) {
        RegisterAll(registry, packs, toolPackIdsByToolName: null);
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
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        var knownDefinitions = new HashSet<string>(
            registry.GetDefinitions().Select(static definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in packs) {
            var descriptorId = (pack.Descriptor.Id ?? string.Empty).Trim();
            var normalizedPackId = NormalizePackId(descriptorId);
            EnsureNoPackIdNormalizationCollision(descriptorIdsByNormalizedPackId, descriptorId, normalizedPackId);

            pack.Register(registry);

            if (normalizedPackId.Length == 0) {
                foreach (var definition in registry.GetDefinitions()) {
                    knownDefinitions.Add(definition.Name);
                }
                continue;
            }

            foreach (var definition in registry.GetDefinitions()) {
                if (!knownDefinitions.Add(definition.Name)) {
                    continue;
                }

                if (toolPackIdsByToolName is not null && normalizedPackId.Length > 0) {
                    toolPackIdsByToolName[definition.Name] = normalizedPackId;
                }
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

    private static void AddOptionalReflectionPack(
        List<IToolPack> packs,
        Dictionary<string, ToolPackAvailabilityInfo> availabilityById,
        bool enabledByConfiguration,
        KnownPackDefinition definition,
        string packLabel,
        string packTypeName,
        string? optionsTypeName,
        Action<object>? configureOptions,
        Action<string>? onWarning) {
        if (!enabledByConfiguration) {
            UpsertAvailability(availabilityById, CreateAvailabilityFromDefinition(definition, enabled: false, DisabledByRuntimeConfigurationReason));
            return;
        }

        var loaded = TryAddPack(
            packs: packs,
            packLabel: packLabel,
            packTypeName: packTypeName,
            optionsTypeName: optionsTypeName,
            configureOptions: configureOptions,
            warnWhenUnavailable: true,
            onWarning: onWarning,
            loadedPack: out var loadedPack,
            unavailableReason: out var unavailableReason);

        if (loaded && loadedPack is not null) {
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDescriptor(
                    descriptor: loadedPack.Descriptor,
                    enabled: true,
                    disabledReason: null));
            return;
        }

        UpsertAvailability(
            availabilityById,
            CreateAvailabilityFromDefinition(
                definition: definition,
                enabled: false,
                disabledReason: unavailableReason));
    }

    private static void AddOptionalBuiltInPack(
        List<IToolPack> packs,
        Dictionary<string, ToolPackAvailabilityInfo> availabilityById,
        bool enabledByConfiguration,
        KnownPackDefinition definition,
        Func<IToolPack> createPack,
        Action<string>? onWarning) {
        if (!enabledByConfiguration) {
            UpsertAvailability(availabilityById, CreateAvailabilityFromDefinition(definition, enabled: false, DisabledByRuntimeConfigurationReason));
            return;
        }

        try {
            var pack = createPack();
            packs.Add(pack);
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDescriptor(
                    descriptor: pack.Descriptor,
                    enabled: true,
                    disabledReason: null));
        } catch (Exception ex) {
            var reason = NormalizeDisabledReason(ex.Message);
            Warn(onWarning, $"{definition.Name} pack skipped: {reason}", shouldWarn: true);
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDefinition(
                    definition: definition,
                    enabled: false,
                    disabledReason: reason));
        }
    }

    private static ToolPackAvailabilityInfo CreateAvailabilityFromDefinition(KnownPackDefinition definition, bool enabled, string? disabledReason) {
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);
        return new ToolPackAvailabilityInfo {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Tier = definition.Tier,
            IsDangerous = definition.IsDangerous,
            SourceKind = NormalizeSourceKind(definition.SourceKind, definition.Id),
            Enabled = enabled,
            DisabledReason = enabled ? null : normalizedReason
        };
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

    private static bool TryAddPack(
        List<IToolPack> packs,
        string packLabel,
        string packTypeName,
        string? optionsTypeName,
        Action<object>? configureOptions,
        bool warnWhenUnavailable,
        Action<string>? onWarning,
        out IToolPack? loadedPack,
        out string unavailableReason) {
        loadedPack = null;
        unavailableReason = UnavailableReasonFallback;

        try {
            var packType = ResolveType(packTypeName);
            if (packType is null) {
                unavailableReason = "Required assembly was not found.";
                Warn(onWarning, $"{packLabel} pack unavailable (assembly not found).", warnWhenUnavailable);
                return false;
            }

            object? options = null;
            if (!string.IsNullOrWhiteSpace(optionsTypeName)) {
                var optionsType = ResolveType(optionsTypeName);
                if (optionsType is null) {
                    unavailableReason = "Pack options type was not found.";
                    Warn(onWarning, $"{packLabel} pack unavailable (options type not found).", warnWhenUnavailable);
                    return false;
                }

                options = Activator.CreateInstance(optionsType);
                if (options is null) {
                    unavailableReason = "Could not create pack options.";
                    Warn(onWarning, $"{packLabel} pack unavailable (cannot create options instance).", warnWhenUnavailable);
                    return false;
                }
                configureOptions?.Invoke(options);
            }

            object? instance = options is null
                ? Activator.CreateInstance(packType)
                : Activator.CreateInstance(packType, options);

            if (instance is not IToolPack pack) {
                unavailableReason = "Pack type does not implement IToolPack.";
                Warn(onWarning, $"{packLabel} pack unavailable (does not implement IToolPack).", warnWhenUnavailable);
                return false;
            }

            loadedPack = RequireDeclaredSourceKind(pack, packLabel);
            packs.Add(loadedPack);
            unavailableReason = string.Empty;
            return true;
        } catch (Exception ex) {
            unavailableReason = NormalizeDisabledReason(ex.Message);
            Warn(onWarning, $"{packLabel} pack skipped: {unavailableReason}", warnWhenUnavailable);
            return false;
        }
    }

    private static IToolPack RequireDeclaredSourceKind(IToolPack pack, string packLabel) {
        var descriptorSourceKind = (pack.Descriptor.SourceKind ?? string.Empty).Trim();
        if (descriptorSourceKind.Length == 0) {
            throw new InvalidOperationException($"{packLabel} pack is missing descriptor SourceKind.");
        }

        return WithSourceKind(pack, descriptorSourceKind);
    }

    private static Type? ResolveType(string assemblyQualifiedTypeName) {
        var resolved = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
        if (resolved is not null) {
            return resolved;
        }

        var parts = assemblyQualifiedTypeName.Split(',', count: 2, options: StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            return null;
        }

        try {
            var assembly = Assembly.Load(new AssemblyName(parts[1]));
            return assembly.GetType(parts[0], throwOnError: false, ignoreCase: false);
        } catch {
            return null;
        }
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

    private sealed record KnownPackDefinition(
        string Id,
        string Name,
        string Description,
        ToolCapabilityTier Tier,
        bool IsDangerous,
        string SourceKind);
}
