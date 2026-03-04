using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

internal static partial class PluginFolderToolPackLoader {
    private const string DisabledByRuntimeConfigurationReason = "Disabled by runtime configuration.";
    private const string DisabledByPluginManifestDefaultReason = "Disabled by plugin manifest default.";
    private const string DisabledByPluginRiskClassificationReason = "Disabled by plugin risk classification (dangerous plugin).";
    private const string DisabledByDuplicatePackReason = "Duplicate pack id already loaded.";
    private static readonly TimeSpan SlowPluginLoadWarningThreshold = TimeSpan.FromMilliseconds(500);

    private static bool TryLoadPluginDirectory(
        string pluginDirectory,
        bool isExplicitRoot,
        ToolPackBootstrapOptions options,
        List<IToolPack> packs,
        HashSet<string> existingPackIds,
        IReadOnlyDictionary<string, IReadOnlyList<IToolPack>> loadedPacksByAssemblyName,
        Action<string>? onWarning,
        Action<ToolPackAvailabilityInfo>? onPackAvailability,
        int loadIndex,
        int loadTotal) {
        PluginManifest? manifest = null;
        var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
        if (File.Exists(manifestPath)) {
            manifest = TryReadManifest(manifestPath, onWarning);
        }

        var pluginId = DeterminePluginId(pluginDirectory, manifest);
        var progressTotal = Math.Max(1, loadTotal);
        var progressIndex = Math.Clamp(loadIndex, 1, progressTotal);
        onWarning?.Invoke(
            $"[plugin] load_progress plugin='{pluginId}' phase='begin' index='{progressIndex}' total='{progressTotal}'");

        var loadStopwatch = Stopwatch.StartNew();
        var entryAssemblyCount = 0;
        var candidateTypeCount = 0;
        var loadedPackCount = 0;
        var disabledPackCount = 0;
        var duplicatePackCount = 0;
        var failedPackCount = 0;

        try {
            var entryAssemblyPaths = ResolveEntryAssemblyPaths(pluginDirectory, manifest, pluginId, onWarning);
            entryAssemblyCount = entryAssemblyPaths.Count;
            if (entryAssemblyPaths.Count == 0) {
                return false;
            }

            if (TryShortCircuitDuplicatePluginFromLoadedPackAssemblyMap(
                    entryAssemblyPaths: entryAssemblyPaths,
                    loadedPacksByAssemblyName: loadedPacksByAssemblyName,
                    manifest: manifest,
                    pluginId: pluginId,
                    isExplicitRoot: isExplicitRoot,
                    existingPackIds: existingPackIds,
                    onWarning: onWarning,
                    onPackAvailability: onPackAvailability,
                    duplicatePackCount: ref duplicatePackCount,
                    out var assemblyMapCandidateTypeCount)) {
                candidateTypeCount = assemblyMapCandidateTypeCount;
                return false;
            }

            if (TryShortCircuitDuplicatePluginFromLoadedAssemblies(
                    entryAssemblyPaths: entryAssemblyPaths,
                    manifest: manifest,
                    pluginId: pluginId,
                    isExplicitRoot: isExplicitRoot,
                    options: options,
                    existingPackIds: existingPackIds,
                    onWarning: onWarning,
                    onPackAvailability: onPackAvailability,
                    duplicatePackCount: ref duplicatePackCount,
                    failedPackCount: ref failedPackCount,
                    candidateTypeCount: out var fastPathCandidateTypeCount)) {
                candidateTypeCount = fastPathCandidateTypeCount;
                return false;
            }

            PreloadPluginDependencies(pluginDirectory, entryAssemblyPaths, pluginId, onWarning);

            IReadOnlyList<Type> candidateTypes = Array.Empty<Type>();
            for (var i = 0; i < entryAssemblyPaths.Count; i++) {
                var assemblyPath = entryAssemblyPaths[i];
                var warnOnLoadFailure = i == entryAssemblyPaths.Count - 1;
                var entryAssembly = TryLoadAssembly(assemblyPath, pluginId, onWarning, warnOnFailure: warnOnLoadFailure);
                if (entryAssembly is null) {
                    continue;
                }

                candidateTypes = ResolveCandidatePackTypes(entryAssembly, manifest, pluginId, onWarning);
                if (candidateTypes.Count > 0) {
                    break;
                }
            }

            if (candidateTypes.Count == 0) {
                if (string.IsNullOrWhiteSpace(manifest?.EntryType)) {
                    onWarning?.Invoke($"[plugin] entry_not_found plugin='{pluginId}' error='no IToolPack implementations found in plugin folder'");
                }
                return false;
            }
            candidateTypeCount = candidateTypes.Count;

            foreach (var candidateType in candidateTypes) {
                if (!TryCreatePack(candidateType, options, out var pack, out var error)) {
                    onWarning?.Invoke($"[plugin] init_failed plugin='{pluginId}' type='{candidateType.FullName}' error='{error}'");
                    failedPackCount++;
                    continue;
                }

                var descriptorId = pack.Descriptor.Id?.Trim();
                var normalizedDescriptorId = ToolPackBootstrap.NormalizePackId(descriptorId);
                if (normalizedDescriptorId.Length == 0) {
                    onWarning?.Invoke($"[plugin] init_failed plugin='{pluginId}' type='{candidateType.FullName}' error='descriptor id is empty'");
                    failedPackCount++;
                    continue;
                }

                if (existingPackIds.Contains(normalizedDescriptorId)) {
                    if (isExplicitRoot) {
                        onWarning?.Invoke($"[plugin] duplicate_pack plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped'");
                    }
                    duplicatePackCount++;
                    onPackAvailability?.Invoke(CreatePluginPackAvailability(
                        pack: pack,
                        normalizedDescriptorId: normalizedDescriptorId,
                        manifest: manifest,
                        enabled: false,
                        disabledReason: DisabledByDuplicatePackReason));
                    continue;
                }

                var enablementDecision = ResolvePackEnablement(normalizedDescriptorId, manifest, options);
                if (!enablementDecision.Enabled) {
                    if (isExplicitRoot) {
                        onWarning?.Invoke($"[plugin] pack_disabled plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped'");
                    }
                    disabledPackCount++;
                    onPackAvailability?.Invoke(CreatePluginPackAvailability(
                        pack: pack,
                        normalizedDescriptorId: normalizedDescriptorId,
                        manifest: manifest,
                        enabled: false,
                        disabledReason: enablementDecision.DisabledReason));
                    continue;
                }

                if (!TryResolvePluginSourceKind(manifest, pack, out var sourceKind, out var sourceKindError)) {
                    onWarning?.Invoke($"[plugin] source_kind_missing plugin='{pluginId}' descriptor='{descriptorId}' error='{sourceKindError}'");
                    failedPackCount++;
                    onPackAvailability?.Invoke(CreatePluginPackAvailability(
                        pack: pack,
                        normalizedDescriptorId: normalizedDescriptorId,
                        manifest: manifest,
                        enabled: false,
                        disabledReason: sourceKindError));
                    continue;
                }

                pack = ToolPackBootstrap.WithSourceKind(pack, sourceKind);
                existingPackIds.Add(normalizedDescriptorId);
                packs.Add(pack);
                loadedPackCount++;
            }
        } finally {
            loadStopwatch.Stop();
            onWarning?.Invoke(
                $"[plugin] load_progress plugin='{pluginId}' phase='end' index='{progressIndex}' total='{progressTotal}' " +
                $"elapsed_ms='{Math.Max(1, (long)loadStopwatch.Elapsed.TotalMilliseconds)}' " +
                $"loaded='{loadedPackCount}' disabled='{disabledPackCount}' duplicate='{duplicatePackCount}' failed='{failedPackCount}'");
            if (loadStopwatch.Elapsed >= SlowPluginLoadWarningThreshold) {
                onWarning?.Invoke(
                    $"[plugin] load_timing plugin='{pluginId}' " +
                    $"elapsed_ms='{Math.Max(1, (long)loadStopwatch.Elapsed.TotalMilliseconds)}' " +
                    $"entry_assemblies='{entryAssemblyCount}' " +
                    $"candidate_types='{candidateTypeCount}' " +
                    $"loaded='{loadedPackCount}' " +
                    $"disabled='{disabledPackCount}' " +
                    $"duplicate='{duplicatePackCount}' " +
                    $"failed='{failedPackCount}'");
            }
        }

        return loadedPackCount > 0;
    }

