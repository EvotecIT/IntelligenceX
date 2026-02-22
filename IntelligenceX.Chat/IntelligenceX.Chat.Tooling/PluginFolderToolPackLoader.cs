using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

internal static class PluginFolderToolPackLoader {
    private const string ManifestFileName = "ix-plugin.json";
    private const string PluginArchiveSuffix = ".ix-plugin.zip";
    private const int SupportedSchemaVersion = 1;
    private const int PluginArchiveCacheMaxEntries = 128;
    private static readonly TimeSpan PluginArchiveCacheMaxAge = TimeSpan.FromDays(30);

    internal static IReadOnlyList<PluginSearchRoot> ResolvePluginSearchRoots(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var roots = new List<PluginSearchRoot>();

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
        Action<string>? onWarning) {
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
        foreach (var root in roots) {
            if (!Directory.Exists(root.Path)) {
                if (root.IsExplicit) {
                    onWarning?.Invoke($"[plugin] path_not_found path='{root.Path}'");
                }
                continue;
            }

            foreach (var pluginDirectory in EnumeratePluginDirectories(root.Path, onWarning)) {
                TryLoadPluginDirectory(
                    pluginDirectory: pluginDirectory,
                    isExplicitRoot: root.IsExplicit,
                    options: options,
                    packs: packs,
                    existingPackIds: existingPackIds,
                    onWarning: onWarning);
            }
        }
    }

