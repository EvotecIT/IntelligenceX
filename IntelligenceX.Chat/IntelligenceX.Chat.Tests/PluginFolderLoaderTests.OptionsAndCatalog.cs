using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class PluginFolderLoaderTests {
    [Fact]
    public void CreateDefaultReadOnlyPacks_PassesDistinctRunAsAndAuthProfilePathsToPluginOptions() {
        PluginFolderLoaderOptionsPack.ResetCapturedOptions();

        var tempRoot = CreatePluginTempRoot();
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

        var tempRoot = CreatePluginTempRoot();
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

        var tempRoot = CreatePluginTempRoot();
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
        var tempRoot = CreatePluginTempRoot();
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
}
