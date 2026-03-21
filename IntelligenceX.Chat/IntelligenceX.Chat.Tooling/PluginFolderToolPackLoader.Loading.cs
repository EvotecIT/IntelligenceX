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
        Action<ToolPluginAvailabilityInfo>? onPluginAvailability,
        Action<ToolPluginCatalogInfo>? onPluginCatalog,
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
        var pluginPackAvailability = new List<ToolPackAvailabilityInfo>();

        try {
            if (manifest is null) {
                failedPackCount++;
                return false;
            }

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
                return false;
            }
            candidateTypeCount = candidateTypes.Count;

            foreach (var candidateType in candidateTypes) {
                if (!TryCreatePack(candidateType, pluginId, options, out var pack, out var error)) {
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
                    var duplicateAvailability = CreatePluginPackAvailability(
                        pack: pack,
                        normalizedDescriptorId: normalizedDescriptorId,
                        manifest: manifest,
                        enabled: false,
                        disabledReason: DisabledByDuplicatePackReason);
                    onPackAvailability?.Invoke(duplicateAvailability);
                    pluginPackAvailability.Add(duplicateAvailability);
                    continue;
                }

                var enablementDecision = ResolvePackEnablement(normalizedDescriptorId, manifest, options);
                if (!enablementDecision.Enabled) {
                    if (isExplicitRoot) {
                        onWarning?.Invoke($"[plugin] pack_disabled plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped'");
                    }
                    disabledPackCount++;
                    var disabledAvailability = CreatePluginPackAvailability(
                        pack: pack,
                        normalizedDescriptorId: normalizedDescriptorId,
                        manifest: manifest,
                        enabled: false,
                        disabledReason: enablementDecision.DisabledReason);
                    onPackAvailability?.Invoke(disabledAvailability);
                    pluginPackAvailability.Add(disabledAvailability);
                    continue;
                }

                if (!TryResolvePluginSourceKind(manifest, pack, out var sourceKind, out var sourceKindError)) {
                    onWarning?.Invoke($"[plugin] source_kind_missing plugin='{pluginId}' descriptor='{descriptorId}' error='{sourceKindError}'");
                    failedPackCount++;
                    var invalidAvailability = CreatePluginPackAvailability(
                        pack: pack,
                        normalizedDescriptorId: normalizedDescriptorId,
                        manifest: manifest,
                        enabled: false,
                        disabledReason: sourceKindError);
                    onPackAvailability?.Invoke(invalidAvailability);
                    pluginPackAvailability.Add(invalidAvailability);
                    continue;
                }

                pack = ToolPackBootstrap.WithSourceKind(pack, sourceKind);
                existingPackIds.Add(normalizedDescriptorId);
                packs.Add(pack);
                loadedPackCount++;
                pluginPackAvailability.Add(CreatePluginPackAvailability(
                    pack: pack,
                    normalizedDescriptorId: normalizedDescriptorId,
                    manifest: manifest,
                    enabled: true,
                    disabledReason: null));
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

        if (manifest is not null || pluginPackAvailability.Count > 0) {
            var pluginCatalog = CreatePluginCatalog(pluginDirectory, manifest, pluginPackAvailability, onWarning);
            onPluginCatalog?.Invoke(pluginCatalog);
            if (pluginPackAvailability.Count > 0) {
                onPluginAvailability?.Invoke(CreatePluginAvailability(pluginCatalog, pluginPackAvailability));
            }
        }

        return loadedPackCount > 0;
    }

    private static bool TryShortCircuitDuplicatePluginFromLoadedPackAssemblyMap(
        IReadOnlyList<string> entryAssemblyPaths,
        IReadOnlyDictionary<string, IReadOnlyList<IToolPack>> loadedPacksByAssemblyName,
        PluginManifest manifest,
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
        PluginManifest manifest,
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
            AssemblyName requestedAssemblyName;
            try {
                requestedAssemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            } catch {
                continue;
            }

            var loadedAssembly = FindReusableLoadedAssembly(assemblyPath, requestedAssemblyName);
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
                if (!TryCreatePack(candidateType, pluginId, options, out var pack, out _)) {
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

}
