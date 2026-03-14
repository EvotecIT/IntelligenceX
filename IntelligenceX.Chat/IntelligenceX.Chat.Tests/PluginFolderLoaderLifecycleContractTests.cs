using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class PluginFolderLoaderLifecycleContractTests {
    private static string CreatePluginLifecycleTempRoot() {
        return TempPathTestHelper.CreateTempDirectoryPath("ix-chat-plugin-lifecycle-test");
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_PluginLoadProgressIsDeterministicByFolderOrder() {
        var tempRoot = CreatePluginLifecycleTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        Directory.CreateDirectory(pluginRoot);

        try {
            CreatePluginFolder(
                pluginRoot,
                folderName: "01-alpha",
                manifest: BuildPluginManifest("alpha-plugin", typeof(AlphaLifecyclePluginPack)));
            CreatePluginFolder(
                pluginRoot,
                folderName: "02-zeta",
                manifest: BuildPluginManifest("zeta-plugin", typeof(ZetaLifecyclePluginPack)));

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableBuiltInPackLoading = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.Contains(
                packs,
                static pack => string.Equals(pack.Descriptor.Id, "alpha-lifecycle-pack", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                packs,
                static pack => string.Equals(pack.Descriptor.Id, "zeta-lifecycle-pack", StringComparison.OrdinalIgnoreCase));

            var beginWarnings = warnings
                .Where(static warning =>
                    warning.StartsWith("[plugin] load_progress", StringComparison.OrdinalIgnoreCase)
                    && warning.Contains("phase='begin'", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.Equal(2, beginWarnings.Length);
            Assert.Contains("plugin='alpha-plugin'", beginWarnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("index='1'", beginWarnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("plugin='zeta-plugin'", beginWarnings[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("index='2'", beginWarnings[1], StringComparison.OrdinalIgnoreCase);

            var endWarnings = warnings
                .Where(static warning =>
                    warning.StartsWith("[plugin] load_progress", StringComparison.OrdinalIgnoreCase)
                    && warning.Contains("phase='end'", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.Equal(2, endWarnings.Length);
            Assert.Contains("plugin='alpha-plugin'", endWarnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("loaded='1'", endWarnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("failed='0'", endWarnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("plugin='zeta-plugin'", endWarnings[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("loaded='1'", endWarnings[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("failed='0'", endWarnings[1], StringComparison.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_PluginOptionsReceiveHealthProbeContractSettings() {
        ProbeLifecyclePluginPack.ResetCapturedOptions();

        var tempRoot = CreatePluginLifecycleTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        Directory.CreateDirectory(pluginRoot);

        try {
            CreatePluginFolder(
                pluginRoot,
                folderName: "probe-contract",
                manifest: BuildPluginManifest("probe-contract-plugin", typeof(ProbeLifecyclePluginPack)));

            var probeStore = new InMemoryToolAuthenticationProbeStore();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableBuiltInPackLoading = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                AuthenticationProbeStore = probeStore,
                RequireSuccessfulSmtpProbeForSend = true,
                SmtpProbeMaxAgeSeconds = 321
            });

            _ = Assert.Single(
                packs,
                static pack => string.Equals(pack.Descriptor.Id, "probe-lifecycle-pack", StringComparison.OrdinalIgnoreCase));
            Assert.Same(probeStore, ProbeLifecyclePluginPack.LastAuthenticationProbeStore);
            Assert.True(ProbeLifecyclePluginPack.LastRequireSuccessfulSmtpProbeForSend);
            Assert.Equal(321, ProbeLifecyclePluginPack.LastSmtpProbeMaxAgeSeconds);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
            ProbeLifecyclePluginPack.ResetCapturedOptions();
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_InvalidManifestEmitsFailureTelemetry() {
        var tempRoot = CreatePluginLifecycleTempRoot();
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        Directory.CreateDirectory(pluginRoot);

        try {
            var entryAssemblyName = CopyTestAssemblyToPluginFolder(Path.Combine(pluginRoot, "invalid-plugin"));
            var invalidManifest = $$"""
            {
              "schemaVersion": 1,
              "pluginId": "invalid-plugin",
              "entryAssembly": "{{entryAssemblyName}}"
            }
            """;
            File.WriteAllText(Path.Combine(pluginRoot, "invalid-plugin", "ix-plugin.json"), invalidManifest);

            var warnings = new List<string>();
            var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
                EnableBuiltInPackLoading = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { pluginRoot },
                PluginArchiveCacheRoot = Path.Combine(tempRoot, "plugin-cache"),
                OnBootstrapWarning = warning => warnings.Add(warning)
            });

            Assert.Empty(packs);
            Assert.Contains(
                warnings,
                static warning => warning.Contains("[plugin] manifest_invalid", StringComparison.OrdinalIgnoreCase)
                                  && warning.Contains("missing required entryType", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                warnings,
                static warning => warning.Contains("[plugin] load_progress plugin='invalid-plugin' phase='end'", StringComparison.OrdinalIgnoreCase)
                                  && warning.Contains("failed='1'", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void CreatePluginFolder(string pluginRoot, string folderName, string manifest) {
        var pluginFolder = Path.Combine(pluginRoot, folderName);
        _ = CopyTestAssemblyToPluginFolder(pluginFolder);
        File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), manifest);
    }

    private static string CopyTestAssemblyToPluginFolder(string pluginFolder) {
        Directory.CreateDirectory(pluginFolder);
        var sourceAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
        var copiedAssemblyPath = Path.Combine(pluginFolder, entryAssemblyName);
        File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);
        return entryAssemblyName;
    }

    private static string BuildPluginManifest(string pluginId, Type entryType) {
        var entryTypeName = entryType.FullName;
        Assert.False(string.IsNullOrWhiteSpace(entryTypeName));

        var entryAssemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        return $$"""
        {
          "schemaVersion": 1,
          "pluginId": "{{pluginId}}",
          "entryAssembly": "{{entryAssemblyName}}",
          "entryType": "{{entryTypeName}}"
        }
        """;
    }

    public sealed class AlphaLifecyclePluginPack : IToolPack {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "alpha-lifecycle-pack",
            Name = "Alpha Lifecycle Pack",
            Tier = ToolCapabilityTier.ReadOnly,
            IsDangerous = false,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }

    public sealed class ZetaLifecyclePluginPack : IToolPack {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "zeta-lifecycle-pack",
            Name = "Zeta Lifecycle Pack",
            Tier = ToolCapabilityTier.ReadOnly,
            IsDangerous = false,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }

    public sealed class ProbeLifecyclePluginPack : IToolPack {
        public sealed class PluginOptions {
            public IToolAuthenticationProbeStore? AuthenticationProbeStore { get; set; }
            public bool RequireSuccessfulSmtpProbeForSend { get; set; }
            public int SmtpProbeMaxAgeSeconds { get; set; }
        }

        public static IToolAuthenticationProbeStore? LastAuthenticationProbeStore { get; private set; }
        public static bool LastRequireSuccessfulSmtpProbeForSend { get; private set; }
        public static int LastSmtpProbeMaxAgeSeconds { get; private set; }

        public ProbeLifecyclePluginPack(PluginOptions options) {
            ArgumentNullException.ThrowIfNull(options);
            LastAuthenticationProbeStore = options.AuthenticationProbeStore;
            LastRequireSuccessfulSmtpProbeForSend = options.RequireSuccessfulSmtpProbeForSend;
            LastSmtpProbeMaxAgeSeconds = options.SmtpProbeMaxAgeSeconds;
        }

        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "probe-lifecycle-pack",
            Name = "Probe Lifecycle Pack",
            Tier = ToolCapabilityTier.ReadOnly,
            IsDangerous = false,
            SourceKind = "open_source"
        };

        public static void ResetCapturedOptions() {
            LastAuthenticationProbeStore = null;
            LastRequireSuccessfulSmtpProbeForSend = false;
            LastSmtpProbeMaxAgeSeconds = 0;
        }

        public void Register(ToolRegistry registry) {
            ArgumentNullException.ThrowIfNull(registry);
        }
    }
}
