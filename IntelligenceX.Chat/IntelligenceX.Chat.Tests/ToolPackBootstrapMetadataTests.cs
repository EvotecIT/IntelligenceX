using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolPackBootstrapMetadataTests {
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

    [Fact]
    public void CreateRuntimeBootstrapOptions_MapsSettingsAndRuntimePolicyContext() {
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
            RunAsProfilePath = "C:/temp/runas.json",
            AuthenticationProfilePath = "C:/temp/auth.json"
        });

        var options = ToolPackBootstrap.CreateRuntimeBootstrapOptions(
            new TestPackRuntimeSettings {
                AllowedRoots = new[] { "C:/allowed-a", "C:/allowed-b" },
                AdDomainController = "dc.contoso.local",
                AdDefaultSearchBaseDn = "DC=contoso,DC=local",
                AdMaxResults = 2222,
                PowerShellAllowWrite = true,
                EnableBuiltInPackLoading = false,
                UseDefaultBuiltInToolAssemblyNames = false,
                BuiltInToolAssemblyNames = new[] { "IntelligenceX.Tools.System", "IntelligenceX.Tools.EventLog" },
                BuiltInToolProbePaths = new[] { "C:/tools/a", "C:/tools/b" },
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { "C:/plugins/a", "C:/plugins/b" },
                DisabledPackIds = new[] { "officeimo", "testimox", "dnsclientx", "domaindetective" },
                EnabledPackIds = new[] { "powershell", "plugin-loader-test" }
            },
            runtimePolicyContext);

        Assert.Equal(new[] { "C:/allowed-a", "C:/allowed-b" }, options.AllowedRoots);
        Assert.Equal("dc.contoso.local", options.AdDomainController);
        Assert.Equal("DC=contoso,DC=local", options.AdDefaultSearchBaseDn);
        Assert.Equal(2222, options.AdMaxResults);
        Assert.True(options.PowerShellAllowWrite);
        Assert.False(options.EnableBuiltInPackLoading);
        Assert.False(options.UseDefaultBuiltInToolAssemblyNames);
        Assert.Equal(new[] { "IntelligenceX.Tools.System", "IntelligenceX.Tools.EventLog" }, options.BuiltInToolAssemblyNames);
        Assert.Equal(new[] { "C:/tools/a", "C:/tools/b" }, options.BuiltInToolProbePaths);
        Assert.False(options.EnableDefaultPluginPaths);
        Assert.Equal(new[] { "C:/plugins/a", "C:/plugins/b" }, options.PluginPaths);
        Assert.Equal(new[] { "officeimo", "testimox", "dnsclientx", "domaindetective" }, options.DisabledPackIds);
        Assert.Equal(new[] { "powershell", "plugin-loader-test" }, options.EnabledPackIds);
        Assert.Same(runtimePolicyContext.AuthenticationProbeStore, options.AuthenticationProbeStore);
        Assert.True(options.RequireSuccessfulSmtpProbeForSend);
        Assert.Equal(600, options.SmtpProbeMaxAgeSeconds);
        Assert.Equal(runtimePolicyContext.Options.RunAsProfilePath, options.RunAsProfilePath);
        Assert.Equal(runtimePolicyContext.Options.AuthenticationProfilePath, options.AuthenticationProfilePath);
        Assert.Empty(options.PackRuntimeOptionBag);
    }

    [Fact]
    public void ConfigurePackOptions_AppliesRuntimeConfigurableOptionsWithoutChatSidePackKeyMapping() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "ConfigurePackOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var options = new RuntimeConfigurableOptions();
        var bootstrapOptions = new ToolPackBootstrapOptions {
            AllowedRoots = new[] { "C:/runtime-a", "C:/runtime-b" },
            AdDomainController = "dc01.contoso.local",
            AdDefaultSearchBaseDn = "DC=contoso,DC=local",
            AdMaxResults = 4096
        };

        method!.Invoke(null, new object?[] {
            options,
            bootstrapOptions,
            typeof(RuntimeConfigurablePack),
            null
        });

        Assert.Equal(new[] { "C:/runtime-a", "C:/runtime-b" }, options.AppliedRoots);
        Assert.Equal("dc01.contoso.local", options.AppliedDomainController);
        Assert.Equal("DC=contoso,DC=local", options.AppliedSearchBaseDn);
        Assert.Equal(4096, options.AppliedMaxResults);
    }

    [Fact]
    public void ConfigurePackOptions_AllowsExplicitRuntimeOptionBagOverrides_OnRuntimeConfigurableOptions() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "ConfigurePackOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var options = new RuntimeConfigurableOptions();
        var bootstrapOptions = new ToolPackBootstrapOptions {
            AllowedRoots = new[] { "C:/runtime-a", "C:/runtime-b" },
            AdDomainController = "dc01.contoso.local",
            AdDefaultSearchBaseDn = "DC=contoso,DC=local",
            AdMaxResults = 4096,
            PackRuntimeOptionBag = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase) {
                ["runtime_configurable"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["AppliedDomainController"] = "override.contoso.local",
                    ["AppliedMaxResults"] = 512
                }
            }
        };

        method!.Invoke(null, new object?[] {
            options,
            bootstrapOptions,
            typeof(RuntimeConfigurablePack),
            null
        });

        Assert.Equal(new[] { "C:/runtime-a", "C:/runtime-b" }, options.AppliedRoots);
        Assert.Equal("override.contoso.local", options.AppliedDomainController);
        Assert.Equal("DC=contoso,DC=local", options.AppliedSearchBaseDn);
        Assert.Equal(512, options.AppliedMaxResults);
    }

    [Fact]
    public void NormalizeSourceKind_Throws_WhenSourceKindMissing() {
        Assert.Throws<ArgumentException>(() => ToolPackBootstrap.NormalizeSourceKind(sourceKind: null, descriptorId: "system"));
    }

    [Fact]
    public void EnumerateToolAssemblyNamesForDiscovery_StaysWithinRuntimeDiscoveredDefaultBuiltInAssemblySet() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "EnumerateToolAssemblyNamesForDiscovery",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var defaultDiscoveryMethod = typeof(ToolPackBootstrap).GetMethod(
            "DiscoverDefaultBuiltInAssemblyNames",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(defaultDiscoveryMethod);

        var options = new ToolPackBootstrapOptions();
        var discovered = Assert.IsAssignableFrom<IEnumerable<AssemblyName>>(method!.Invoke(null, new object[] { options }));
        var discoveredNames = discovered
            .Select(static assemblyName => (assemblyName.Name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .ToArray();
        Assert.NotEmpty(discoveredNames);

        var discoveredDefaultAssemblyNames = Assert.IsAssignableFrom<IEnumerable<string>>(defaultDiscoveryMethod!.Invoke(null, new object?[] { null }));
        var allowlist = new HashSet<string>(
            discoveredDefaultAssemblyNames.Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(allowlist);

        Assert.All(discoveredNames, assemblyName => Assert.Contains(assemblyName, allowlist));
    }

    [Fact]
    public void DiscoverDefaultBuiltInAssemblyNames_UsesToolingAssemblyReferences_InsteadOfOutputFolderEnumeration() {
        var defaultDiscoveryMethod = typeof(ToolPackBootstrap).GetMethod(
            "DiscoverDefaultBuiltInAssemblyNames",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(defaultDiscoveryMethod);

        var discoveredDefaultAssemblyNames = Assert.IsAssignableFrom<IEnumerable<string>>(defaultDiscoveryMethod!.Invoke(null, new object?[] { null }));
        var discovered = new HashSet<string>(
            discoveredDefaultAssemblyNames.Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("IntelligenceX.Tools.System", discovered, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IntelligenceX.Tools.TestimoX", discovered, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IntelligenceX.Tools.TestimoX.Analytics", discovered, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("IntelligenceX.Tools.Common", discovered, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryOpenBuiltInToolAssemblyManifestStream_ReturnsEmbeddedManifestStream() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "TryOpenBuiltInToolAssemblyManifestStream",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        using var stream = Assert.IsAssignableFrom<Stream?>(
            method!.Invoke(null, new object[] { typeof(ToolPackBootstrap).Assembly }));
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void GetBuiltInToolAssemblyProbePaths_IncludesExplicitAndEnvironmentProbeRoots() {
        var explicitProbeRoot = Path.GetTempPath();
        var environmentProbeRoot = AppContext.BaseDirectory;
        var previousValue = Environment.GetEnvironmentVariable("INTELLIGENCEX_BUILTIN_TOOL_PROBE_PATHS");
        Environment.SetEnvironmentVariable(
            "INTELLIGENCEX_BUILTIN_TOOL_PROBE_PATHS",
            environmentProbeRoot + Path.PathSeparator + environmentProbeRoot);
        try {
            var probePaths = ToolPackBootstrap.GetBuiltInToolAssemblyProbePaths(new ToolPackBootstrapOptions {
                BuiltInToolProbePaths = new[] { explicitProbeRoot, explicitProbeRoot }
            });

            Assert.Contains(Path.GetFullPath(explicitProbeRoot), probePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(environmentProbeRoot), probePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(probePaths.Count, probePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_BUILTIN_TOOL_PROBE_PATHS", previousValue);
        }
    }

    [Fact]
    public void EnumerateToolAssemblyNamesForDiscovery_UsesConfiguredAssemblyNames_WhenDefaultAllowlistIsDisabled() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "EnumerateToolAssemblyNamesForDiscovery",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var options = new ToolPackBootstrapOptions {
            UseDefaultBuiltInToolAssemblyNames = false,
            BuiltInToolAssemblyNames = new[] { "IntelligenceX.Tools.System" }
        };
        var discovered = Assert.IsAssignableFrom<IEnumerable<AssemblyName>>(method!.Invoke(null, new object[] { options }));
        var discoveredNames = discovered
            .Select(static assemblyName => (assemblyName.Name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .ToArray();

        Assert.Single(discoveredNames);
        Assert.Equal("IntelligenceX.Tools.System", discoveredNames[0], ignoreCase: true);
    }

    [Fact]
    public void EnumerateToolAssemblyNamesForDiscovery_SkipsInvalidConfiguredAssemblyNames_WithoutThrowing() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "EnumerateToolAssemblyNamesForDiscovery",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string>();
        var options = new ToolPackBootstrapOptions {
            UseDefaultBuiltInToolAssemblyNames = false,
            BuiltInToolAssemblyNames = new[] {
                "IntelligenceX.Tools.System",
                "IntelligenceX.Tools.System, Version=not-a-version"
            },
            OnBootstrapWarning = warnings.Add
        };
        var discovered = Assert.IsAssignableFrom<IEnumerable<AssemblyName>>(method!.Invoke(null, new object[] { options }));
        var discoveredNames = discovered
            .Select(static assemblyName => (assemblyName.Name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .ToArray();

        Assert.Single(discoveredNames);
        Assert.Equal("IntelligenceX.Tools.System", discoveredNames[0], ignoreCase: true);
        Assert.Contains(
            warnings,
            static warning => warning.Contains("built_in_pack_assembly_skipped", StringComparison.OrdinalIgnoreCase)
                              && warning.Contains("invalid assembly name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateToolAssemblyNamesForDiscovery_NormalizesConfiguredDisplayNames_ToSimpleAssemblyNameMatching() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "EnumerateToolAssemblyNamesForDiscovery",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var options = new ToolPackBootstrapOptions {
            UseDefaultBuiltInToolAssemblyNames = false,
            BuiltInToolAssemblyNames = new[] { "IntelligenceX.Tools.System, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" }
        };
        var discovered = Assert.IsAssignableFrom<IEnumerable<AssemblyName>>(method!.Invoke(null, new object[] { options }));
        var discoveredNames = discovered
            .Select(static assemblyName => (assemblyName.Name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .ToArray();

        Assert.Single(discoveredNames);
        Assert.Equal("IntelligenceX.Tools.System", discoveredNames[0], ignoreCase: true);
    }

    [Fact]
    public void TryResolveTrustedToolAssemblyPath_ResolvesLoadablePath_ForDiscoveredToolAssembly() {
        var enumerateAssembliesMethod = typeof(ToolPackBootstrap).GetMethod(
            "EnumerateToolAssemblyNamesForDiscovery",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(enumerateAssembliesMethod);
        var resolvePathMethod = typeof(ToolPackBootstrap).GetMethod(
            "TryResolveTrustedToolAssemblyPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolvePathMethod);

        var options = new ToolPackBootstrapOptions();
        var discovered = Assert.IsAssignableFrom<IEnumerable<AssemblyName>>(enumerateAssembliesMethod!.Invoke(null, new object[] { options }));
        var assemblyName = Assert.Single(discovered.Take(1));
        var invocationArguments = new object?[] { assemblyName, options, null };
        var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
        var trustedAssemblyPath = Assert.IsType<string>(invocationArguments[2]);

        Assert.True(resolved);
        Assert.False(string.IsNullOrWhiteSpace(trustedAssemblyPath));
        Assert.True(File.Exists(trustedAssemblyPath));
    }

    [Fact]
    public void TryResolveTrustedToolAssemblyPath_ReturnsFalse_WhenAssemblyNameIsMissing() {
        var resolvePathMethod = typeof(ToolPackBootstrap).GetMethod(
            "TryResolveTrustedToolAssemblyPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolvePathMethod);
        var invocationArguments = new object?[] { new AssemblyName(), new ToolPackBootstrapOptions(), null };
        var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
        var trustedAssemblyPath = invocationArguments[2] as string;

        Assert.False(resolved);
        Assert.Equal(string.Empty, trustedAssemblyPath);
    }

    [Fact]
    public void TryResolveTrustedToolAssemblyPath_ResolvesAllDiscoveredAssemblyNames() {
        var enumerateAssembliesMethod = typeof(ToolPackBootstrap).GetMethod(
            "EnumerateToolAssemblyNamesForDiscovery",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(enumerateAssembliesMethod);
        var resolvePathMethod = typeof(ToolPackBootstrap).GetMethod(
            "TryResolveTrustedToolAssemblyPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolvePathMethod);

        var options = new ToolPackBootstrapOptions();
        var discovered = Assert.IsAssignableFrom<IEnumerable<AssemblyName>>(enumerateAssembliesMethod!.Invoke(null, new object[] { options }));
        var discoveredAssemblyNames = discovered.ToArray();
        Assert.NotEmpty(discoveredAssemblyNames);

        foreach (var assemblyName in discoveredAssemblyNames) {
            var invocationArguments = new object?[] { assemblyName, options, null };
            var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
            var trustedAssemblyPath = invocationArguments[2] as string;
            Assert.True(resolved, $"Failed to resolve trusted path for '{assemblyName.Name}'.");
            Assert.False(string.IsNullOrWhiteSpace(trustedAssemblyPath));
            Assert.True(File.Exists(trustedAssemblyPath), $"Resolved path '{trustedAssemblyPath}' for '{assemblyName.Name}' does not exist.");
        }
    }

    [Fact]
    public void TryResolveTrustedToolAssemblyPathFromProbeRoots_ResolvesMatchingAssemblyCopy() {
        var resolvePathMethod = typeof(ToolPackBootstrap).GetMethod(
            "TryResolveTrustedToolAssemblyPathFromProbeRoots",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolvePathMethod);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try {
            var sourceAssemblyPath = typeof(SystemToolPack).Assembly.Location;
            var destinationAssemblyPath = Path.Combine(tempRoot, Path.GetFileName(sourceAssemblyPath));
            File.Copy(sourceAssemblyPath, destinationAssemblyPath, overwrite: true);

            var invocationArguments = new object?[] {
                new AssemblyName("IntelligenceX.Tools.System"),
                new[] { tempRoot },
                null
            };
            var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
            var trustedAssemblyPath = invocationArguments[2] as string;

            Assert.True(resolved);
            Assert.Equal(Path.GetFullPath(destinationAssemblyPath), trustedAssemblyPath, ignoreCase: true);
        } finally {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_LoadsBuiltInPacks_WithTrustedAssemblyResolution() {
        var warnings = new List<string>();
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableDefaultPluginPaths = false,
            EnablePluginFolderLoading = false,
            OnBootstrapWarning = warnings.Add
        });

        Assert.True(
            packs.Count > 0,
            "Expected at least one built-in pack. Bootstrap warnings: " + string.Join(" | ", warnings));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DisablesBuiltInPacks_WhenConfigured() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableBuiltInPackLoading = false,
            EnablePluginFolderLoading = false,
            EnableDefaultPluginPaths = false
        });

        Assert.Empty(packs);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DisablesDefaultBuiltInAssemblyAllowlist_WhenConfigured() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableBuiltInPackLoading = true,
            UseDefaultBuiltInToolAssemblyNames = false,
            EnablePluginFolderLoading = false,
            EnableDefaultPluginPaths = false
        });

        Assert.Empty(packs);
    }

    [Theory]
    [InlineData("open_source", "open_source")]
    [InlineData("public", "open_source")]
    [InlineData("closed_source", "closed_source")]
    [InlineData("private", "closed_source")]
    [InlineData("builtin", "builtin")]
    public void NormalizeSourceKind_NormalizesKnownValues(string input, string expected) {
        var normalized = ToolPackBootstrap.NormalizeSourceKind(input);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("system", "system")]
    [InlineData("ad", "active_directory")]
    [InlineData("adplayground", "active_directory")]
    [InlineData("reviewer_setup", "reviewer_setup")]
    [InlineData("event-log", "eventlog")]
    [InlineData("file system", "filesystem")]
    [InlineData("testimoxpack", "testimox")]
    [InlineData("custom pack", "custom_pack")]
    public void NormalizePackId_UsesCanonicalShape(string input, string expected) {
        var normalized = ToolPackBootstrap.NormalizePackId(input);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void ResolvePackRuntimeOptionKeys_IncludesOptionOwnedAliases() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "ResolvePackRuntimeOptionKeys",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(object), typeof(Type), typeof(string) },
            modifiers: null);
        Assert.NotNull(method);

        var keys = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(
            null,
            new object?[] { new SyntheticRuntimeOptionTarget(), typeof(TestPack), null }));

        Assert.Equal(2, keys.Count);
        Assert.Equal("*", keys[0]);
        Assert.Equal("synthetic_runtime_target", keys[1]);
    }

    [Fact]
    public void ResolvePackRuntimeOptionKeys_FallsBackToGlobalOnly_ForUndeclaredOptionTargets() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "ResolvePackRuntimeOptionKeys",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(object), typeof(Type), typeof(string) },
            modifiers: null);
        Assert.NotNull(method);

        var keys = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(
            null,
            new object?[] { null, typeof(TestPack), null }));

        Assert.Single(keys);
        Assert.Equal("*", keys[0], ignoreCase: true);
    }

    [Fact]
    public void ResolvePackRuntimeOptionKeys_UsesExplicitPackKey_WhenProvidedForUndeclaredOptionTargets() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "ResolvePackRuntimeOptionKeys",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(object), typeof(Type), typeof(string) },
            modifiers: null);
        Assert.NotNull(method);

        var keys = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(
            null,
            new object?[] { null, typeof(TestPack), "custom_pack" }));

        Assert.Equal(2, keys.Count);
        Assert.Equal("*", keys[0], ignoreCase: true);
        Assert.Equal("custom_pack", keys[1], ignoreCase: true);
    }

    [Fact]
    public void ResolvePackRuntimeOptionKeys_UsesRealPackEngineAliases() {
        var method = typeof(ToolPackBootstrap).GetMethod(
            "ResolvePackRuntimeOptionKeys",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(object), typeof(Type), typeof(string) },
            modifiers: null);
        Assert.NotNull(method);

        var adOptions = new ActiveDirectoryToolOptions();
        var eventLogOptions = new EventLogToolOptions();
        var systemOptions = new SystemToolOptions();

        var adKeys = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(
            null,
            new object?[] { adOptions, typeof(ActiveDirectoryToolPack), null }));
        var eventLogKeys = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(
            null,
            new object?[] { eventLogOptions, typeof(EventLogToolPack), null }));
        var systemKeys = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(
            null,
            new object?[] { systemOptions, typeof(SystemToolPack), null }));

        Assert.Contains("adplayground", adOptions.RuntimeOptionKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventviewerx", eventLogOptions.RuntimeOptionKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("computerx", systemOptions.RuntimeOptionKeys, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("active_directory", adKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog", eventLogKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", systemKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_AppliesSharedAndPackSpecificRuntimeOptionBagKeys_ForSharedTestimoXOptions() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("testimox", "testimox_analytics"),
            EnableDefaultPluginPaths = false,
            PackRuntimeOptionBag = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase) {
                ["testimox"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["MaxRulesPerRun"] = 111
                },
                ["testimox_analytics"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["MaxRulesPerRun"] = 222
                }
            }
        });

        var corePack = Assert.IsType<TestimoXToolPack>(Assert.Single(
            packs,
            static pack => string.Equals(pack.Descriptor.Id, "testimox", StringComparison.OrdinalIgnoreCase)));
        var analyticsPack = Assert.IsType<TestimoXAnalyticsToolPack>(Assert.Single(
            packs,
            static pack => string.Equals(pack.Descriptor.Id, "testimox_analytics", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(111, GetPackOptions<TestimoXToolOptions>(corePack).MaxRulesPerRun);
        Assert.Equal(222, GetPackOptions<TestimoXToolOptions>(analyticsPack).MaxRulesPerRun);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_IncludesOfficeImoPack_ByDefault() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("officeimo"),
            EnableDefaultPluginPaths = false
        });

        Assert.Contains(packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_RespectsDisableOfficeImoPack() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DefaultEnabledKnownPackIds,
            EnableDefaultPluginPaths = false
        });

        Assert.DoesNotContain(packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_EnabledPackIds_OverridesDefaultDisabledKnownPack() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DefaultEnabledKnownPackIds,
            EnableDefaultPluginPaths = false,
            EnabledPackIds = new[] { "powershell" }
        });

        Assert.Contains(packs, static pack => string.Equals(pack.Descriptor.Id, "powershell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_EnabledPackIds_LoadsDangerousActiveDirectoryLifecyclePack_OnExplicitOptIn() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableDefaultPluginPaths = false,
            EnabledPackIds = new[] { "active_directory_lifecycle" }
        });

        var lifecyclePack = Assert.Single(packs, static pack =>
            string.Equals(pack.Descriptor.Id, "active_directory_lifecycle", StringComparison.OrdinalIgnoreCase));
        Assert.True(lifecyclePack.Descriptor.IsDangerous);
        Assert.Equal(ToolCapabilityTier.DangerousWrite, lifecyclePack.Descriptor.Tier);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_DisabledPackIds_TakesPrecedenceOverEnabledPackIds() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DefaultEnabledKnownPackIds,
            EnableDefaultPluginPaths = false,
            EnabledPackIds = new[] { "officeimo" }
        });

        Assert.DoesNotContain(packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_UsesCanonicalDescriptorIds_AndPublishesLegacyAliases() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("active_directory", "filesystem", "reviewer_setup"),
            EnableDefaultPluginPaths = false
        });

        var activeDirectory = Assert.Single(packs, static pack =>
            string.Equals(pack.Descriptor.Id, "active_directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ad", activeDirectory.Descriptor.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("adplayground", activeDirectory.Descriptor.Aliases, StringComparer.OrdinalIgnoreCase);

        var fileSystem = Assert.Single(packs, static pack =>
            string.Equals(pack.Descriptor.Id, "filesystem", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("fs", fileSystem.Descriptor.Aliases, StringComparer.OrdinalIgnoreCase);

        var reviewerSetup = Assert.Single(packs, static pack =>
            string.Equals(pack.Descriptor.Id, "reviewer_setup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("reviewersetup", reviewerSetup.Descriptor.Aliases, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsDisabledReason_WhenPackDisabledByConfiguration() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            DisabledPackIds = DefaultEnabledKnownPackIds,
            EnableDefaultPluginPaths = false
        });

        Assert.DoesNotContain(result.Packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));

        var officeImo = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
        Assert.False(officeImo.Enabled);
        Assert.Equal("Disabled by runtime configuration.", officeImo.DisabledReason);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsEnabled_WhenPackLoaded() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("officeimo"),
            EnableDefaultPluginPaths = false
        });

        var officeImo = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
        Assert.True(officeImo.Enabled);
        Assert.True(string.IsNullOrWhiteSpace(officeImo.DisabledReason));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsDangerousActiveDirectoryLifecyclePackDisabledByDefault() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            EnableDefaultPluginPaths = false
        });

        Assert.DoesNotContain(result.Packs, static pack =>
            string.Equals(pack.Descriptor.Id, "active_directory_lifecycle", StringComparison.OrdinalIgnoreCase));

        var lifecyclePack = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "active_directory_lifecycle", StringComparison.OrdinalIgnoreCase));
        Assert.False(lifecyclePack.Enabled);
        Assert.True(lifecyclePack.IsDangerous);
        Assert.Equal(ToolCapabilityTier.DangerousWrite, lifecyclePack.Tier);
        Assert.Equal("active_directory", lifecyclePack.Category);
        Assert.Equal("adplayground", lifecyclePack.EngineId);
        Assert.Contains("governed_write", lifecyclePack.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dry_run", lifecyclePack.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("joiner", lifecyclePack.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("mover", lifecyclePack.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("offboarding", lifecyclePack.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Disabled by runtime configuration.", lifecyclePack.DisabledReason);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ProjectsDeclaredEngineMetadata() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("ad", "system", "eventlog"),
            EnableDefaultPluginPaths = false
        });

        var activeDirectory = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "active_directory", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("adplayground", activeDirectory.EngineId);
        Assert.Contains("directory", activeDirectory.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("replication", activeDirectory.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("remote_analysis", activeDirectory.CapabilityTags, StringComparer.OrdinalIgnoreCase);

        var system = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "system", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("computerx", system.EngineId);
        Assert.Contains("cpu", system.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("host_inventory", system.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("local_analysis", system.CapabilityTags, StringComparer.OrdinalIgnoreCase);

        var eventLog = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "eventlog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("eventviewerx", eventLog.EngineId);
        Assert.Contains("auth", eventLog.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("event_logs", eventLog.CapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("evtx", eventLog.CapabilityTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ProjectsDeclaredCategoryAndSearchTokens() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("ad", "system", "eventlog"),
            EnableDefaultPluginPaths = false
        });

        var activeDirectory = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "active_directory", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("active_directory", activeDirectory.Category);
        Assert.Contains("ad", activeDirectory.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("adplayground", activeDirectory.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("adplayground", activeDirectory.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("directory", activeDirectory.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("replication", activeDirectory.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gpo", activeDirectory.SearchTokens, StringComparer.OrdinalIgnoreCase);

        var system = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "system", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("system", system.Category);
        Assert.Contains("computerx", system.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("cpu", system.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("disk_space", system.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("memory", system.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("disk", system.SearchTokens, StringComparer.OrdinalIgnoreCase);

        var eventLog = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "eventlog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("eventlog", eventLog.Category);
        Assert.Contains("auth", eventLog.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventviewerx", eventLog.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("evtx", eventLog.SearchTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("windows_logs", eventLog.SearchTokens, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsDisabledReason_ForDnsOpenSourcePacksWhenDisabledByConfiguration() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            DisabledPackIds = DefaultEnabledKnownPackIds,
            EnableDefaultPluginPaths = false
        });

        var dnsClientX = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "dnsclientx", StringComparison.OrdinalIgnoreCase));
        Assert.False(dnsClientX.Enabled);
        Assert.Equal("Disabled by runtime configuration.", dnsClientX.DisabledReason);
        Assert.Equal("open_source", dnsClientX.SourceKind);

        var domainDetective = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "domaindetective", StringComparison.OrdinalIgnoreCase));
        Assert.False(domainDetective.Enabled);
        Assert.Equal("Disabled by runtime configuration.", domainDetective.DisabledReason);
        Assert.Equal("open_source", domainDetective.SourceKind);
    }

    [Fact]
    public void RegisterAll_AssignsPackIds_ForRegisteredTools() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DisableDefaultsExcept("eventlog", "reviewer_setup"),
            EnableDefaultPluginPaths = false
        });
        var registry = new ToolRegistry();
        var toolPackIdsByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ToolPackBootstrap.RegisterAll(registry, packs, toolPackIdsByToolName);

        Assert.True(toolPackIdsByToolName.TryGetValue("eventlog_pack_info", out var eventLogPackId));
        Assert.Equal("eventlog", eventLogPackId);

        Assert.True(toolPackIdsByToolName.TryGetValue("reviewer_setup_pack_info", out var reviewerPackId));
        Assert.Equal("reviewer_setup", reviewerPackId);
    }

    [Fact]
    public void RegisterAll_DefaultLoadedPacks_ExposeExplicitRoutingContracts_ForPackInfoTools() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnablePluginFolderLoading = false,
            EnableDefaultPluginPaths = false
        });
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };

        ToolPackBootstrap.RegisterAll(registry, packs);

        var packInfoDefinitions = registry.GetDefinitions()
            .Where(static definition => definition.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(packInfoDefinitions);

        foreach (var definition in packInfoDefinitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(routing.PackId));
            Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, routing.Role, ignoreCase: true);
        }
    }

    [Fact]
    public void RegisterAll_DefaultLoadedPacks_ExposeExplicitRoutingContracts_ForAllCanonicalTools() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnablePluginFolderLoading = false,
            EnableDefaultPluginPaths = false
        });
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };

        ToolPackBootstrap.RegisterAll(registry, packs);

        var canonicalDefinitions = registry.GetDefinitions()
            .Where(static definition => string.IsNullOrWhiteSpace(definition.AliasOf))
            .OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(canonicalDefinitions);

        foreach (var definition in canonicalDefinitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(routing.PackId));
            Assert.Contains(routing.Role, ToolRoutingTaxonomy.AllowedRoles, StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(routing.DomainIntentFamily)) {
                Assert.False(
                    string.IsNullOrWhiteSpace(routing.DomainIntentActionId),
                    $"Tool '{definition.Name}' declares domain family '{routing.DomainIntentFamily}' but has no action id.");
            }
        }
    }

    [Fact]
    public void RegisterAll_AndCatalog_SupportSyntheticPackWithoutChatHardcoding() {
        var packs = new IToolPack[] {
            new SyntheticPack("sample-pack-v2", "Sample Pack v2")
        };
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var toolPackIdsByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ToolPackBootstrap.RegisterAll(registry, packs, toolPackIdsByToolName);

        Assert.True(registry.TryGetDefinition("sample_inventory_query", out var definition));
        Assert.NotNull(definition);
        Assert.Equal("sample_pack_v2", definition!.Routing!.PackId, ignoreCase: true);

        Assert.True(toolPackIdsByToolName.TryGetValue("sample_inventory_query", out var assignedPackId));
        Assert.Equal("sample_pack_v2", assignedPackId);

        var catalog = ToolOrchestrationCatalog.Build(registry.GetDefinitions());
        Assert.True(catalog.TryGetEntry("sample_inventory_query", out var entry));
        Assert.Equal("sample_pack_v2", entry.PackId);
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, entry.Role);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, entry.RoutingSource);
        Assert.Equal("corp_internal", entry.DomainIntentFamily);
    }

    [Fact]
    public void RegisterAll_Throws_WhenDistinctPackIdsCollideAfterNormalization() {
        var packs = new IToolPack[] {
            new TestPack("event-log", "EventLog A"),
            new TestPack("event_log", "EventLog B")
        };
        var registry = new ToolRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() => ToolPackBootstrap.RegisterAll(registry, packs));

        Assert.Contains("both normalize to 'eventlog'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterAll_Throws_WhenPackAliasNormalizesToDifferentCanonicalPackId() {
        var packs = new IToolPack[] {
            new TestPack("filesystem", "Filesystem", aliases: new[] { "event-log" })
        };
        var registry = new ToolRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() => ToolPackBootstrap.RegisterAll(registry, packs));

        Assert.Contains("alias 'event-log'", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'filesystem'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatePackMetadataIndexes_Throws_WhenDescriptorIdsCollideAfterNormalization() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var descriptors = new[] {
            new ToolPackDescriptor { Id = "event-log", Name = "EventLog A", Tier = ToolCapabilityTier.ReadOnly, SourceKind = "open_source" },
            new ToolPackDescriptor { Id = "event_log", Name = "EventLog B", Tier = ToolCapabilityTier.ReadOnly, SourceKind = "open_source" }
        };
        var method = typeof(ChatServiceSession).GetMethod("UpdatePackMetadataIndexes", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(session, new object[] { descriptors }));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);

        Assert.Contains("both normalize to 'eventlog'", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatePackMetadataIndexes_AllowsCustomRuntimeAliases_WhenTheyDoNotHijackKnownPackIdentity() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var descriptors = new[] {
            new ToolPackDescriptor {
                Id = "ops_inventory",
                Name = "Ops Inventory",
                Aliases = new[] { "serverops" },
                Tier = ToolCapabilityTier.ReadOnly,
                SourceKind = "open_source"
            }
        };
        var method = typeof(ChatServiceSession).GetMethod("UpdatePackMetadataIndexes", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        method!.Invoke(session, new object[] { descriptors });
    }

    [Fact]
    public void BuildToolDefinitionDtos_PreservesRepresentativePackMetadataFromOrchestrationCatalog() {
        var adPack = new ActiveDirectoryToolPack(new ActiveDirectoryToolOptions());
        var eventLogPack = new EventLogToolPack(new EventLogToolOptions());
        var systemPack = new SystemToolPack(new SystemToolOptions());
        IToolPack[] packs = { adPack, eventLogPack, systemPack };

        var registry = new ToolRegistry();
        ToolPackBootstrap.RegisterAll(registry, packs);

        var definitions = registry.GetDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, packs);
        var packAvailability = packs
            .Select(static pack => new ToolPackAvailabilityInfo {
                Id = pack.Descriptor.Id ?? string.Empty,
                Name = pack.Descriptor.Name ?? string.Empty,
                Description = pack.Descriptor.Description,
                Tier = pack.Descriptor.Tier,
                IsDangerous = pack.Descriptor.Tier == ToolCapabilityTier.DangerousWrite,
                SourceKind = pack.Descriptor.SourceKind ?? string.Empty,
                Enabled = true
            })
            .ToArray();
        var toolDtos = ToolCatalogExportBuilder.BuildToolDefinitionDtos(definitions, orchestrationCatalog, packAvailability)
            .ToDictionary(static tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        var packAvailabilityById = packAvailability.ToDictionary(
            static pack => ToolPackBootstrap.NormalizePackId(pack.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in new[] {
                     "ad_pack_info",
                     "ad_environment_discover",
                     "ad_scope_discovery",
                     "system_metrics_summary",
                     "system_time_sync",
                     "eventlog_channels_list",
                     "eventlog_timeline_query"
                 }) {
            Assert.True(orchestrationCatalog.TryGetEntry(toolName, out var entry));
            var dto = Assert.IsType<ToolDefinitionDto>(toolDtos[toolName]);
            AssertToolDtoMatchesOrchestration(dto, entry!, packAvailabilityById);
        }
    }

    [Fact]
    public void BuildToolDefinitionDtos_FallsBackToPackCategory_WhenDefinitionCategoryIsMissingAndPackSelfRegistersIt() {
        var definition = new ToolDefinition(
            "ops_inventory_query",
            "Query remote host inventory.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "ops_inventory",
                Role = ToolRoutingTaxonomy.RoleOperational
            });
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(new[] { definition });
        var packAvailability = new[] {
            new ToolPackAvailabilityInfo {
                Id = "ops_inventory",
                Name = "Ops Inventory",
                SourceKind = "open_source",
                Category = "system",
                Enabled = true
            }
        };

        var dto = Assert.Single(ToolCatalogExportBuilder.BuildToolDefinitionDtos(new[] { definition }, orchestrationCatalog, packAvailability));

        Assert.Equal("system", dto.Category);
    }

    [Fact]
    public void BuildPackInfoDtos_ProjectsRuntimePackMetadata() {
        var packs = new ToolPackAvailabilityInfo[] {
            new() {
                Id = "ops_inventory",
                Name = "Ops Inventory",
                Description = "Remote inventory tooling.",
                Tier = ToolCapabilityTier.ReadOnly,
                SourceKind = "closed_source",
                EngineId = "computerx",
                Aliases = new[] { "serverops", "host_inventory" },
                Category = "system",
                CapabilityTags = new[] { "host_inventory", "remote_analysis" },
                SearchTokens = new[] { "computerx", "server_inventory", "cpu", "memory" },
                Enabled = true
            }
        };

        var dtos = ToolCatalogExportBuilder.BuildPackInfoDtos(packs, orchestrationCatalog: null);
        var dto = Assert.Single(dtos);

        Assert.Equal("ops_inventory", dto.Id);
        Assert.Equal("system", dto.Category);
        Assert.Equal("computerx", dto.EngineId);
        Assert.Equal(new[] { "serverops", "host_inventory" }, dto.Aliases);
        Assert.Equal(new[] { "host_inventory", "remote_analysis" }, dto.CapabilityTags);
        Assert.Equal(new[] { "computerx", "server_inventory", "cpu", "memory" }, dto.SearchTokens);
    }

    private sealed class TestPackRuntimeSettings : IToolPackRuntimeSettings {
        public IReadOnlyList<string> AllowedRoots { get; init; } = Array.Empty<string>();
        public string? AdDomainController { get; init; }
        public string? AdDefaultSearchBaseDn { get; init; }
        public int AdMaxResults { get; init; } = 1000;
        public bool PowerShellAllowWrite { get; init; }
        public bool EnableBuiltInPackLoading { get; init; } = true;
        public bool UseDefaultBuiltInToolAssemblyNames { get; init; } = true;
        public IReadOnlyList<string> BuiltInToolAssemblyNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> BuiltInToolProbePaths { get; init; } = Array.Empty<string>();
        public bool EnableDefaultPluginPaths { get; init; } = true;
        public IReadOnlyList<string> PluginPaths { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DisabledPackIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> EnabledPackIds { get; init; } = Array.Empty<string>();
    }

    private sealed class SyntheticRuntimeOptionTarget : IToolPackRuntimeOptionTarget {
        public IReadOnlyList<string> RuntimeOptionKeys => new[] { "synthetic_runtime_target" };
    }

    private sealed class TestPack : IToolPack {
        public TestPack(string id, string name, IReadOnlyList<string>? aliases = null) {
            Descriptor = new ToolPackDescriptor {
                Id = id,
                Name = name,
                Aliases = aliases ?? Array.Empty<string>(),
                Tier = ToolCapabilityTier.ReadOnly,
                SourceKind = "open_source"
            };
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            _ = registry;
        }
    }

    private sealed class SyntheticPack : IToolPack {
        public SyntheticPack(string id, string name) {
            Descriptor = new ToolPackDescriptor {
                Id = id,
                Name = name,
                Tier = ToolCapabilityTier.ReadOnly,
                SourceKind = "open_source"
            };
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            if (registry is null) {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(new SyntheticTool(ToolPackBootstrap.NormalizePackId(Descriptor.Id)));
        }
    }

    private sealed class SyntheticTool : ITool {
        public SyntheticTool(string packId) {
            Definition = new ToolDefinition(
                name: "sample_inventory_query",
                description: "Synthetic sample tool for bootstrap/catalog contract tests.",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add(
                        "properties",
                        new JsonObject()
                            .Add("target", new JsonObject().Add("type", "string")))
                    .Add("additionalProperties", false),
                tags: new[] {
                    "pack:sample_pack_v2",
                    "domain_family:corp_internal"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = packId,
                    Role = ToolRoutingTaxonomy.RoleOperational,
                    DomainIntentFamily = "corp_internal",
                    DomainIntentActionId = "act_domain_scope_corp_internal"
                });
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            _ = arguments;
            _ = cancellationToken;
            return Task.FromResult("{}");
        }
    }

    private static string[] DisableDefaultsExcept(params string[] keepEnabledPackIds) {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keepEnabledPackIds.Length; i++) {
            var normalized = ToolPackBootstrap.NormalizePackId(keepEnabledPackIds[i]);
            if (normalized.Length > 0) {
                keep.Add(normalized);
            }
        }

        var disabled = new List<string>();
        for (var i = 0; i < DefaultEnabledKnownPackIds.Length; i++) {
            var normalized = ToolPackBootstrap.NormalizePackId(DefaultEnabledKnownPackIds[i]);
            if (normalized.Length == 0 || keep.Contains(normalized)) {
                continue;
            }

            disabled.Add(normalized);
        }

        return disabled.ToArray();
    }

    private sealed class RuntimeConfigurableOptions : IToolPackRuntimeConfigurable, IToolPackRuntimeOptionTarget {
        public string[] AppliedRoots { get; set; } = Array.Empty<string>();
        public string AppliedDomainController { get; set; } = string.Empty;
        public string AppliedSearchBaseDn { get; set; } = string.Empty;
        public int AppliedMaxResults { get; set; }
        public IReadOnlyList<string> RuntimeOptionKeys => new[] { "runtime_configurable" };

        public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
            AppliedRoots = context.AllowedRoots?.ToArray() ?? Array.Empty<string>();
            AppliedDomainController = context.AdDomainController ?? string.Empty;
            AppliedSearchBaseDn = context.AdDefaultSearchBaseDn ?? string.Empty;
            AppliedMaxResults = context.AdMaxResults;
        }
    }

    private sealed class RuntimeConfigurablePack : IToolPack {
        public ToolPackDescriptor Descriptor => new() {
            Id = "runtime_configurable",
            Name = "Runtime configurable",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "builtin"
        };

        public void Register(ToolRegistry registry) {
            throw new NotSupportedException();
        }
    }

    private static TOptions GetPackOptions<TOptions>(object pack)
        where TOptions : class {
        var field = pack.GetType().GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<TOptions>(field!.GetValue(pack));
    }

    private static void AssertToolDtoMatchesOrchestration(
        ToolDefinitionDto dto,
        ToolOrchestrationCatalogEntry entry,
        IReadOnlyDictionary<string, ToolPackAvailabilityInfo> packAvailabilityById) {
        Assert.Equal(entry.PackId, dto.PackId);
        Assert.Equal(entry.Role, dto.RoutingRole);
        Assert.Equal(entry.Scope, dto.RoutingScope);
        Assert.Equal(entry.Operation, dto.RoutingOperation);
        Assert.Equal(entry.Entity, dto.RoutingEntity);
        Assert.Equal(entry.Risk, dto.RoutingRisk);
        Assert.Equal(entry.RoutingSource, dto.RoutingSource);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.DomainIntentFamily) ? null : entry.DomainIntentFamily, dto.DomainIntentFamily);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.DomainIntentActionId) ? null : entry.DomainIntentActionId, dto.DomainIntentActionId);
        Assert.Equal(entry.IsPackInfoTool, dto.IsPackInfoTool);
        Assert.Equal(entry.IsEnvironmentDiscoverTool, dto.IsEnvironmentDiscoverTool);
        Assert.Equal(entry.IsWriteCapable, dto.IsWriteCapable);
        Assert.Equal(entry.RequiresAuthentication, dto.RequiresAuthentication);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.AuthenticationContractId) ? null : entry.AuthenticationContractId, dto.AuthenticationContractId);
        Assert.Equal(entry.AuthenticationArguments, dto.AuthenticationArguments);
        Assert.Equal(entry.SupportsConnectivityProbe, dto.SupportsConnectivityProbe);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.ProbeToolName) ? null : entry.ProbeToolName, dto.ProbeToolName);
        Assert.Equal(entry.ExecutionScope, dto.ExecutionScope);
        Assert.Equal(entry.SupportsTargetScoping, dto.SupportsTargetScoping);
        Assert.Equal(entry.TargetScopeArguments, dto.TargetScopeArguments);
        Assert.Equal(entry.SupportsRemoteHostTargeting, dto.SupportsRemoteHostTargeting);
        Assert.Equal(entry.RemoteHostArguments, dto.RemoteHostArguments);
        Assert.Equal(entry.RepresentativeExamples, dto.RepresentativeExamples);
        Assert.Equal(entry.IsSetupAware, dto.IsSetupAware);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.SetupToolName) ? null : entry.SetupToolName, dto.SetupToolName);
        Assert.Equal(entry.IsHandoffAware, dto.IsHandoffAware);
        Assert.Equal(
            entry.HandoffEdges
                .Select(static edge => edge.TargetPackId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            dto.HandoffTargetPackIds);
        Assert.Equal(
            entry.HandoffEdges
                .Select(static edge => edge.TargetToolName)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            dto.HandoffTargetToolNames);
        Assert.Equal(entry.IsRecoveryAware, dto.IsRecoveryAware);
        Assert.Equal(entry.SupportsTransientRetry, dto.SupportsTransientRetry);
        Assert.Equal(entry.MaxRetryAttempts, dto.MaxRetryAttempts);
        Assert.Equal(entry.RecoveryToolNames, dto.RecoveryToolNames);

        if (string.IsNullOrWhiteSpace(entry.PackId)) {
            Assert.Null(dto.PackName);
            Assert.Null(dto.PackDescription);
            Assert.Null(dto.PackSourceKind);
            return;
        }

        var pack = Assert.IsType<ToolPackAvailabilityInfo>(packAvailabilityById[entry.PackId]);
        Assert.Equal(pack.Name, dto.PackName);
        Assert.Equal(
            string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim(),
            dto.PackDescription);
        Assert.Equal(ResolveExpectedSourceKind(pack.SourceKind), dto.PackSourceKind);
    }

    private static ToolPackSourceKind ResolveExpectedSourceKind(string? sourceKind) {
        return ToolPackBootstrap.NormalizeSourceKind(sourceKind) switch {
            "open_source" => ToolPackSourceKind.OpenSource,
            "closed_source" => ToolPackSourceKind.ClosedSource,
            _ => ToolPackSourceKind.Builtin
        };
    }
}