    private static bool TryShortCircuitDuplicatePluginFromLoadedPackAssemblyMap(
        IReadOnlyList<string> entryAssemblyPaths,
        IReadOnlyDictionary<string, IReadOnlyList<IToolPack>> loadedPacksByAssemblyName,
        PluginManifest? manifest,
        string pluginId,
        bool isExplicitRoot,
        HashSet<string> existingPackIds,
        Action<string>? onWarning,
        Action<ToolPackAvailabilityInfo>? onPackAvailability,
        ref int duplicatePackCount,
        out int candidateTypeCount) {
        candidateTypeCount = 0;
        if (entryAssemblyPaths.Count == 0
            || existingPackIds.Count == 0
            || loadedPacksByAssemblyName.Count == 0) {
            return false;
        }

        var primaryEntryAssemblyName = Path.GetFileNameWithoutExtension(entryAssemblyPaths[0]);
        if (string.IsNullOrWhiteSpace(primaryEntryAssemblyName)
            || !loadedPacksByAssemblyName.TryGetValue(primaryEntryAssemblyName, out var candidatePacks)
            || candidatePacks.Count == 0) {
            return false;
        }

        var duplicatePacks = new Dictionary<string, IToolPack>(StringComparer.OrdinalIgnoreCase);
        var manifestEntryType = (manifest?.EntryType ?? string.Empty).Trim();
        for (var i = 0; i < candidatePacks.Count; i++) {
            var pack = candidatePacks[i];
            if (pack is null) {
                continue;
            }

            if (manifestEntryType.Length > 0
                && !string.Equals(pack.GetType().FullName, manifestEntryType, StringComparison.Ordinal)) {
                continue;
            }

            var normalizedDescriptorId = ToolPackBootstrap.NormalizePackId(pack.Descriptor.Id);
            if (normalizedDescriptorId.Length == 0 || !existingPackIds.Contains(normalizedDescriptorId)) {
                return false;
            }

            duplicatePacks[normalizedDescriptorId] = pack;
        }

        if (duplicatePacks.Count == 0) {
            return false;
        }

        candidateTypeCount = duplicatePacks.Count;
        foreach (var pair in duplicatePacks.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)) {
            var normalizedDescriptorId = pair.Key;
            var pack = pair.Value;
            if (isExplicitRoot) {
                onWarning?.Invoke(
                    $"[plugin] duplicate_pack plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped' mode='assembly_map'");
            }

            duplicatePackCount++;
            onPackAvailability?.Invoke(CreatePluginPackAvailability(
                pack: pack,
                normalizedDescriptorId: normalizedDescriptorId,
                manifest: manifest,
                enabled: false,
                disabledReason: DisabledByDuplicatePackReason));
        }

