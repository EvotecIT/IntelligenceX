using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
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
        Assert.False(options.EnableDefaultPluginPaths);
        Assert.Equal(new[] { "C:/plugins/a", "C:/plugins/b" }, options.PluginPaths);
        Assert.Equal(new[] { "officeimo", "testimox", "dnsclientx", "domaindetective" }, options.DisabledPackIds);
        Assert.Equal(new[] { "powershell", "plugin-loader-test" }, options.EnabledPackIds);
        Assert.Same(runtimePolicyContext.AuthenticationProbeStore, options.AuthenticationProbeStore);
        Assert.True(options.RequireSuccessfulSmtpProbeForSend);
        Assert.Equal(600, options.SmtpProbeMaxAgeSeconds);
        Assert.Equal(runtimePolicyContext.Options.RunAsProfilePath, options.RunAsProfilePath);
        Assert.Equal(runtimePolicyContext.Options.AuthenticationProfilePath, options.AuthenticationProfilePath);

        Assert.True(options.PackRuntimeOptionBag.TryGetValue("*", out var globalBag));
        Assert.NotNull(globalBag);
        Assert.True(globalBag!.TryGetValue("AllowedRoots", out var allowedRootsValue));
        var allowedRootsBag = Assert.IsAssignableFrom<IEnumerable<string>>(allowedRootsValue);
        Assert.Equal(new[] { "C:/allowed-a", "C:/allowed-b" }, allowedRootsBag);
        Assert.Equal(runtimePolicyContext.Options.RunAsProfilePath, globalBag["RunAsProfilePath"]);
        Assert.Equal(runtimePolicyContext.Options.AuthenticationProfilePath, globalBag["AuthenticationProfilePath"]);
        Assert.Same(runtimePolicyContext.AuthenticationProbeStore, globalBag["AuthenticationProbeStore"]);
        Assert.True(Assert.IsType<bool>(globalBag["RequireSuccessfulSmtpProbeForSend"]));
        Assert.Equal(600, Assert.IsType<int>(globalBag["SmtpProbeMaxAgeSeconds"]));

        Assert.True(options.PackRuntimeOptionBag.TryGetValue("active_directory", out var adBag));
        Assert.NotNull(adBag);
        Assert.Equal("dc.contoso.local", adBag!["DomainController"]);
        Assert.Equal("DC=contoso,DC=local", adBag["DefaultSearchBaseDn"]);
        Assert.Equal(2222, Assert.IsType<int>(adBag["MaxResults"]));
        Assert.False(options.PackRuntimeOptionBag.ContainsKey("adplayground"));

        Assert.True(options.PackRuntimeOptionBag.TryGetValue("powershell", out var powershellBag));
        Assert.NotNull(powershellBag);
        Assert.True(Assert.IsType<bool>(powershellBag!["AllowWrite"]));

        Assert.True(options.PackRuntimeOptionBag.TryGetValue("email", out var emailBag));
        Assert.NotNull(emailBag);
        Assert.Same(runtimePolicyContext.AuthenticationProbeStore, emailBag!["AuthenticationProbeStore"]);
        Assert.True(Assert.IsType<bool>(emailBag["RequireSuccessfulSmtpProbeForSend"]));
        Assert.Equal(600, Assert.IsType<int>(emailBag["SmtpProbeMaxAgeSeconds"]));
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

        method!.Invoke(null, new object[] {
            options,
            bootstrapOptions,
            typeof(RuntimeConfigurablePack)
        });

        Assert.Equal(new[] { "C:/runtime-a", "C:/runtime-b" }, options.AppliedRoots);
        Assert.Equal("dc01.contoso.local", options.AppliedDomainController);
        Assert.Equal("DC=contoso,DC=local", options.AppliedSearchBaseDn);
        Assert.Equal(4096, options.AppliedMaxResults);
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
        var invocationArguments = new object?[] { assemblyName, null };
        var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
        var trustedAssemblyPath = Assert.IsType<string>(invocationArguments[1]);

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
        var invocationArguments = new object?[] { new AssemblyName(), null };
        var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
        var trustedAssemblyPath = invocationArguments[1] as string;

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
            var invocationArguments = new object?[] { assemblyName, null };
            var resolved = Assert.IsType<bool>(resolvePathMethod!.Invoke(null, invocationArguments));
            var trustedAssemblyPath = invocationArguments[1] as string;
            Assert.True(resolved, $"Failed to resolve trusted path for '{assemblyName.Name}'.");
            Assert.False(string.IsNullOrWhiteSpace(trustedAssemblyPath));
            Assert.True(File.Exists(trustedAssemblyPath), $"Resolved path '{trustedAssemblyPath}' for '{assemblyName.Name}' does not exist.");
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

        Assert.Contains("*", keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("synthetic_runtime_target", keys, StringComparer.OrdinalIgnoreCase);
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
    public void CreateDefaultReadOnlyPacks_DisabledPackIds_TakesPrecedenceOverEnabledPackIds() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            DisabledPackIds = DefaultEnabledKnownPackIds,
            EnableDefaultPluginPaths = false,
            EnabledPackIds = new[] { "officeimo" }
        });

        Assert.DoesNotContain(packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
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

    private sealed class TestPackRuntimeSettings : IToolPackRuntimeSettings {
        public IReadOnlyList<string> AllowedRoots { get; init; } = Array.Empty<string>();
        public string? AdDomainController { get; init; }
        public string? AdDefaultSearchBaseDn { get; init; }
        public int AdMaxResults { get; init; } = 1000;
        public bool PowerShellAllowWrite { get; init; }
        public bool EnableBuiltInPackLoading { get; init; } = true;
        public bool UseDefaultBuiltInToolAssemblyNames { get; init; } = true;
        public IReadOnlyList<string> BuiltInToolAssemblyNames { get; init; } = Array.Empty<string>();
        public bool EnableDefaultPluginPaths { get; init; } = true;
        public IReadOnlyList<string> PluginPaths { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DisabledPackIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> EnabledPackIds { get; init; } = Array.Empty<string>();
    }

    private sealed class SyntheticRuntimeOptionTarget : IToolPackRuntimeOptionTarget {
        public IReadOnlyList<string> RuntimeOptionKeys => new[] { "synthetic_runtime_target" };
    }

    private sealed class TestPack : IToolPack {
        public TestPack(string id, string name) {
            Descriptor = new ToolPackDescriptor {
                Id = id,
                Name = name,
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

    private sealed class RuntimeConfigurableOptions : IToolPackRuntimeConfigurable {
        public string[] AppliedRoots { get; private set; } = Array.Empty<string>();
        public string AppliedDomainController { get; private set; } = string.Empty;
        public string AppliedSearchBaseDn { get; private set; } = string.Empty;
        public int AppliedMaxResults { get; private set; }

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
}
