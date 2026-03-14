using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class PluginFolderLoaderTests {
    [Fact]
    public void GetPluginSearchPaths_SkipsPluginArchiveCacheRoots() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var cacheRoot = Path.Combine(tempRoot, "plugin-cache");
        var cacheChild = Path.Combine(cacheRoot, "zip-v1-legacy");

        var paths = ToolPackBootstrap.GetPluginSearchPaths(new ToolPackBootstrapOptions {
            EnableDefaultPluginPaths = false,
            PluginPaths = new[] { pluginRoot, cacheRoot, cacheChild },
            PluginArchiveCacheRoot = cacheRoot
        });

        Assert.Single(paths);
        Assert.Equal(Path.GetFullPath(pluginRoot), paths[0], ignoreCase: true);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_SkipsPlugin_WhenManifestEntryAssemblyIsAbsolutePath() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderTestPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));
            var escapedAbsolutePath = copiedAssemblyPath.Replace("\\", "\\\\", StringComparison.Ordinal);

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{escapedAbsolutePath}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.Empty(packs);
            Assert.Contains(
                warnings,
                static warning => warning.Contains("manifest_invalid", StringComparison.OrdinalIgnoreCase)
                                  && warning.Contains("entryAssembly must be relative to plugin root", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_LoadsPackFromPluginArchive() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(tempRoot, "plugin-loader-archive-source");
        Directory.CreateDirectory(pluginFolder);
        Directory.CreateDirectory(pluginRoot);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderTestPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var archivePath = Path.Combine(pluginRoot, "plugin-loader-test.ix-plugin.zip");
            ZipFile.CreateFromDirectory(pluginFolder, archivePath);
            Directory.Delete(pluginFolder, recursive: true);

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
            Assert.DoesNotContain(
                warnings,
                static w => w.Contains("plugin-loader-test", StringComparison.OrdinalIgnoreCase)
                            && !w.Contains("load_progress", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_InvalidPluginArchiveReportsWarningAndSkipsPack() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        Directory.CreateDirectory(pluginRoot);

        try {
            var archivePath = Path.Combine(pluginRoot, "plugin-loader-test.ix-plugin.zip");
            File.WriteAllText(archivePath, "not-a-valid-zip");

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.DoesNotContain(packs,
                static pack => string.Equals(pack.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(warnings,
                static warning => warning.Contains("archive_extract_failed", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_PathTraversalArchiveReportsWarningAndSkipsPack() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        Directory.CreateDirectory(pluginRoot);

        try {
            var archivePath = Path.Combine(pluginRoot, "plugin-loader-test.ix-plugin.zip");
            using (var fs = File.Create(archivePath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false)) {
                var traversal = archive.CreateEntry("../outside.txt");
                using var writer = new StreamWriter(traversal.Open());
                writer.Write("escape");
            }

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.DoesNotContain(packs,
                static pack => string.Equals(pack.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(warnings,
                static warning => warning.Contains("archive_extract_failed", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateDefaultReadOnlyPacks_ConcurrentArchiveLoadsRemainStable() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(tempRoot, "plugin-loader-archive-source");
        Directory.CreateDirectory(pluginFolder);
        Directory.CreateDirectory(pluginRoot);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderTestPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var archivePath = Path.Combine(pluginRoot, "plugin-loader-test.ix-plugin.zip");
            ZipFile.CreateFromDirectory(pluginFolder, archivePath);
            Directory.Delete(pluginFolder, recursive: true);

            var warnings = new ConcurrentBag<string>();
            var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
                var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                    EnableDefaultPluginPaths = false,
                    PluginPaths = new[] { pluginRoot },
                    DisabledPackIds = DefaultEnabledKnownPackIds,
                    PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                    OnBootstrapWarning = warning => warnings.Add(warning)
                });

                return packs.Any(pack =>
                    string.Equals(pack.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            })).ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.All(results, Assert.True);
            Assert.DoesNotContain(warnings,
                static warning => warning.Contains("archive_extract_failed", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
