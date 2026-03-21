using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using SystemPackType = IntelligenceX.Tools.System.SystemToolPack;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class PluginFolderLoaderTests {
    private const string GlobalPackOptionKey = "*";

    private static readonly string[] DefaultEnabledKnownPackIds = {
        "filesystem",
        "eventlog",
        "system",
        "active_directory",
        "testimox",
        "testimox_analytics",
        "officeimo",
        "dnsclientx",
        "domaindetective",
        "reviewer_setup",
        "email"
    };

    private static string CreatePluginTempRoot() {
        return TempPathTestHelper.CreateTempDirectoryPath("ix-chat-plugin-test");
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_LoadsPackFromPluginFolderManifest() {
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

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{entryAssemblyName}}",
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

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
            Assert.DoesNotContain(
                warnings,
                static w => w.Contains("plugin-loader-test", StringComparison.OrdinalIgnoreCase)
                            && !w.Contains("load_progress", StringComparison.OrdinalIgnoreCase)
                            && !w.Contains("load_timing", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ResolvesPluginSkillIdsFromDeclaredDirectories() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-test");
        var skillRoot = Path.Combine(pluginFolder, "skills");
        Directory.CreateDirectory(Path.Combine(skillRoot, "inventory-test"));
        Directory.CreateDirectory(Path.Combine(skillRoot, "network-recon"));

        try {
            File.WriteAllText(Path.Combine(skillRoot, "inventory-test", "SKILL.md"), "# Inventory test");
            File.WriteAllText(Path.Combine(skillRoot, "network-recon", "SKILL.md"), "# Network recon");

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
              "entryType": "{{entryType}}",
              "skillDirectories": [ "skills" ]
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId("plugin-loader-test");
            var pluginAvailability = Assert.Single(
                result.PluginAvailability,
                plugin => string.Equals(plugin.Id, normalizedPluginId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.Combine(pluginFolder, "skills"), Assert.Single(pluginAvailability.SkillDirectories));
            Assert.Equal(
                new[] { "inventory-test", "network-recon" },
                pluginAvailability.SkillIds);
            var pluginCatalog = Assert.Single(
                result.PluginCatalog,
                plugin => string.Equals(plugin.Id, normalizedPluginId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.Combine(pluginFolder, "skills"), Assert.Single(pluginCatalog.SkillDirectories));
            Assert.Equal(
                new[] { "inventory-test", "network-recon" },
                pluginCatalog.SkillIds);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DuplicateBuiltInPluginUsesFastpathSkip() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "duplicate-system-plugin");
        Directory.CreateDirectory(pluginFolder);

        try {
            var builtInAssemblyPath = typeof(SystemPackType).Assembly.Location;
            var entryAssemblyName = Path.GetFileName(builtInAssemblyPath);
            Assert.False(string.IsNullOrWhiteSpace(entryAssemblyName));
            File.Copy(builtInAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);

            var entryType = typeof(SystemPackType).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));
            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "duplicate-system-plugin",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var warnings = new List<string>();
            var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.Contains(
                warnings,
                static warning => warning.Contains("[plugin] duplicate_pack plugin='duplicate-system-plugin'", StringComparison.OrdinalIgnoreCase)
                                  && (warning.Contains("mode='assembly_map'", StringComparison.OrdinalIgnoreCase)
                                      || warning.Contains("mode='fastpath'", StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(
                warnings,
                static warning => warning.Contains("[plugin] load_timing plugin='duplicate-system-plugin'", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, result.Packs.Count(static pack => string.Equals(pack.Descriptor.Id, "system", StringComparison.OrdinalIgnoreCase)));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DeduplicatesSamePluginIdentityAcrossSearchRoots() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRootA = Path.Combine(tempRoot, "plugins-a");
        var pluginRootB = Path.Combine(tempRoot, "plugins-b");
        var pluginFolderA = Path.Combine(pluginRootA, "plugin-loader-test-a");
        var pluginFolderB = Path.Combine(pluginRootB, "plugin-loader-test-b");
        Directory.CreateDirectory(pluginFolderA);
        Directory.CreateDirectory(pluginFolderB);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            Assert.False(string.IsNullOrWhiteSpace(entryAssemblyName));
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolderA, entryAssemblyName), overwrite: true);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolderB, entryAssemblyName), overwrite: true);

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
            File.WriteAllText(Path.Combine(pluginFolderA, "ix-plugin.json"), manifest);
            File.WriteAllText(Path.Combine(pluginFolderB, "ix-plugin.json"), manifest);

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRootA, pluginRootB },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            _ = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                warnings,
                static warning => warning.Contains("[plugin] duplicate_plugin_identity plugin='plugin-loader-test'", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                1,
                warnings.Count(static warning =>
                    warning.Contains("[plugin] load_progress plugin='plugin-loader-test' phase='begin'", StringComparison.OrdinalIgnoreCase)));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DuplicateIdentityFallsBackToLaterCandidateWhenFirstFails() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRootA = Path.Combine(tempRoot, "plugins-a");
        var pluginRootB = Path.Combine(tempRoot, "plugins-b");
        var pluginFolderA = Path.Combine(pluginRootA, "plugin-loader-test-a");
        var pluginFolderB = Path.Combine(pluginRootB, "plugin-loader-test-b");
        Directory.CreateDirectory(pluginFolderA);
        Directory.CreateDirectory(pluginFolderB);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            Assert.False(string.IsNullOrWhiteSpace(entryAssemblyName));
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolderB, entryAssemblyName), overwrite: true);

            var entryType = typeof(PluginFolderLoaderTestPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));
            var brokenManifest = """
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "missing-entry.dll",
              "entryType": "IntelligenceX.Chat.Tests.PluginFolderLoaderTests+PluginFolderLoaderTestPack"
            }
            """;
            var validManifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolderA, "ix-plugin.json"), brokenManifest);
            File.WriteAllText(Path.Combine(pluginFolderB, "ix-plugin.json"), validManifest);

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRootA, pluginRootB },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            _ = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                warnings,
                static warning => warning.Contains("[plugin] entry_not_found plugin='plugin-loader-test' entryAssembly='missing-entry.dll'", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                2,
                warnings.Count(static warning =>
                    warning.Contains("[plugin] load_progress plugin='plugin-loader-test' phase='begin'", StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(
                warnings,
                static warning => warning.Contains("[plugin] duplicate_plugin_identity plugin='plugin-loader-test'", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsManifestDefaultDisabledPluginPack() {
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

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "defaultEnabled": false,
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            Assert.DoesNotContain(result.Packs,
                static pack => string.Equals(pack.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId("plugin-loader-test");
            var pluginAvailability = Assert.Single(result.PackAvailability, pack =>
                string.Equals(pack.Id, normalizedPluginId, StringComparison.OrdinalIgnoreCase));
            Assert.False(pluginAvailability.Enabled);
            Assert.Equal("Disabled by plugin manifest default.", pluginAvailability.DisabledReason);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_ManifestDefaultDisabledPackCanBeEnabledByPackId() {
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

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "defaultEnabled": false,
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                EnabledPackIds = new[] { "plugin-loader-test" },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsDangerousPluginAsDisabledByDefault() {
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

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "isDangerous": true,
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            Assert.DoesNotContain(result.Packs,
                static pack => string.Equals(pack.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId("plugin-loader-test");
            var pluginAvailability = Assert.Single(result.PackAvailability, pack =>
                string.Equals(pack.Id, normalizedPluginId, StringComparison.OrdinalIgnoreCase));
            Assert.False(pluginAvailability.Enabled);
            Assert.Equal("Disabled by plugin risk classification (dangerous plugin).", pluginAvailability.DisabledReason);
            Assert.True(pluginAvailability.IsDangerous);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DangerousPluginCanBeEnabledByPackId() {
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

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "isDangerous": true,
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                EnabledPackIds = new[] { "plugin-loader-test" },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_PrefersDescriptorSourceKindOverManifestSourceKind() {
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

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "sourceKind": "private"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_SkipsManifestlessFolderWithMultipleDlls() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            // Add a non-plugin assembly that sorts before the actual plugin assembly.
            var dependencySource = typeof(JsonDocument).Assembly.Location;
            var dependencyTarget = Path.Combine(pluginFolder, "AAA.Dependency.dll");
            File.Copy(dependencySource, dependencyTarget, overwrite: true);

            // Copy the test assembly that contains PluginFolderLoaderTestPack.
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var copiedAssemblyPath = Path.Combine(pluginFolder, Path.GetFileName(sourceAssemblyPath));
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

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
                static warning => warning.Contains("manifest_missing", StringComparison.OrdinalIgnoreCase)
                                  && warning.Contains("action='skipped'", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_SkipsPlugin_WhenManifestMissingRequiredPluginId() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);

            var entryType = typeof(PluginFolderLoaderTestPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "entryAssembly": "{{entryAssemblyName}}",
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
                                  && warning.Contains("missing required pluginId", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_SkipsPlugin_WhenManifestMissingRequiredEntryType() {
        var tempRoot = CreatePluginTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-test",
              "entryAssembly": "{{entryAssemblyName}}"
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
                                  && warning.Contains("missing required entryType", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }



    public sealed class PluginFolderLoaderTestPack : IToolPack {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "plugin-loader-test",
            Name = "Plugin Loader Test",
            Tier = ToolCapabilityTier.ReadOnly,
            IsDangerous = false,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }

    public sealed class PluginFolderLoaderOptionsPack : IToolPack {
        public sealed class PluginOptions : IToolPackRuntimeConfigurable {
            public string? RunAsProfilePath { get; set; }
            public string? AuthenticationProfilePath { get; set; }
            public bool CustomFlag { get; set; }

            public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
                ArgumentNullException.ThrowIfNull(context);

                RunAsProfilePath = string.IsNullOrWhiteSpace(context.RunAsProfilePath)
                    ? RunAsProfilePath
                    : context.RunAsProfilePath.Trim();
                AuthenticationProfilePath = string.IsNullOrWhiteSpace(context.AuthenticationProfilePath)
                    ? AuthenticationProfilePath
                    : context.AuthenticationProfilePath.Trim();
            }
        }

        public static string? LastRunAsProfilePath { get; private set; }
        public static string? LastAuthenticationProfilePath { get; private set; }
        public static bool LastCustomFlag { get; private set; }

        public PluginFolderLoaderOptionsPack(PluginOptions options) {
            ArgumentNullException.ThrowIfNull(options);
            LastRunAsProfilePath = options.RunAsProfilePath;
            LastAuthenticationProfilePath = options.AuthenticationProfilePath;
            LastCustomFlag = options.CustomFlag;
        }

        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "plugin-loader-options-test",
            Name = "Plugin Loader Options Test",
            Tier = ToolCapabilityTier.ReadOnly,
            IsDangerous = false,
            SourceKind = "open_source"
        };

        public static void ResetCapturedOptions() {
            LastRunAsProfilePath = null;
            LastAuthenticationProfilePath = null;
            LastCustomFlag = false;
        }

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }

    public sealed class PluginFolderLoaderSyntheticCatalogPack : IToolPack {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "plugin-loader-synthetic-catalog",
            Name = "Plugin Loader Synthetic Catalog",
            Tier = ToolCapabilityTier.ReadOnly,
            IsDangerous = false,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
            registry.Register(new PluginFolderLoaderSyntheticCatalogTool(ToolPackBootstrap.NormalizePackId(Descriptor.Id)));
        }
    }

    private sealed class PluginFolderLoaderSyntheticCatalogTool : ITool {
        public PluginFolderLoaderSyntheticCatalogTool(string packId) {
            Definition = new ToolDefinition(
                name: "plugin_loader_synthetic_probe",
                description: "Synthetic plugin-discovered tool used for catalog integration coverage.",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject().Add("target", new JsonObject().Add("type", "string")))
                    .Add("additionalProperties", false),
                tags: new[] {
                    "pack:plugin_loader_synthetic_catalog",
                    "domain_family:plugin_ops"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = packId,
                    Role = ToolRoutingTaxonomy.RoleOperational,
                    DomainIntentFamily = "plugin_ops",
                    DomainIntentActionId = "act_domain_scope_plugin_ops",
                    DomainIntentFamilyDisplayName = "Plugin operations",
                    DomainIntentFamilyReplyExample = "plugin operations",
                    DomainIntentFamilyChoiceDescription = "Plugin operations scope (plugin-owned runtime diagnostics)"
                });
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            _ = arguments;
            _ = cancellationToken;
            return Task.FromResult("{}");
        }
    }
}
