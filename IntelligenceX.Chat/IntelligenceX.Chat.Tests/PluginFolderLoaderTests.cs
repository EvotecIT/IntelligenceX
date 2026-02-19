using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
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
                EnableEmailPack = false,
                EnableReviewerSetupPack = false,
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
            Assert.DoesNotContain(warnings, static w => w.Contains("plugin-loader-test", StringComparison.OrdinalIgnoreCase));
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
                EnableFileSystemPack = false,
                EnableSystemPack = false,
                EnableActiveDirectoryPack = false,
                EnablePowerShellPack = false,
                EnableTestimoXPack = false,
                EnableEmailPack = false,
                EnableReviewerSetupPack = false
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
    public void CreateDefaultReadOnlyPacks_LoadsPackFromManifestlessFolderWithMultipleDlls() {
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
                EnableFileSystemPack = false,
                EnableSystemPack = false,
                EnableActiveDirectoryPack = false,
                EnablePowerShellPack = false,
                EnableTestimoXPack = false,
                EnableEmailPack = false,
                EnableReviewerSetupPack = false,
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            var pluginPack = Assert.Single(packs, static p => string.Equals(p.Descriptor.Id, "plugin-loader-test", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("open_source", pluginPack.Descriptor.SourceKind);
            Assert.DoesNotContain(warnings, static w => w.Contains("no IToolPack implementations found", StringComparison.OrdinalIgnoreCase));
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
                EnableFileSystemPack = false,
                EnableSystemPack = false,
                EnableActiveDirectoryPack = false,
                EnablePowerShellPack = false,
                EnableTestimoXPack = false,
                EnableEmailPack = false,
                EnableReviewerSetupPack = false,
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
        }

        public static string? LastRunAsProfilePath { get; private set; }
        public static string? LastAuthenticationProfilePath { get; private set; }

        public PluginFolderLoaderOptionsPack(PluginOptions options) {
            ArgumentNullException.ThrowIfNull(options);
            LastRunAsProfilePath = options.RunAsProfilePath;
            LastAuthenticationProfilePath = options.AuthenticationProfilePath;
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
        }

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }
}
