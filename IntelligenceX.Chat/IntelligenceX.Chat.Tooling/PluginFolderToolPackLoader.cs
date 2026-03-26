using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

internal static partial class PluginFolderToolPackLoader {
    internal const string ManifestFileName = "ix-plugin.json";
    internal const string PluginArchiveSuffix = ".ix-plugin.zip";
    private const string TestAssemblyFileName = "IntelligenceX.Chat.Tests.dll";
    private const int SupportedSchemaVersion = 1;
    private const int PluginArchiveCacheMaxEntries = 128;
    private const string PluginArchiveCacheKeyPrefix = "zip-v2-";
    private static readonly TimeSpan PluginArchiveLockTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PluginArchiveCleanupLockTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PluginArchiveCacheMaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan PluginArchiveTrimInterval = TimeSpan.FromSeconds(30);
    private static long _lastPluginArchiveTrimUtcTicks;

    internal static IReadOnlyList<PluginSearchRoot> ResolvePluginSearchRoots(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var roots = new List<PluginSearchRoot>();
        var cacheRoot = NormalizePath(ResolvePluginArchiveCacheRoot(options));

        if (options.EnableDefaultPluginPaths) {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData)) {
                roots.Add(new PluginSearchRoot(
                    Path.Combine(localAppData, "IntelligenceX.Chat", "plugins"),
                    IsExplicit: false));
            }

