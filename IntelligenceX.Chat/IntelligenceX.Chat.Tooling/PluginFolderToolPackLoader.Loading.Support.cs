using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

internal static partial class PluginFolderToolPackLoader {
    private static PluginManifest? TryReadManifest(string manifestPath, Action<string>? onWarning) {
        try {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });
            if (manifest is null) {
                onWarning?.Invoke($"[plugin] manifest_invalid path='{manifestPath}' error='empty manifest'");
                return null;
            }

            if (!TryValidateManifest(manifest, out var validationError)) {
                onWarning?.Invoke($"[plugin] manifest_invalid path='{manifestPath}' error='{validationError}'");
                return null;
            }

            return manifest;
        } catch (Exception ex) {
            onWarning?.Invoke($"[plugin] manifest_invalid path='{manifestPath}' error='{ex.GetType().Name}: {ex.Message}'");
            return null;
        }
    }

    private static bool TryValidateManifest(PluginManifest manifest, out string error) {
        if (manifest.SchemaVersion is null) {
            error = $"missing required schemaVersion (supported={SupportedSchemaVersion}).";
            return false;
        }

        if (manifest.SchemaVersion.Value != SupportedSchemaVersion) {
            error = $"unsupported schemaVersion '{manifest.SchemaVersion.Value}' (supported={SupportedSchemaVersion}).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.PluginId)) {
            error = "missing required pluginId.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) {
            error = "missing required entryAssembly.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType)) {
            error = "missing required entryType.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string DeterminePluginId(string pluginDirectory, PluginManifest? manifest) {
        if (!string.IsNullOrWhiteSpace(manifest?.PluginId)) {
            return manifest!.PluginId!.Trim();
        }

        return Path.GetFileName(pluginDirectory);
    }

    private static IReadOnlyList<string> ResolveEntryAssemblyPaths(
        string pluginDirectory,
        PluginManifest manifest,
        string pluginId,
        Action<string>? onWarning) {
        var configured = (manifest.EntryAssembly ?? string.Empty).Trim();
        if (configured.Length == 0) {
            onWarning?.Invoke($"[plugin] manifest_invalid plugin='{pluginId}' error='missing required entryAssembly'");
            return Array.Empty<string>();
        }

        if (Path.IsPathRooted(configured)) {
            onWarning?.Invoke($"[plugin] manifest_invalid plugin='{pluginId}' error='entryAssembly must be relative to plugin root'");
            return Array.Empty<string>();
        }

        var pluginRootPath = NormalizePath(pluginDirectory);
        if (string.IsNullOrWhiteSpace(pluginRootPath)) {
            onWarning?.Invoke($"[plugin] dependency_missing plugin='{pluginId}' error='plugin root path is invalid'");
            return Array.Empty<string>();
        }

        if (!pluginRootPath.EndsWith(Path.DirectorySeparatorChar)) {
            pluginRootPath += Path.DirectorySeparatorChar;
        }

        var candidate = NormalizePath(Path.Combine(pluginDirectory, configured));
        if (string.IsNullOrWhiteSpace(candidate)) {
            onWarning?.Invoke($"[plugin] entry_not_found plugin='{pluginId}' entryAssembly='{configured}'");
            return Array.Empty<string>();
        }

        var isUnderPluginRoot = candidate.StartsWith(pluginRootPath, StringComparison.OrdinalIgnoreCase);
        if (!isUnderPluginRoot) {
            onWarning?.Invoke($"[plugin] manifest_invalid plugin='{pluginId}' error='entryAssembly escapes plugin root'");
            return Array.Empty<string>();
        }

        if (!File.Exists(candidate)) {
            onWarning?.Invoke($"[plugin] entry_not_found plugin='{pluginId}' entryAssembly='{configured}'");
            return Array.Empty<string>();
        }

        return new[] { candidate };
    }

    private static void PreloadPluginDependencies(
        string pluginDirectory,
        IReadOnlyList<string> entryAssemblyPaths,
        string pluginId,
        Action<string>? onWarning) {
        string[] dlls;
        try {
            dlls = Directory
                .EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (Exception ex) {
            onWarning?.Invoke($"[plugin] dependency_missing plugin='{pluginId}' error='{ex.GetType().Name}: {ex.Message}'");
            return;
        }

        var entrySet = new HashSet<string>(entryAssemblyPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var dll in dlls) {
            var fullPath = NormalizePath(dll);
            if (string.IsNullOrWhiteSpace(fullPath)) {
                continue;
            }

            if (entrySet.Contains(fullPath)) {
                continue;
            }

            _ = TryLoadAssembly(fullPath, pluginId, onWarning, warnOnFailure: false);
        }
    }

    private static Assembly? TryLoadAssembly(string assemblyPath, string pluginId, Action<string>? onWarning, bool warnOnFailure = true) {
        try {
            var name = AssemblyName.GetAssemblyName(assemblyPath);
            var existing = FindReusableLoadedAssembly(assemblyPath, name);
            if (existing is not null) {
                return existing;
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        } catch (BadImageFormatException) {
            return null;
        } catch (Exception ex) {
            if (warnOnFailure) {
                onWarning?.Invoke(
                    $"[plugin] dependency_missing plugin='{pluginId}' assembly='{Path.GetFileName(assemblyPath)}' error='{ex.GetType().Name}: {ex.Message}'");
            }

            return null;
        }
    }

    internal static bool CanReuseLoadedAssembly(Assembly loadedAssembly, AssemblyName requestedName, string assemblyPath) {
        ArgumentNullException.ThrowIfNull(loadedAssembly);
        ArgumentNullException.ThrowIfNull(requestedName);

        if (IsLoadedAssemblyPathMatch(loadedAssembly, assemblyPath)) {
            return true;
        }

        return AssemblyIdentityMatches(loadedAssembly.GetName(), requestedName);
    }

    private static Assembly? FindReusableLoadedAssembly(string assemblyPath, AssemblyName requestedName) {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(loadedAssembly => CanReuseLoadedAssembly(loadedAssembly, requestedName, assemblyPath));
    }

    private static bool IsLoadedAssemblyPathMatch(Assembly loadedAssembly, string assemblyPath) {
        var requestedPath = NormalizePath(assemblyPath);
        if (string.IsNullOrWhiteSpace(requestedPath)) {
            return false;
        }

        string? loadedLocation;
        try {
            loadedLocation = NormalizePath(loadedAssembly.Location);
        } catch {
            return false;
        }

        return !string.IsNullOrWhiteSpace(loadedLocation)
               && string.Equals(loadedLocation, requestedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AssemblyIdentityMatches(AssemblyName loadedName, AssemblyName requestedName) {
        var loadedSimpleName = (loadedName.Name ?? string.Empty).Trim();
        var requestedSimpleName = (requestedName.Name ?? string.Empty).Trim();
        if (loadedSimpleName.Length == 0
            || requestedSimpleName.Length == 0
            || !string.Equals(loadedSimpleName, requestedSimpleName, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!Equals(loadedName.Version, requestedName.Version)) {
            return false;
        }

        if (!string.Equals(
                NormalizeCultureName(loadedName.CultureName),
                NormalizeCultureName(requestedName.CultureName),
                StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return PublicKeyTokensEqual(loadedName.GetPublicKeyToken(), requestedName.GetPublicKeyToken());
    }

    private static string NormalizeCultureName(string? cultureName) {
        return string.IsNullOrWhiteSpace(cultureName)
            ? string.Empty
            : cultureName.Trim();
    }

    private static bool PublicKeyTokensEqual(byte[]? left, byte[]? right) {
        left ??= [];
        right ??= [];

        if (left.Length != right.Length) {
            return false;
        }

        for (var i = 0; i < left.Length; i++) {
            if (left[i] != right[i]) {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<Type> ResolveCandidatePackTypes(
        Assembly entryAssembly,
        PluginManifest manifest,
        string pluginId,
        Action<string>? onWarning) {
        var configuredTypeName = (manifest.EntryType ?? string.Empty).Trim();
        if (configuredTypeName.Length == 0) {
            onWarning?.Invoke($"[plugin] manifest_invalid plugin='{pluginId}' error='missing required entryType'");
            return Array.Empty<Type>();
        }

        var configuredType = entryAssembly.GetType(configuredTypeName, throwOnError: false, ignoreCase: false);
        if (configuredType is null) {
            onWarning?.Invoke($"[plugin] entry_not_found plugin='{pluginId}' entryType='{manifest.EntryType}'");
            return Array.Empty<Type>();
        }

        if (!typeof(IToolPack).IsAssignableFrom(configuredType)) {
            onWarning?.Invoke($"[plugin] entry_not_found plugin='{pluginId}' entryType='{manifest.EntryType}' error='type does not implement IToolPack'");
            return Array.Empty<Type>();
        }

        return new[] { configuredType };
    }

    private static bool TryCreatePack(
        Type packType,
        string? pluginId,
        ToolPackBootstrapOptions bootstrapOptions,
        out IToolPack pack,
        out string error) {
        pack = null!;

        try {
            var parameterlessCtor = packType.GetConstructor(Type.EmptyTypes);
            if (parameterlessCtor is not null) {
                var created = parameterlessCtor.Invoke(parameters: null);
                if (created is IToolPack toolPack) {
                    pack = toolPack;
                    error = string.Empty;
                    return true;
                }
            }

            var constructors = packType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var ctor in constructors) {
                var parameters = ctor.GetParameters();
                if (parameters.Length != 1) {
                    continue;
                }

                var optionsType = parameters[0].ParameterType;
                object? options;
                try {
                    options = Activator.CreateInstance(optionsType);
                } catch {
                    continue;
                }

                if (options is null) {
                    continue;
                }

                ConfigurePackOptions(options, bootstrapOptions, packType, pluginId);
                var created = ctor.Invoke(new[] { options });
                if (created is IToolPack toolPack) {
                    pack = toolPack;
                    error = string.Empty;
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

    private static void ConfigurePackOptions(
        object options,
        ToolPackBootstrapOptions bootstrapOptions,
        Type packType,
        string? pluginId) {
        ToolPackBootstrap.ConfigurePackOptionsFromRuntimeBag(options, bootstrapOptions, packType, explicitPackKey: pluginId);
    }

    private static string? NormalizePath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        try {
            return Path.GetFullPath(path.Trim());
        } catch {
            return null;
        }
    }

    private static PackEnablementDecision ResolvePackEnablement(
        string normalizedPackId,
        PluginManifest? manifest,
        ToolPackBootstrapOptions options) {
        if (string.IsNullOrWhiteSpace(normalizedPackId)) {
            return new PackEnablementDecision(Enabled: false, DisabledReason: DisabledByRuntimeConfigurationReason);
        }

        if (ContainsNormalizedPackId(options.DisabledPackIds, normalizedPackId)) {
            return new PackEnablementDecision(Enabled: false, DisabledReason: DisabledByRuntimeConfigurationReason);
        }

        if (ContainsNormalizedPackId(options.EnabledPackIds, normalizedPackId)) {
            return new PackEnablementDecision(Enabled: true, DisabledReason: null);
        }

        if (manifest?.DefaultEnabled is bool defaultEnabled && !defaultEnabled) {
            return new PackEnablementDecision(Enabled: false, DisabledReason: DisabledByPluginManifestDefaultReason);
        }

        if (manifest is { DefaultEnabled: null, IsDangerous: true }) {
            return new PackEnablementDecision(Enabled: false, DisabledReason: DisabledByPluginRiskClassificationReason);
        }

        return new PackEnablementDecision(Enabled: true, DisabledReason: null);
    }

    private static bool ContainsNormalizedPackId(IReadOnlyList<string>? packIds, string normalizedPackId) {
        if (packIds is null || packIds.Count == 0 || string.IsNullOrWhiteSpace(normalizedPackId)) {
            return false;
        }

        for (var i = 0; i < packIds.Count; i++) {
            var normalized = ToolPackBootstrap.NormalizePackId(packIds[i]);
            if (normalized.Length == 0) {
                continue;
            }

            if (string.Equals(normalized, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static ToolPackAvailabilityInfo CreatePluginPackAvailability(
        IToolPack pack,
        string normalizedDescriptorId,
        PluginManifest? manifest,
        bool enabled,
        string? disabledReason) {
        var descriptor = pack.Descriptor;
        var name = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedDescriptorId : descriptor.Name.Trim();
        var description = string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description.Trim();
        var sourceKind = TryResolveAvailabilitySourceKind(manifest, descriptor, out var resolvedSourceKind)
            ? resolvedSourceKind
            : ToolPackBootstrap.PackSourceOpenSource;
        var normalizedEngineId = ToolPackMetadataNormalizer.NormalizeDescriptorToken(descriptor.EngineId);
        var normalizedAliases = ToolPackBootstrap.NormalizePackAliases(
            packId: normalizedDescriptorId,
            aliases: descriptor.Aliases);
        var normalizedCategory = ToolPackBootstrap.NormalizePackCategory(descriptor.Category, normalizedDescriptorId);
        var normalizedCapabilityTags = NormalizeDistinctDescriptorTokens(descriptor.CapabilityTags);
        var normalizedSearchTokens = ToolPackBootstrap.NormalizePackSearchTokens(
            packId: normalizedDescriptorId,
            aliases: normalizedAliases,
            category: normalizedCategory,
            engineId: normalizedEngineId,
            explicitSearchTokens: descriptor.SearchTokens);

        return new ToolPackAvailabilityInfo {
            Id = normalizedDescriptorId,
            Name = name,
            Description = description,
            Tier = descriptor.Tier,
            IsDangerous = manifest?.IsDangerous ?? descriptor.IsDangerous,
            SourceKind = sourceKind,
            EngineId = normalizedEngineId.Length == 0 ? null : normalizedEngineId,
            Aliases = normalizedAliases,
            Category = normalizedCategory,
            CapabilityTags = normalizedCapabilityTags,
            SearchTokens = normalizedSearchTokens,
            Enabled = enabled,
            DisabledReason = enabled ? null : disabledReason
        };
    }

    private static IReadOnlyList<string> NormalizeDistinctDescriptorTokens(IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var token = ToolPackMetadataNormalizer.NormalizeDescriptorToken(values[i]);
            if (token.Length == 0 || !seen.Add(token)) {
                continue;
            }

            normalized.Add(token);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    private static bool TryResolveAvailabilitySourceKind(
        PluginManifest? manifest,
        ToolPackDescriptor descriptor,
        out string sourceKind) {
        sourceKind = string.Empty;

        if (!string.IsNullOrWhiteSpace(descriptor.SourceKind)
            && ToolPackBootstrap.TryNormalizeSourceKind(descriptor.SourceKind, out sourceKind)) {
            return true;
        }

        var configured = manifest?.SourceKind;
        if (string.IsNullOrWhiteSpace(configured)) {
            configured = manifest?.Source;
        }
        if (string.IsNullOrWhiteSpace(configured)) {
            configured = manifest?.Visibility;
        }

        return !string.IsNullOrWhiteSpace(configured)
               && ToolPackBootstrap.TryNormalizeSourceKind(configured, out sourceKind);
    }

    private static bool TryResolvePluginSourceKind(PluginManifest? manifest, IToolPack pack, out string sourceKind, out string error) {
        sourceKind = string.Empty;
        error = string.Empty;

        var descriptorValue = pack.Descriptor.SourceKind;
        if (!string.IsNullOrWhiteSpace(descriptorValue)) {
            if (ToolPackBootstrap.TryNormalizeSourceKind(descriptorValue, out sourceKind)) {
                return true;
            }

            error = $"invalid descriptor SourceKind '{descriptorValue}'.";
            return false;
        }

        var configured = manifest?.SourceKind;
        if (string.IsNullOrWhiteSpace(configured)) {
            configured = manifest?.Source;
        }
        if (string.IsNullOrWhiteSpace(configured)) {
            configured = manifest?.Visibility;
        }

        if (string.IsNullOrWhiteSpace(configured)) {
            error = "missing SourceKind in descriptor and manifest (sourceKind/source/visibility).";
            return false;
        }

        if (ToolPackBootstrap.TryNormalizeSourceKind(configured, out sourceKind)) {
            return true;
        }

        error = $"invalid manifest source kind '{configured}'.";
        return false;
    }

    private static ToolPluginCatalogInfo CreatePluginCatalog(
        string rootPath,
        PluginManifest? manifest,
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        Action<string>? onWarning) {
        var pluginId = DeterminePluginId(rootPath, manifest);
        var normalizedPluginId = ToolPackBootstrap.NormalizePackId(pluginId);
        var pluginName = (manifest?.DisplayName ?? string.Empty).Trim();
        if (pluginName.Length == 0) {
            pluginName = normalizedPluginId.Length == 0 ? pluginId : normalizedPluginId;
        }

        var normalizedPackIds = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack is not null)
            .Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Id))
            .Where(static packId => packId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var enabled = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()).Any(static pack => pack.Enabled);
        var disabledReason = enabled
            ? null
            : (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
                .Select(static pack => (pack.DisabledReason ?? string.Empty).Trim())
                .FirstOrDefault(static reason => reason.Length > 0);
        var sourceKind = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Select(static pack => (pack.SourceKind ?? string.Empty).Trim())
            .FirstOrDefault(static kind => kind.Length > 0) ?? string.Empty;
        if (sourceKind.Length == 0) {
            sourceKind = ToolPackBootstrap.PackSourceOpenSource;
        }

        var skillDirectories = ResolvePluginSkillDirectories(rootPath, manifest);
        return new ToolPluginCatalogInfo {
            Id = normalizedPluginId.Length == 0 ? pluginId : normalizedPluginId,
            Name = pluginName,
            Version = string.IsNullOrWhiteSpace(manifest?.Version) ? null : manifest.Version.Trim(),
            Origin = "plugin_folder",
            SourceKind = sourceKind,
            DefaultEnabled = manifest?.DefaultEnabled ?? true,
            IsDangerous = (manifest?.IsDangerous ?? false) || (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()).Any(static pack => pack.IsDangerous),
            PackIds = normalizedPackIds,
            RootPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath,
            SkillDirectories = skillDirectories,
            SkillIds = ResolvePluginSkillIds(skillDirectories, onWarning)
        };
    }

    private static ToolPluginAvailabilityInfo CreatePluginAvailability(
        ToolPluginCatalogInfo pluginCatalog,
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability) {
        var enabled = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()).Any(static pack => pack.Enabled);
        var disabledReason = enabled
            ? null
            : (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
                .Select(static pack => (pack.DisabledReason ?? string.Empty).Trim())
                .FirstOrDefault(static reason => reason.Length > 0);

        return new ToolPluginAvailabilityInfo {
            Id = pluginCatalog.Id,
            Name = pluginCatalog.Name,
            Version = pluginCatalog.Version,
            Origin = pluginCatalog.Origin,
            SourceKind = pluginCatalog.SourceKind,
            DefaultEnabled = pluginCatalog.DefaultEnabled,
            Enabled = enabled,
            DisabledReason = disabledReason,
            IsDangerous = pluginCatalog.IsDangerous,
            PackIds = pluginCatalog.PackIds ?? Array.Empty<string>(),
            RootPath = pluginCatalog.RootPath,
            SkillDirectories = pluginCatalog.SkillDirectories ?? Array.Empty<string>(),
            SkillIds = pluginCatalog.SkillIds ?? Array.Empty<string>()
        };
    }

    private readonly record struct PackEnablementDecision(bool Enabled, string? DisabledReason);
}