    private static IEnumerable<string> EnumeratePluginDirectories(string rootPath, Action<string>? onWarning) {
        if (IsPluginFolder(rootPath)) {
            yield return rootPath;
        }

        foreach (var archiveDirectory in EnumeratePluginArchiveDirectories(rootPath, onWarning)) {
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
            }
        }
    }

    private static IEnumerable<string> EnumeratePluginArchiveDirectories(string rootPath, Action<string>? onWarning) {
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
            var extracted = TryMaterializePluginArchive(archive, onWarning);
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
        if (File.Exists(manifestPath)) {
            return true;
        }

        try {
            return Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly).Any();
        } catch {
            return false;
        }
    }

    private static string? TryMaterializePluginArchive(string archivePath, Action<string>? onWarning) {
        var normalizedArchive = NormalizePath(archivePath);
        if (string.IsNullOrWhiteSpace(normalizedArchive) || !File.Exists(normalizedArchive)) {
            return null;
        }

        try {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntelligenceX.Chat",
                "plugin-cache");
            Directory.CreateDirectory(cacheRoot);
            TryTrimPluginArchiveCache(cacheRoot, onWarning);

            var cacheKey = BuildPluginArchiveCacheKey(normalizedArchive);
            if (string.IsNullOrWhiteSpace(cacheKey)) {
                return null;
            }

            var extractDir = Path.Combine(cacheRoot, cacheKey);
            if (IsPluginFolder(extractDir)) {
                return extractDir;
            }

            var tempDir = extractDir + ".tmp-" + Guid.NewGuid().ToString("N");
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(normalizedArchive, tempDir);
            if (!IsPluginFolder(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
                return null;
            }

            if (Directory.Exists(extractDir)) {
                Directory.Delete(extractDir, recursive: true);
            }

            Directory.Move(tempDir, extractDir);
            return extractDir;
        } catch (Exception ex) {
            onWarning?.Invoke(
                $"[plugin] archive_extract_failed archive='{normalizedArchive}' error='{ex.GetType().Name}: {ex.Message}'");
            return null;
        }
    }

    private static void TryTrimPluginArchiveCache(string cacheRoot, Action<string>? onWarning) {
        try {
            if (!Directory.Exists(cacheRoot)) {
                return;
            }

            var now = DateTime.UtcNow;
            var entries = Directory
                .EnumerateDirectories(cacheRoot, "zip-v1-*", SearchOption.TopDirectoryOnly)
                .Select(static path => new DirectoryInfo(path))
                .OrderByDescending(static entry => entry.LastWriteTimeUtc)
                .ToList();

            foreach (var entry in entries.ToArray()) {
                if (now - entry.LastWriteTimeUtc <= PluginArchiveCacheMaxAge) {
                    continue;
                }

                try {
                    Directory.Delete(entry.FullName, recursive: true);
                    entries.Remove(entry);
                } catch {
                    // Best effort cache trim: ignore individual delete failures.
                }
            }

            if (entries.Count <= PluginArchiveCacheMaxEntries) {
                return;
            }

            foreach (var entry in entries
                         .OrderBy(static candidate => candidate.LastWriteTimeUtc)
                         .Take(entries.Count - PluginArchiveCacheMaxEntries)) {
                try {
                    Directory.Delete(entry.FullName, recursive: true);
                } catch {
                    // Best effort cache trim: ignore individual delete failures.
                }
            }
        } catch (Exception ex) {
            onWarning?.Invoke(
                $"[plugin] cache_trim_failed cache='{cacheRoot}' error='{ex.GetType().Name}: {ex.Message}'");
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
            return "zip-v1-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        } catch {
            return string.Empty;
        }
    }

    private static void TryLoadPluginDirectory(
        string pluginDirectory,
        bool isExplicitRoot,
        ToolPackBootstrapOptions options,
        List<IToolPack> packs,
        HashSet<string> existingPackIds,
        Action<string>? onWarning) {
        PluginManifest? manifest = null;
        var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
        if (File.Exists(manifestPath)) {
            manifest = TryReadManifest(manifestPath, onWarning);
        }

        var pluginId = DeterminePluginId(pluginDirectory, manifest);
        var entryAssemblyPaths = ResolveEntryAssemblyPaths(pluginDirectory, manifest, pluginId, onWarning);
        if (entryAssemblyPaths.Count == 0) {
            return;
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
            return;
        }

        foreach (var candidateType in candidateTypes) {
            if (!TryCreatePack(candidateType, options, out var pack, out var error)) {
                onWarning?.Invoke($"[plugin] init_failed plugin='{pluginId}' type='{candidateType.FullName}' error='{error}'");
                continue;
            }

            var descriptorId = pack.Descriptor.Id?.Trim();
            var normalizedDescriptorId = ToolPackBootstrap.NormalizePackId(descriptorId);
            if (normalizedDescriptorId.Length == 0) {
                onWarning?.Invoke($"[plugin] init_failed plugin='{pluginId}' type='{candidateType.FullName}' error='descriptor id is empty'");
                continue;
            }

            if (existingPackIds.Contains(normalizedDescriptorId)) {
                if (isExplicitRoot) {
                    onWarning?.Invoke($"[plugin] duplicate_pack plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped'");
                }
                continue;
            }

            if (!IsPackEnabledByOptions(normalizedDescriptorId, options)) {
                if (isExplicitRoot) {
                    onWarning?.Invoke($"[plugin] pack_disabled plugin='{pluginId}' descriptor='{normalizedDescriptorId}' action='skipped'");
                }
                continue;
            }

            if (!TryResolvePluginSourceKind(manifest, pack, out var sourceKind, out var sourceKindError)) {
                onWarning?.Invoke($"[plugin] source_kind_missing plugin='{pluginId}' descriptor='{descriptorId}' error='{sourceKindError}'");
                continue;
            }

            pack = ToolPackBootstrap.WithSourceKind(pack, sourceKind);
            existingPackIds.Add(normalizedDescriptorId);
            packs.Add(pack);
        }
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

    private static bool IsPackEnabledByOptions(string descriptorId, ToolPackBootstrapOptions options) {
        var normalized = ToolPackBootstrap.NormalizePackId(descriptorId);
        if (normalized.Length == 0) {
            return false;
        }

        return normalized switch {
            "fs" => options.EnableFileSystemPack,
            "system" => options.EnableSystemPack,
            "ad" => options.EnableActiveDirectoryPack,
            "powershell" => options.EnablePowerShellPack,
            "testimox" => options.EnableTestimoXPack,
            "officeimo" => options.EnableOfficeImoPack,
            "reviewersetup" => options.EnableReviewerSetupPack,
            "email" => options.EnableEmailPack,
            _ => true
        };
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

    internal readonly record struct PluginSearchRoot(string Path, bool IsExplicit);

    private sealed class PluginManifest {
        public int? SchemaVersion { get; set; }
        public string? PluginId { get; set; }
        public string? DisplayName { get; set; }
        public string? PackageId { get; set; }
        public string? Version { get; set; }
        public string? SourceKind { get; set; }
        public string? Source { get; set; }
        public string? Visibility { get; set; }
        public string? EntryAssembly { get; set; }
        public string? EntryType { get; set; }
    }
}
