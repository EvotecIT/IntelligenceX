using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class PluginFolderLoaderTests {
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
                EnableFileSystemPack = false,
                EnableSystemPack = false,
                EnableActiveDirectoryPack = false,
                EnablePowerShellPack = false,
                EnableTestimoXPack = false,
                EnableReviewerSetupPack = false,
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.Contains(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(warnings, static w => w.Contains("plugin-loader-test", StringComparison.OrdinalIgnoreCase));
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
            IsDangerous = false
        };

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }
}