        return true;
    }

    private static bool TryShortCircuitDuplicatePluginFromLoadedAssemblies(
        IReadOnlyList<string> entryAssemblyPaths,
        PluginManifest? manifest,
        string pluginId,
        bool isExplicitRoot,
        ToolPackBootstrapOptions options,
        HashSet<string> existingPackIds,
        Action<string>? onWarning,
        Action<ToolPackAvailabilityInfo>? onPackAvailability,
        ref int duplicatePackCount,
        ref int failedPackCount,
        out int candidateTypeCount) {
        candidateTypeCount = 0;
        if (entryAssemblyPaths.Count == 0 || existingPackIds.Count == 0) {
            return false;
        }

        var candidatePacksById = new Dictionary<string, IToolPack>(StringComparer.OrdinalIgnoreCase);
        var anyCandidateTypesResolved = false;

        for (var i = 0; i < entryAssemblyPaths.Count; i++) {
            var assemblyPath = entryAssemblyPaths[i];
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (string.IsNullOrWhiteSpace(assemblyName)) {
                continue;
            }

            var loadedAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            if (loadedAssembly is null) {
                continue;
            }

            var candidateTypes = ResolveCandidatePackTypes(loadedAssembly, manifest, pluginId, onWarning: null);
            if (candidateTypes.Count == 0) {
                continue;
            }

            anyCandidateTypesResolved = true;
            candidateTypeCount += candidateTypes.Count;
            for (var typeIndex = 0; typeIndex < candidateTypes.Count; typeIndex++) {
                var candidateType = candidateTypes[typeIndex];
                if (!TryCreatePack(candidateType, options, out var pack, out _)) {
                    failedPackCount++;
                    return false;
                }

                var descriptorId = pack.Descriptor.Id?.Trim();
                var normalizedDescriptorId = ToolPackBootstrap.NormalizePackId(descriptorId);
                if (normalizedDescriptorId.Length == 0 || !existingPackIds.Contains(normalizedDescriptorId)) {
                    return false;
                }

                candidatePacksById[normalizedDescriptorId] = pack;
            }
        }

        if (!anyCandidateTypesResolved || candidatePacksById.Count == 0) {
            return false;
        }

        foreach (var pair in candidatePacksById.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)) {
            var normalizedDescriptorId = pair.Key;
            var pack = pair.Value;
            if (isExplicitRoot) {
                onWarning?.Invoke(
                    $"[plugin] duplicate_pack plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped' mode='fastpath'");
            }

            duplicatePackCount++;
            onPackAvailability?.Invoke(CreatePluginPackAvailability(
                pack: pack,
                normalizedDescriptorId: normalizedDescriptorId,
                manifest: manifest,
                enabled: false,
                disabledReason: DisabledByDuplicatePackReason));
        }

        return true;
    }

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

            if (manifest.SchemaVersion.HasValue && manifest.SchemaVersion.Value != SupportedSchemaVersion) {
                onWarning?.Invoke(
                    $"[plugin] manifest_version_mismatch path='{manifestPath}' schema='{manifest.SchemaVersion.Value}' supported='{SupportedSchemaVersion}'");
            }

            return manifest;
        } catch (Exception ex) {
            onWarning?.Invoke($"[plugin] manifest_invalid path='{manifestPath}' error='{ex.GetType().Name}: {ex.Message}'");
            return null;
        }
    }

    private static string DeterminePluginId(string pluginDirectory, PluginManifest? manifest) {
        if (!string.IsNullOrWhiteSpace(manifest?.PluginId)) {
            return manifest!.PluginId!.Trim();
        }
        if (!string.IsNullOrWhiteSpace(manifest?.PackageId)) {
            return manifest!.PackageId!.Trim();
        }
        return Path.GetFileName(pluginDirectory);
    }

    private static IReadOnlyList<string> ResolveEntryAssemblyPaths(
        string pluginDirectory,
        PluginManifest? manifest,
        string pluginId,
        Action<string>? onWarning) {
        if (!string.IsNullOrWhiteSpace(manifest?.EntryAssembly)) {
            var configured = manifest!.EntryAssembly!.Trim();
            var candidate = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(pluginDirectory, configured);

            if (File.Exists(candidate)) {
                var normalized = NormalizePath(candidate);
                if (!string.IsNullOrWhiteSpace(normalized)) {
                    return new[] { normalized };
                }
                return Array.Empty<string>();
            }

            onWarning?.Invoke($"[plugin] entry_not_found plugin='{pluginId}' entryAssembly='{configured}'");
            return Array.Empty<string>();
        }

        string[] dlls;
        try {
            dlls = Directory
                .EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (Exception ex) {
            onWarning?.Invoke($"[plugin] dependency_missing plugin='{pluginId}' error='{ex.GetType().Name}: {ex.Message}'");
            return Array.Empty<string>();
        }

        if (dlls.Length == 0) {
            onWarning?.Invoke($"[plugin] dependency_missing plugin='{pluginId}' error='no .dll files found'");
            return Array.Empty<string>();
        }

        var pluginFolderName = Path.GetFileName(pluginDirectory);
        var ordered = dlls
            .Select(static path => NormalizePath(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .OrderBy(path => ResolveEntryAssemblyPriority(path, pluginId, pluginFolderName))
            .ThenBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ordered;
    }

    private static int ResolveEntryAssemblyPriority(string assemblyPath, string pluginId, string? pluginFolderName) {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        if (string.Equals(assemblyName, pluginId, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(pluginFolderName)
            && string.Equals(assemblyName, pluginFolderName, StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        return 2;
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
            var existing = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
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

    private static IReadOnlyList<Type> ResolveCandidatePackTypes(
        Assembly entryAssembly,
        PluginManifest? manifest,
        string pluginId,
        Action<string>? onWarning) {
        if (!string.IsNullOrWhiteSpace(manifest?.EntryType)) {
            var configuredType = entryAssembly.GetType(manifest!.EntryType!.Trim(), throwOnError: false, ignoreCase: false);
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

        try {
            return entryAssembly
                .GetTypes()
                .Where(static type => type.IsClass && !type.IsAbstract && typeof(IToolPack).IsAssignableFrom(type))
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();
        } catch (ReflectionTypeLoadException ex) {
            var candidates = ex.Types
                .Where(static type => type is not null)
                .Where(static type => type!.IsClass && !type.IsAbstract && typeof(IToolPack).IsAssignableFrom(type))
                .Cast<Type>()
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0) {
                var firstLoaderError = ex.LoaderExceptions?.FirstOrDefault();
                onWarning?.Invoke(
                    $"[plugin] entry_not_found plugin='{pluginId}' error='{firstLoaderError?.GetType().Name}: {firstLoaderError?.Message}'");
            }
            return candidates;
        }
    }

    private static bool TryCreatePack(Type packType, ToolPackBootstrapOptions bootstrapOptions, out IToolPack pack, out string error) {
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

                ConfigurePackOptions(options, bootstrapOptions);
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
        SetPropertyIfPresent(options, "AuthenticationProbeStore", bootstrapOptions.AuthenticationProbeStore);
        SetPropertyIfPresent(options, "RequireSuccessfulSmtpProbeForSend", bootstrapOptions.RequireSuccessfulSmtpProbeForSend);
        SetPropertyIfPresent(options, "SmtpProbeMaxAgeSeconds", bootstrapOptions.SmtpProbeMaxAgeSeconds);
        SetPropertyIfPresent(options, "RunAsProfilePath", bootstrapOptions.RunAsProfilePath);
        SetPropertyIfPresent(options, "AuthenticationProfilePath", bootstrapOptions.AuthenticationProfilePath);
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
        if (targetType.IsInstanceOfType(value)) {
            property.SetValue(instance, value);
            return;
        }

        try {
            var converted = Convert.ChangeType(value, targetType);
            property.SetValue(instance, converted);
        } catch {
            // Keep plugin defaults when conversion fails.
        }
    }

    private static void AddStringListValuesIfPresent(object instance, string propertyName, IEnumerable<string> values) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead) {
            return;
        }

        if (property.GetValue(instance) is not IList list) {
            return;
        }

        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            list.Add(value.Trim());
        }
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

        return new ToolPackAvailabilityInfo {
            Id = normalizedDescriptorId,
            Name = name,
            Description = description,
            Tier = descriptor.Tier,
            IsDangerous = manifest?.IsDangerous ?? descriptor.IsDangerous,
            SourceKind = sourceKind,
            Enabled = enabled,
            DisabledReason = enabled ? null : disabledReason
        };
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

    private readonly record struct PackEnablementDecision(bool Enabled, string? DisabledReason);

}
