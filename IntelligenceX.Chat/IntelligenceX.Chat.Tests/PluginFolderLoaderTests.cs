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

public sealed class PluginFolderLoaderTests {
    private const string GlobalPackOptionKey = "*";

    private static readonly string[] DefaultEnabledKnownPackIds = {
        "filesystem",
        "eventlog",
        "system",
        "active_directory",
        "testimox",
        "officeimo",
        "dnsclientx",
        "domaindetective",
        "reviewer_setup",
        "email"
    };

    [Fact]
    public void CreateDefaultReadOnlyPacks_LoadsPackFromPluginFolderManifest() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
                            && !w.Contains("load_progress", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ResolvesPluginSkillIdsFromDeclaredDirectories() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DuplicateBuiltInPluginUsesFastpathSkip() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void GetPluginSearchPaths_SkipsPluginArchiveCacheRoots() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void CreateDefaultReadOnlyPacks_PassesDistinctRunAsAndAuthProfilePathsToPluginOptions() {
        PluginFolderLoaderOptionsPack.ResetCapturedOptions();

        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-options-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderOptionsPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-options-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                DisabledPackIds = DefaultEnabledKnownPackIds,
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                RunAsProfilePath = "C:/temp/run-as-profiles.json",
                AuthenticationProfilePath = "C:/temp/auth-profiles.json"
            });

            _ = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-options-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("C:/temp/run-as-profiles.json", PluginFolderLoaderOptionsPack.LastRunAsProfilePath);
            Assert.Equal("C:/temp/auth-profiles.json", PluginFolderLoaderOptionsPack.LastAuthenticationProfilePath);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
            PluginFolderLoaderOptionsPack.ResetCapturedOptions();
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_AppliesPackRuntimeOptionBag_ByPluginId() {
        PluginFolderLoaderOptionsPack.ResetCapturedOptions();

        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-options-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderOptionsPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-options-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packRuntimeOptionBag = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase) {
                ["plugin_loader_options_test"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["CustomFlag"] = true
                }
            };

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableBuiltInPackLoading = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                PackRuntimeOptionBag = packRuntimeOptionBag
            });

            _ = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-options-test", StringComparison.OrdinalIgnoreCase));
            Assert.True(PluginFolderLoaderOptionsPack.LastCustomFlag);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
            PluginFolderLoaderOptionsPack.ResetCapturedOptions();
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_PackRuntimeOptionBag_PrefersPluginIdOverGlobalAndAlias() {
        PluginFolderLoaderOptionsPack.ResetCapturedOptions();

        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-options-test");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderOptionsPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-options-test",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packRuntimeOptionBag = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase) {
                [GlobalPackOptionKey] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["RunAsProfilePath"] = "C:/temp/run-as-global-custom.json"
                },
                ["intelligencex_chat_tests"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["RunAsProfilePath"] = "C:/temp/run-as-alias.json"
                },
                ["plugin_loader_options_test"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["RunAsProfilePath"] = "C:/temp/run-as-plugin-id.json"
                }
            };

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableBuiltInPackLoading = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                RunAsProfilePath = "C:/temp/run-as-legacy-global.json",
                PackRuntimeOptionBag = packRuntimeOptionBag
            });

            _ = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-options-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("C:/temp/run-as-plugin-id.json", PluginFolderLoaderOptionsPack.LastRunAsProfilePath);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
            PluginFolderLoaderOptionsPack.ResetCapturedOptions();
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_PluginSyntheticPackFlowsIntoCatalogWithoutChatCodeEdits() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-plugin-test-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "plugin-loader-synthetic-catalog");
        Directory.CreateDirectory(pluginFolder);

        try {
            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
            File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

            var entryType = typeof(PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            var manifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "plugin-loader-synthetic-catalog",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);

            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableBuiltInPackLoading = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache")
            });

            _ = Assert.Single(
                packs,
                static p => string.Equals(p.Descriptor.Id, "plugin-loader-synthetic-catalog", StringComparison.OrdinalIgnoreCase));

            var registry = new ToolRegistry {
                RequireExplicitRoutingMetadata = true
            };
            var toolPackIdsByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            ToolPackBootstrap.RegisterAll(registry, packs, toolPackIdsByToolName);

            Assert.True(toolPackIdsByToolName.TryGetValue("plugin_loader_synthetic_probe", out var mappedPackId));
            Assert.Equal("plugin_loader_synthetic_catalog", mappedPackId);

            Assert.True(registry.TryGetDefinition("plugin_loader_synthetic_probe", out var definition));
            Assert.NotNull(definition);
            var routing = Assert.IsType<ToolRoutingContract>(definition!.Routing);
            Assert.Equal("plugin_loader_synthetic_catalog", routing.PackId, ignoreCase: true);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.Equal(ToolRoutingTaxonomy.RoleOperational, routing.Role, ignoreCase: true);

            var catalog = ToolOrchestrationCatalog.Build(registry.GetDefinitions());
            Assert.True(catalog.TryGetEntry("plugin_loader_synthetic_probe", out var entry));
            Assert.Equal("plugin_loader_synthetic_catalog", entry.PackId);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, entry.RoutingSource);
            Assert.Equal(ToolRoutingTaxonomy.RoleOperational, entry.Role);
            Assert.Equal("plugin_ops", entry.DomainIntentFamily);
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
        public sealed class PluginOptions {
            public string? RunAsProfilePath { get; set; }
            public string? AuthenticationProfilePath { get; set; }
            public bool CustomFlag { get; set; }
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
                    DomainIntentActionId = "act_domain_scope_plugin_ops"
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