            roots.Add(new PluginSearchRoot(
                Path.Combine(AppContext.BaseDirectory, "plugins"),
                IsExplicit: false));
        }

        if (options.PluginPaths is not null) {
            foreach (var path in options.PluginPaths) {
                if (string.IsNullOrWhiteSpace(path)) {
                    continue;
                }

                roots.Add(new PluginSearchRoot(path.Trim(), IsExplicit: true));
            }
        }

        var normalized = new List<PluginSearchRoot>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots) {
            var fullPath = NormalizePath(root.Path);
            if (string.IsNullOrWhiteSpace(fullPath)) {
                continue;
            }

            if (IsPluginArchiveCachePath(fullPath, cacheRoot)) {
                continue;
            }

            if (!dedupe.Add(fullPath)) {
                continue;
            }

            normalized.Add(new PluginSearchRoot(fullPath, root.IsExplicit));
        }

        return normalized;
    }

    internal static void AddPluginPacks(
        List<IToolPack> packs,
        ToolPackBootstrapOptions options,
        HashSet<string> existingPackIds,
        Action<string>? onWarning,
        Action<ToolPackAvailabilityInfo>? onPackAvailability = null,
        Action<ToolPluginAvailabilityInfo>? onPluginAvailability = null,
        Action<ToolPluginCatalogInfo>? onPluginCatalog = null) {
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }
        if (existingPackIds is null) {
            throw new ArgumentNullException(nameof(existingPackIds));
        }

        var roots = ResolvePluginSearchRoots(options);
        var pendingPluginDirectories = new List<(string PluginDirectory, bool IsExplicitRoot, string PluginIdentity)>();
        foreach (var root in roots) {
            if (!Directory.Exists(root.Path)) {
                if (root.IsExplicit) {
                    onWarning?.Invoke($"[plugin] path_not_found path='{root.Path}'");
                }
                continue;
            }

            foreach (var pluginDirectory in EnumeratePluginDirectories(root.Path, options, onWarning)) {
                var pluginIdentity = ResolvePluginIdentity(pluginDirectory);
                pendingPluginDirectories.Add((pluginDirectory, root.IsExplicit, pluginIdentity));
            }
        }

        var loadedPluginIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = pendingPluginDirectories.Count;
        for (var i = 0; i < pendingPluginDirectories.Count; i++) {
            var pending = pendingPluginDirectories[i];
            if (pending.PluginIdentity.Length > 0
                && loadedPluginIdentities.TryGetValue(pending.PluginIdentity, out var existingDirectory)) {
                if (pending.IsExplicitRoot) {
                    onWarning?.Invoke(
                        $"[plugin] duplicate_plugin_identity plugin='{pending.PluginIdentity}' path='{pending.PluginDirectory}' existing='{existingDirectory}' action='skipped'");
                }
                continue;
            }

            var loadedPacksByAssemblyName = BuildLoadedPacksByAssemblyName(packs);
            var loadedAnyPack = TryLoadPluginDirectory(
                pluginDirectory: pending.PluginDirectory,
                isExplicitRoot: pending.IsExplicitRoot,
                options: options,
                packs: packs,
                existingPackIds: existingPackIds,
                loadedPacksByAssemblyName: loadedPacksByAssemblyName,
                onWarning: onWarning,
                onPackAvailability: onPackAvailability,
                onPluginAvailability: onPluginAvailability,
                onPluginCatalog: onPluginCatalog,
                loadIndex: i + 1,
                loadTotal: total);
            if (loadedAnyPack && pending.PluginIdentity.Length > 0) {
                loadedPluginIdentities[pending.PluginIdentity] = pending.PluginDirectory;
            }
        }
    }

    internal static IReadOnlyList<ToolPluginCatalogInfo> CreatePluginCatalogPreview(
        ToolPackBootstrapOptions options,
        Action<string>? onWarning = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var roots = ResolvePluginSearchRoots(options);
        var catalogs = new List<ToolPluginCatalogInfo>();
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots) {
            if (!Directory.Exists(root.Path)) {
                if (root.IsExplicit) {
                    onWarning?.Invoke($"[plugin] path_not_found path='{root.Path}'");
                }
                continue;
            }

            foreach (var pending in EnumeratePluginPreviewEntries(root.Path, onWarning, root.IsExplicit)) {
                if (pending.PluginIdentity.Length > 0 && !seenPluginIds.Add(pending.PluginIdentity)) {
                    if (pending.IsExplicitRoot) {
                        onWarning?.Invoke(
                            $"[plugin] duplicate_plugin_identity plugin='{pending.PluginIdentity}' path='{pending.Path}' action='skipped' mode='preview'");
                    }
                    continue;
                }

                var manifest = pending.IsArchive
                    ? TryReadManifestFromArchive(pending.Path, onWarning)
                    : TryReadManifest(Path.Combine(pending.Path, ManifestFileName), onWarning);
                if (manifest is null) {
                    continue;
                }

                catalogs.Add(CreatePluginCatalog(
                    pending.Path,
                    manifest,
                    Array.Empty<ToolPackAvailabilityInfo>(),
                    onWarning));
            }
        }

        return catalogs
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<ToolDefinitionDto> CreatePluginToolDefinitionPreview(
        ToolPackBootstrapOptions options,
        Action<string>? onWarning = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var tools = new List<ToolDefinitionDto>();
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ResolvePluginSearchRoots(options)) {
            if (!Directory.Exists(root.Path)) {
                if (root.IsExplicit) {
                    onWarning?.Invoke($"[plugin] path_not_found path='{root.Path}'");
                }
                continue;
            }

            foreach (var pending in EnumeratePluginPreviewEntries(root.Path, onWarning, root.IsExplicit)) {
                if (pending.PluginIdentity.Length > 0 && !seenPluginIds.Add(pending.PluginIdentity)) {
                    if (pending.IsExplicitRoot) {
                        onWarning?.Invoke(
                            $"[plugin] duplicate_plugin_identity plugin='{pending.PluginIdentity}' path='{pending.Path}' action='skipped' mode='preview'");
                    }
                    continue;
                }

                var manifest = pending.IsArchive
                    ? TryReadManifestFromArchive(pending.Path, onWarning)
                    : TryReadManifest(Path.Combine(pending.Path, ManifestFileName), onWarning);
                if (manifest is null) {
                    continue;
                }

                var pluginCatalog = CreatePluginCatalog(
                    pending.Path,
                    manifest,
                    Array.Empty<ToolPackAvailabilityInfo>(),
                    onWarning);
                tools.AddRange(CreateManifestToolDefinitions(manifest, pluginCatalog, onWarning));
            }
        }

        return tools
            .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static ToolPackBootstrapResult LoadPluginPacksForPackId(
        ToolPackBootstrapOptions options,
        string targetPackId,
        IEnumerable<IToolPack>? existingPacks,
        Action<string>? onWarning = null) {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedTargetPackId = ToolPackBootstrap.NormalizePackId(targetPackId);
        if (normalizedTargetPackId.Length == 0) {
            return new ToolPackBootstrapResult();
        }

        var loadedPacks = existingPacks?.Where(static pack => pack is not null).ToArray() ?? Array.Empty<IToolPack>();
        var existingPackIds = new HashSet<string>(
            loadedPacks
                .Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Descriptor.Id))
                .Where(static packId => packId.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var loadedPacksByAssemblyName = BuildLoadedPacksByAssemblyName(loadedPacks);
        var packs = new List<IToolPack>();
        var packAvailability = new List<ToolPackAvailabilityInfo>();
        var pluginAvailability = new List<ToolPluginAvailabilityInfo>();
        var pluginCatalog = new List<ToolPluginCatalogInfo>();
        var seenPluginIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in ResolvePluginSearchRoots(options)) {
            if (!Directory.Exists(root.Path)) {
                if (root.IsExplicit) {
                    onWarning?.Invoke($"[plugin] path_not_found path='{root.Path}'");
                }
                continue;
            }

            foreach (var pluginDirectory in EnumeratePluginDirectories(root.Path, options, onWarning)) {
                var pluginIdentity = ResolvePluginIdentity(pluginDirectory);
                if (pluginIdentity.Length > 0 && !seenPluginIdentities.Add(pluginIdentity)) {
                    continue;
                }

                var manifest = TryReadManifest(Path.Combine(pluginDirectory, ManifestFileName), onWarning);
                if (manifest is null || !ManifestCouldContainPack(manifest, pluginDirectory, normalizedTargetPackId)) {
                    continue;
                }

                var loaded = TryLoadPluginDirectory(
                    pluginDirectory: pluginDirectory,
                    isExplicitRoot: root.IsExplicit,
                    options: options,
                    packs: packs,
                    existingPackIds: existingPackIds,
                    loadedPacksByAssemblyName: loadedPacksByAssemblyName,
                    onWarning: onWarning,
                    onPackAvailability: availability => packAvailability.Add(availability),
                    onPluginAvailability: availability => pluginAvailability.Add(availability),
                    onPluginCatalog: catalog => pluginCatalog.Add(catalog),
                    loadIndex: 1,
                    loadTotal: 1);
                if (!loaded && packAvailability.Count == 0 && pluginAvailability.Count == 0 && pluginCatalog.Count == 0) {
                    continue;
                }

                return new ToolPackBootstrapResult {
                    Packs = packs.ToArray(),
                    PackAvailability = packAvailability.ToArray(),
                    PluginAvailability = pluginAvailability.ToArray(),
                    PluginCatalog = pluginCatalog.ToArray()
                };
            }
        }

        return new ToolPackBootstrapResult();
    }

    private static bool ManifestCouldContainPack(PluginManifest manifest, string pluginDirectory, string normalizedTargetPackId) {
        ArgumentNullException.ThrowIfNull(manifest);

        var declaredPackIds = ResolveManifestPackIds(manifest);
        if (declaredPackIds.Length > 0) {
            return declaredPackIds.Contains(normalizedTargetPackId, StringComparer.OrdinalIgnoreCase);
        }

        var normalizedPluginId = ToolPackBootstrap.NormalizePackId(DeterminePluginId(pluginDirectory, manifest));
        return string.Equals(normalizedPluginId, normalizedTargetPackId, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<IToolPack>> BuildLoadedPacksByAssemblyName(IReadOnlyList<IToolPack> packs) {
        var byAssemblyName = new Dictionary<string, List<IToolPack>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < packs.Count; i++) {
            var pack = packs[i];
            if (pack is null) {
                continue;
            }

            var assemblyName = (pack.GetType().Assembly.GetName().Name ?? string.Empty).Trim();
            if (assemblyName.Length == 0) {
                continue;
            }

            if (!byAssemblyName.TryGetValue(assemblyName, out var bucket)) {
                bucket = new List<IToolPack>();
                byAssemblyName[assemblyName] = bucket;
            }

            bucket.Add(pack);
        }

        var normalized = new Dictionary<string, IReadOnlyList<IToolPack>>(byAssemblyName.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in byAssemblyName) {
            normalized[pair.Key] = pair.Value;
        }

        return normalized;
    }

    private static IEnumerable<PluginPreviewEntry> EnumeratePluginPreviewEntries(
        string rootPath,
        Action<string>? onWarning,
        bool isExplicitRoot) {
        if (IsPluginFolder(rootPath)) {
            yield return new PluginPreviewEntry(
                rootPath,
                IsArchive: false,
                isExplicitRoot,
                ResolvePluginIdentity(rootPath));
        } else if (LooksLikeManifestlessPluginFolder(rootPath)) {
            onWarning?.Invoke($"[plugin] manifest_missing path='{rootPath}' action='skipped'");
        }

        foreach (var archive in EnumeratePluginPreviewArchives(rootPath)) {
            yield return new PluginPreviewEntry(
                archive,
                IsArchive: true,
                isExplicitRoot,
                ResolvePluginArchiveIdentity(archive));
        }

        IEnumerable<string> subDirectories;
        try {
            subDirectories = Directory.EnumerateDirectories(rootPath);
        } catch {
            yield break;
        }

        foreach (var directory in subDirectories.OrderBy(static d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)) {
            if (IsPluginFolder(directory)) {
                yield return new PluginPreviewEntry(
                    directory,
                    IsArchive: false,
                    isExplicitRoot,
                    ResolvePluginIdentity(directory));
            } else if (LooksLikeManifestlessPluginFolder(directory)) {
                onWarning?.Invoke($"[plugin] manifest_missing path='{directory}' action='skipped'");
            }
        }
    }

    private static IEnumerable<string> EnumeratePluginPreviewArchives(string rootPath) {
        string[] archives;
        try {
            archives = Directory
                .EnumerateFiles(rootPath, "*" + PluginArchiveSuffix, SearchOption.TopDirectoryOnly)
                .OrderBy(static file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch {
            yield break;
        }

        foreach (var archive in archives) {
            yield return archive;
        }
    }

    private static IEnumerable<string> EnumeratePluginDirectories(string rootPath, ToolPackBootstrapOptions options, Action<string>? onWarning) {
        if (IsPluginFolder(rootPath)) {
            yield return rootPath;
        } else if (LooksLikeManifestlessPluginFolder(rootPath)) {
            onWarning?.Invoke($"[plugin] manifest_missing path='{rootPath}' action='skipped'");
        }

        foreach (var archiveDirectory in EnumeratePluginArchiveDirectories(rootPath, options, onWarning)) {
            yield return archiveDirectory;
        }

        IEnumerable<string> subDirectories;
        try {
            subDirectories = Directory.EnumerateDirectories(rootPath);
        } catch {
            yield break;
        }

        foreach (var directory in subDirectories.OrderBy(static d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)) {
            if (IsPluginFolder(directory)) {
                yield return directory;
            } else if (LooksLikeManifestlessPluginFolder(directory)) {
                onWarning?.Invoke($"[plugin] manifest_missing path='{directory}' action='skipped'");
            }
        }
    }

    private static IEnumerable<string> EnumeratePluginArchiveDirectories(
        string rootPath,
        ToolPackBootstrapOptions options,
        Action<string>? onWarning) {
        string[] archives;
        try {
            archives = Directory
                .EnumerateFiles(rootPath, "*" + PluginArchiveSuffix, SearchOption.TopDirectoryOnly)
                .OrderBy(static file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch {
            yield break;
        }

        foreach (var archive in archives) {
            var extracted = TryMaterializePluginArchive(archive, options, onWarning);
            if (!string.IsNullOrWhiteSpace(extracted)) {
                yield return extracted!;
            }
        }
    }

    private static bool IsPluginFolder(string path) {
        if (!Directory.Exists(path)) {
            return false;
        }

        var manifestPath = Path.Combine(path, ManifestFileName);
        return File.Exists(manifestPath);
    }

    private static bool LooksLikeManifestlessPluginFolder(string path) {
        if (!Directory.Exists(path) || IsPluginFolder(path)) {
            return false;
        }

        try {
            return Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly).Any();
        } catch {
            return false;
        }
    }

    private static string ResolvePluginIdentity(string pluginDirectory) {
        var fallback = (Path.GetFileName(pluginDirectory) ?? string.Empty).Trim();
        if (fallback.Length == 0) {
            return string.Empty;
        }

        var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
        if (!File.Exists(manifestPath)) {
            return fallback;
        }

        try {
            using var stream = File.OpenRead(manifestPath);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Object) {
                return fallback;
            }

            var pluginId = TryReadManifestString(json.RootElement, "pluginId");
            return pluginId.Length == 0 ? fallback : pluginId;
        } catch {
            return fallback;
        }
    }

    private static string ResolvePluginArchiveIdentity(string archivePath) {
        var fallback = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath)) {
            return fallback ?? string.Empty;
        }

        try {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifestEntry = archive.Entries.FirstOrDefault(static entry =>
                string.Equals(Path.GetFileName(entry.FullName), ManifestFileName, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null) {
                return fallback ?? string.Empty;
            }

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return fallback ?? string.Empty;
            }

            var pluginId = TryReadManifestString(document.RootElement, "pluginId");
            return pluginId.Length == 0 ? (fallback ?? string.Empty) : pluginId;
        } catch {
            return fallback ?? string.Empty;
        }
    }

    private static PluginManifest? TryReadManifestFromArchive(string archivePath, Action<string>? onWarning) {
        try {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifestEntry = archive.Entries.FirstOrDefault(static entry =>
                string.Equals(Path.GetFileName(entry.FullName), ManifestFileName, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null) {
                onWarning?.Invoke($"[plugin] manifest_missing archive='{archivePath}' action='skipped'");
                return null;
            }

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            return TryDeserializeManifest(json, archivePath, onWarning);
        } catch (Exception ex) {
            onWarning?.Invoke($"[plugin] manifest_invalid path='{archivePath}' error='{ex.GetType().Name}: {ex.Message}'");
            return null;
        }
    }

    private static string TryReadManifestString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string? TryMaterializePluginArchive(string archivePath, ToolPackBootstrapOptions options, Action<string>? onWarning) {
        var normalizedArchive = NormalizePath(archivePath);
        if (string.IsNullOrWhiteSpace(normalizedArchive) || !File.Exists(normalizedArchive)) {
            return null;
        }

        FileStream? extractLock = null;
        string? tempDir = null;
        try {
            var cacheRoot = ResolvePluginArchiveCacheRoot(options);
            Directory.CreateDirectory(cacheRoot);
            TryTrimPluginArchiveCacheIfDue(cacheRoot, onWarning);

            var cacheKey = BuildPluginArchiveCacheKey(normalizedArchive);
            if (string.IsNullOrWhiteSpace(cacheKey)) {
                return null;
            }

            var extractDir = Path.Combine(cacheRoot, cacheKey);
            var extractLockPath = extractDir + ".lock";
            extractLock = TryAcquireFileLock(extractLockPath, PluginArchiveLockTimeout);
            if (extractLock is null) {
                onWarning?.Invoke(
                    $"[plugin] archive_extract_failed archive='{normalizedArchive}' error='lock timeout ({PluginArchiveLockTimeout.TotalSeconds:0}s)'");
                return null;
            }

            if (IsPluginFolder(extractDir)) {
                TouchCacheEntry(extractDir);
                return extractDir;
            }
            if (LooksLikeManifestlessPluginFolder(extractDir)) {
                onWarning?.Invoke($"[plugin] manifest_missing archive='{normalizedArchive}' action='skipped'");
                return null;
            }

            tempDir = extractDir + ".tmp-" + Guid.NewGuid().ToString("N");
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }

            TryExtractArchiveSafely(normalizedArchive, tempDir);
            if (!IsPluginFolder(tempDir)) {
                if (LooksLikeManifestlessPluginFolder(tempDir)) {
                    onWarning?.Invoke($"[plugin] manifest_missing archive='{normalizedArchive}' action='skipped'");
                }
                Directory.Delete(tempDir, recursive: true);
                tempDir = null;
                return null;
            }

            if (Directory.Exists(extractDir)) {
                Directory.Delete(extractDir, recursive: true);
            }

            Directory.Move(tempDir, extractDir);
            tempDir = null;
            TouchCacheEntry(extractDir);
            return extractDir;
        } catch (Exception ex) {
            onWarning?.Invoke(
                $"[plugin] archive_extract_failed archive='{normalizedArchive}' error='{ex.GetType().Name}: {ex.Message}'");
            return null;
        } finally {
            extractLock?.Dispose();
            if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir)) {
                try {
                    Directory.Delete(tempDir, recursive: true);
                } catch {
                    // Best effort cleanup for partially materialized temporary archives.
                }
            }
        }
    }

    private static string ResolvePluginArchiveCacheRoot(ToolPackBootstrapOptions options) {
        var configured = NormalizePath(options.PluginArchiveCacheRoot);
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured!;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "IntelligenceX.Chat", "plugin-cache");
    }

    private static void TryTrimPluginArchiveCacheIfDue(string cacheRoot, Action<string>? onWarning) {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastPluginArchiveTrimUtcTicks);
        if (lastTicks > 0 && nowTicks - lastTicks < PluginArchiveTrimInterval.Ticks) {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastPluginArchiveTrimUtcTicks, nowTicks, lastTicks) != lastTicks) {
            return;
        }

        TryTrimPluginArchiveCache(cacheRoot, onWarning);
    }

    private static void TryTrimPluginArchiveCache(string cacheRoot, Action<string>? onWarning) {
        try {
            if (!Directory.Exists(cacheRoot)) {
                return;
            }

            var cleanupLockPath = Path.Combine(cacheRoot, ".cleanup.lock");
            using var cleanupLock = TryAcquireFileLock(cleanupLockPath, PluginArchiveCleanupLockTimeout);
            if (cleanupLock is null) {
                return;
            }

            var now = DateTime.UtcNow;
            var entries = Directory
                .EnumerateDirectories(cacheRoot, "zip-v*", SearchOption.TopDirectoryOnly)
                .Select(static path => new DirectoryInfo(path))
                .OrderByDescending(static entry => entry.LastWriteTimeUtc)
                .ToList();

            foreach (var entry in entries.ToArray()) {
                if (!entry.Name.StartsWith(PluginArchiveCacheKeyPrefix, StringComparison.OrdinalIgnoreCase)
                    && TryDeleteCacheEntry(entry)) {
                    entries.Remove(entry);
                }
            }

            foreach (var entry in entries.ToArray()) {
                if (!IsLikelyTestCacheEntry(entry)) {
                    continue;
                }

                if (TryDeleteCacheEntry(entry)) {
                    entries.Remove(entry);
                }
            }

            foreach (var entry in entries.ToArray()) {
                if (now - entry.LastWriteTimeUtc <= PluginArchiveCacheMaxAge) {
                    continue;
                }

                if (TryDeleteCacheEntry(entry)) {
                    entries.Remove(entry);
                }
            }

            if (entries.Count <= PluginArchiveCacheMaxEntries) {
                return;
            }

            foreach (var entry in entries
                         .OrderBy(static candidate => candidate.LastWriteTimeUtc)
                         .Take(entries.Count - PluginArchiveCacheMaxEntries)) {
                _ = TryDeleteCacheEntry(entry);
            }
        } catch (Exception ex) {
            onWarning?.Invoke(
                $"[plugin] cache_trim_failed cache='{cacheRoot}' error='{ex.GetType().Name}: {ex.Message}'");
        }
    }

    private static bool IsLikelyTestCacheEntry(DirectoryInfo entry) {
        try {
            if (!entry.Exists) {
                return false;
            }

            return File.Exists(Path.Combine(entry.FullName, TestAssemblyFileName));
        } catch {
            return false;
        }
    }

    private static bool TryDeleteCacheEntry(DirectoryInfo entry) {
        var lockPath = entry.FullName + ".lock";
        using var entryLock = TryAcquireFileLock(lockPath, TimeSpan.Zero);
        if (entryLock is null) {
            return false;
        }

        try {
            if (entry.Exists) {
                Directory.Delete(entry.FullName, recursive: true);
            }
            return true;
        } catch {
            return false;
        }
    }

    private static FileStream? TryAcquireFileLock(string lockPath, TimeSpan timeout) {
        var deadlineUtc = DateTime.UtcNow + timeout;
        while (true) {
            try {
                var lockDirectory = Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrWhiteSpace(lockDirectory)) {
                    Directory.CreateDirectory(lockDirectory);
                }

                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) {
                if (DateTime.UtcNow >= deadlineUtc) {
                    return null;
                }
                Thread.Sleep(35);
            } catch (UnauthorizedAccessException) {
                if (DateTime.UtcNow >= deadlineUtc) {
                    return null;
                }
                Thread.Sleep(35);
            }
        }
    }

    private static void TouchCacheEntry(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
        } catch {
            // Best effort touch to keep active cache entries warm.
        }
    }

    private static void TryExtractArchiveSafely(string archivePath, string destinationDirectory) {
        var rootPath = Path.GetFullPath(destinationDirectory);
        if (!rootPath.EndsWith(Path.DirectorySeparatorChar)) {
            rootPath += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries) {
            var normalizedEntry = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var candidatePath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedEntry));
            if (!candidatePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"archive entry escapes extraction root: '{entry.FullName}'");
            }

            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(candidatePath);
                continue;
            }

            var candidateDirectory = Path.GetDirectoryName(candidatePath);
            if (!string.IsNullOrWhiteSpace(candidateDirectory)) {
                Directory.CreateDirectory(candidateDirectory);
            }

            entry.ExtractToFile(candidatePath, overwrite: true);
        }
    }

    private static string BuildPluginArchiveCacheKey(string archivePath) {
        try {
            var info = new FileInfo(archivePath);
            var stamp = archivePath.ToUpperInvariant()
                        + "|"
                        + info.Length.ToString()
                        + "|"
                        + info.LastWriteTimeUtc.Ticks.ToString();
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(stamp));
            return PluginArchiveCacheKeyPrefix + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        } catch {
            return string.Empty;
        }
    }

    private static bool IsPluginArchiveCachePath(string candidatePath, string? cacheRoot) {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(cacheRoot)) {
            return false;
        }

        var normalizedCandidate = candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCacheRoot = cacheRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedCandidate, normalizedCacheRoot, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var cacheRootPrefix = normalizedCacheRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(cacheRootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal readonly record struct PluginSearchRoot(string Path, bool IsExplicit);

    private sealed class PluginManifest {
        public int? SchemaVersion { get; set; }
        public string? PluginId { get; set; }
        public string? DisplayName { get; set; }
        public string? Version { get; set; }
        public string[]? PackIds { get; set; }
        public PluginManifestToolDescriptor[]? Tools { get; set; }
        public bool? DefaultEnabled { get; set; }
        public bool? IsDangerous { get; set; }
        public string? SourceKind { get; set; }
        public string? Source { get; set; }
        public string? Visibility { get; set; }
        public string? EntryAssembly { get; set; }
        public string? EntryType { get; set; }
        public string[]? SkillDirectories { get; set; }
    }

    private sealed class PluginManifestToolDescriptor {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? DisplayName { get; set; }
        public string? PackId { get; set; }
        public string? Category { get; set; }
        public string[]? Tags { get; set; }
        public string? RoutingRole { get; set; }
        public string? ExecutionScope { get; set; }
        public bool? SupportsLocalExecution { get; set; }
        public bool? SupportsRemoteExecution { get; set; }
        public bool? SupportsTargetScoping { get; set; }
        public bool? SupportsRemoteHostTargeting { get; set; }
        public bool? IsWriteCapable { get; set; }
        public bool? SupportsConnectivityProbe { get; set; }
        public string? ProbeToolName { get; set; }
        public bool? IsSetupAware { get; set; }
        public string? SetupToolName { get; set; }
        public string[]? HandoffTargetPackIds { get; set; }
        public string[]? HandoffTargetToolNames { get; set; }
        public bool? IsRecoveryAware { get; set; }
        public string[]? RecoveryToolNames { get; set; }
        public string[]? RepresentativeExamples { get; set; }
    }

    private readonly record struct PluginPreviewEntry(string Path, bool IsArchive, bool IsExplicitRoot, string PluginIdentity);
}
