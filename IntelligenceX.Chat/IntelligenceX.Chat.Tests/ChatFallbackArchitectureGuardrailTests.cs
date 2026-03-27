using System;
using System.IO;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatFallbackArchitectureGuardrailTests {
    [Fact]
    public void ChatService_ShouldNotContainLegacyPackCapabilityFallbackSourceFiles() {
        Assert.False(File.Exists(GetServiceSourceFilePath("ChatServiceSession.PackCapabilityFallback.cs")));
    }

    [Fact]
    public void ChatService_ShouldNotReferenceLegacyPackCapabilityFallbackSymbols() {
        var repoRoot = FindRepoRoot();
        var serviceRoot = Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Service");
        var legacySymbols = new[] {
            "TryBuildPackCapabilityFallbackToolCall(",
            "RebuildPackCapabilityFallbackContracts(",
            "PackCapabilityFallbackContract",
            "AppendPackFallbackTelemetryMarker(",
            "ResolvePackFallbackTelemetryFamily(",
            "PackIdMatches("
        };

        foreach (var file in Directory.EnumerateFiles(serviceRoot, "*.cs", SearchOption.AllDirectories)) {
            var source = File.ReadAllText(file);
            for (var i = 0; i < legacySymbols.Length; i++) {
                Assert.DoesNotContain(legacySymbols[i], source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RoutingScoring_ShouldNotUsePackSuffixParsingForDeterministicFamilyKey() {
        var source = File.ReadAllText(GetServiceSourceFilePath("ChatServiceSession.ChatRouting.RoutingScoring.cs"));

        Assert.DoesNotContain("_pack_info", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_environment_discover", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolOrchestrationCatalog_ShouldNotInferRoutingFlagsFromToolNameSuffixes() {
        var source = File.ReadAllText(GetToolingSourceFilePath("ToolOrchestrationCatalog.cs"));

        Assert.DoesNotContain("_pack_info", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_environment_discover", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DomainIntentSignals_ShouldNotResolveDefinitionFamiliesViaNameInferenceFirst() {
        var source = File.ReadAllText(GetServiceSourceFilePath("ChatServiceSession.ToolRouting.DomainIntentSignals.cs"));

        Assert.DoesNotContain(
            "ToolSelectionMetadata.TryResolveDomainIntentFamily(definition, out var family)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DomainIntentHostScopeGuardrail_ShouldNotInferFamilyFromToolNameFallback() {
        var source = File.ReadAllText(GetServiceSourceFilePath("ChatServiceSession.ToolExecution.DomainScopeGuardrail.cs"));

        Assert.DoesNotContain(
            "ToolSelectionMetadata.TryResolveDomainIntentFamily(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRouting_ShouldNotInferDomainIntentFamilyFromSelectionMetadataFallback() {
        var source = File.ReadAllText(GetServiceSourceFilePath("ChatServiceSession.ToolRouting.cs"));
        const string methodStart = "private string ResolveDomainIntentFamily(string toolName)";
        const string methodEnd = "private static DomainIntentActionCatalog ResolveDomainIntentActionCatalog";
        var start = source.IndexOf(methodStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{methodStart} not found in ChatServiceSession.ToolRouting.cs");

        var end = source.IndexOf(methodEnd, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{methodEnd} not found after {methodStart} in ChatServiceSession.ToolRouting.cs");

        var methodSource = source.Substring(start, end - start);

        Assert.DoesNotContain(
            "ToolSelectionMetadata.TryResolveDomainIntentFamily(",
            methodSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoutingScoring_ShouldNotFallbackToSelectionMetadataPackInferenceForPackHints() {
        var source = File.ReadAllText(GetServiceSourceFilePath("ChatServiceSession.ChatRouting.RoutingScoring.cs"));

        Assert.DoesNotContain(
            "ToolSelectionMetadata.TryResolvePackId(definition, out var resolvedPackId)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoutingScoring_ShouldNotUseHardcodedCompoundPackTokenHeuristics() {
        var source = File.ReadAllText(GetServiceSourceFilePath("ChatServiceSession.ChatRouting.RoutingScoring.cs"));

        Assert.DoesNotContain(
            "ToolSelectionMetadata.IsKnownCompoundPackRoutingCompact(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChatHost_ShouldNotContainHardcodedToolSpecificRetryRewrites() {
        var source = File.ReadAllText(GetHostSourceFilePath("Program.Session.ToolExecution.cs"));
        var legacySymbols = new[] {
            "ApplyAdDiscoveryRootDseFallback(",
            "ApplyAdReplicationProbeFallback(",
            "ApplyDomainDetectiveSummaryTimeoutFallback("
        };

        for (var i = 0; i < legacySymbols.Length; i++) {
            Assert.DoesNotContain(legacySymbols[i], source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChatHost_ShouldNotContainPluginManifestContractCoupling() {
        var repoRoot = FindRepoRoot();
        var hostRoot = Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Host");
        var disallowedTokens = new[] {
            "ix-plugin.json",
            ".ix-plugin.zip",
            "PluginFolderToolPackLoader"
        };

        foreach (var file in Directory.EnumerateFiles(hostRoot, "*.cs", SearchOption.AllDirectories)) {
            var source = File.ReadAllText(file);
            for (var i = 0; i < disallowedTokens.Length; i++) {
                Assert.DoesNotContain(disallowedTokens[i], source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ToolingDiscovery_ShouldNotContainHardcodedBuiltInAssemblyAllowlistNames() {
        var source = File.ReadAllText(GetToolingSourceFilePath("ToolPackBootstrap.RegistryAndReflection.cs"));
        var disallowedAssemblyNames = new[] {
            "IntelligenceX.Tools.FileSystem",
            "IntelligenceX.Tools.EventLog",
            "IntelligenceX.Tools.ADPlayground",
            "IntelligenceX.Tools.System",
            "IntelligenceX.Tools.PowerShell",
            "IntelligenceX.Tools.TestimoX",
            "IntelligenceX.Tools.OfficeIMO",
            "IntelligenceX.Tools.Email",
            "IntelligenceX.Tools.ReviewerSetup",
            "IntelligenceX.Tools.DnsClientX",
            "IntelligenceX.Tools.DomainDetective"
        };

        for (var i = 0; i < disallowedAssemblyNames.Length; i++) {
            Assert.DoesNotContain(disallowedAssemblyNames[i], source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ChatToolingProject_ShouldOnlyReferenceSharedToolContracts_NotBuiltInToolPackProjects() {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IntelligenceX.Chat",
            "IntelligenceX.Chat.Tooling",
            "IntelligenceX.Chat.Tooling.csproj"));

        Assert.Contains("IntelligenceX.Tools.Common", source, StringComparison.OrdinalIgnoreCase);

        var disallowedPackProjectTokens = new[] {
            "IntelligenceX.Tools.ADPlayground",
            "IntelligenceX.Tools.DomainDetective",
            "IntelligenceX.Tools.DnsClientX",
            "IntelligenceX.Tools.Email",
            "IntelligenceX.Tools.EventLog",
            "IntelligenceX.Tools.FileSystem",
            "IntelligenceX.Tools.OfficeIMO",
            "IntelligenceX.Tools.PowerShell",
            "IntelligenceX.Tools.ReviewerSetup",
            "IntelligenceX.Tools.System",
            "IntelligenceX.Tools.TestimoX"
        };

        for (var i = 0; i < disallowedPackProjectTokens.Length; i++) {
            Assert.DoesNotContain(disallowedPackProjectTokens[i], source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ChatHostProject_ShouldBundleExpectedBuiltInToolPackProjects_ForPublishableRuntime() {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IntelligenceX.Chat",
            "IntelligenceX.Chat.Host",
            "IntelligenceX.Chat.Host.csproj"));

        var bundledBuiltInPackProjectTokens = new[] {
            "IntelligenceX.Tools.DnsClientX",
            "IntelligenceX.Tools.DomainDetective",
            "IntelligenceX.Tools.Email",
            "IntelligenceX.Tools.EventLog",
            "IntelligenceX.Tools.FileSystem",
            "IntelligenceX.Tools.OfficeIMO",
            "IntelligenceX.Tools.PowerShell",
            "IntelligenceX.Tools.ReviewerSetup"
        };

        for (var i = 0; i < bundledBuiltInPackProjectTokens.Length; i++) {
            Assert.Contains(bundledBuiltInPackProjectTokens[i], source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Condition=\"'$(IncludePrivateToolPacks)' == 'true'\"", source, StringComparison.Ordinal);

        var privatePackProjectTokens = new[] {
            "IntelligenceX.Tools.System",
            "IntelligenceX.Tools.ADPlayground",
            "IntelligenceX.Tools.TestimoX",
            "IntelligenceX.Tools.TestimoX.Analytics"
        };

        for (var i = 0; i < privatePackProjectTokens.Length; i++) {
            Assert.Contains(privatePackProjectTokens[i], source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ChatHostBuildOutput_ShouldContainExpectedBundledToolAssemblies_AndPrivateCompanionsWhenPresent() {
        var hostOutputDirectory = ResolveHostOutputDirectory();

        Assert.True(Directory.Exists(hostOutputDirectory), $"Expected host output directory '{hostOutputDirectory}' to exist.");
        Assert.True(File.Exists(Path.Combine(hostOutputDirectory, "IntelligenceX.Chat.Host.dll")));

        var bundledToolAssemblies = new[] {
            "IntelligenceX.Tools.DnsClientX.dll",
            "IntelligenceX.Tools.DomainDetective.dll",
            "IntelligenceX.Tools.Email.dll",
            "IntelligenceX.Tools.EventLog.dll",
            "IntelligenceX.Tools.FileSystem.dll",
            "IntelligenceX.Tools.OfficeIMO.dll",
            "IntelligenceX.Tools.PowerShell.dll",
            "IntelligenceX.Tools.ReviewerSetup.dll"
        };

        for (var i = 0; i < bundledToolAssemblies.Length; i++) {
            Assert.True(
                File.Exists(Path.Combine(hostOutputDirectory, bundledToolAssemblies[i])),
                $"Expected bundled host output assembly '{bundledToolAssemblies[i]}' in '{hostOutputDirectory}'.");
        }

        Assert.True(
            File.Exists(Path.Combine(hostOutputDirectory, "System.Diagnostics.EventLog.dll")),
            "Expected EventLog runtime dependency 'System.Diagnostics.EventLog.dll' to be copied beside the host output.");

        var privateToolAssemblies = new[] {
            "IntelligenceX.Tools.System.dll",
            "IntelligenceX.Tools.ADPlayground.dll",
            "IntelligenceX.Tools.TestimoX.dll",
            "IntelligenceX.Tools.TestimoX.Analytics.dll"
        };

        var anyPrivateToolAssemblyPresent = false;
        for (var i = 0; i < privateToolAssemblies.Length; i++) {
            if (File.Exists(Path.Combine(hostOutputDirectory, privateToolAssemblies[i]))) {
                anyPrivateToolAssemblyPresent = true;
                break;
            }
        }

        if (!anyPrivateToolAssemblyPresent) {
            return;
        }

        for (var i = 0; i < privateToolAssemblies.Length; i++) {
            Assert.True(
                File.Exists(Path.Combine(hostOutputDirectory, privateToolAssemblies[i])),
                $"Expected private host output assembly '{privateToolAssemblies[i]}' when private pack bundling is enabled.");
        }

        Assert.True(
            File.Exists(Path.Combine(hostOutputDirectory, "ADPlayground.Monitoring.dll")),
            "Expected private ADPlayground companion dependency 'ADPlayground.Monitoring.dll' when private pack bundling is enabled.");
    }

    [Fact]
    public void ChatRuntimeSurface_ShouldNotHardcodePackIdsInAppOrService() {
        var repoRoot = FindRepoRoot();
        var roots = new[] {
            Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.App"),
            Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Service")
        };
        var disallowedPackTokens = new[] {
            "testimox",
            "active_directory",
            "adplayground",
            "domaindetective",
            "dnsclientx",
            "reviewer_setup"
        };

        for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++) {
            var root = roots[rootIndex];
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)) {
                var source = File.ReadAllText(file);
                for (var tokenIndex = 0; tokenIndex < disallowedPackTokens.Length; tokenIndex++) {
                    Assert.DoesNotContain(disallowedPackTokens[tokenIndex], source, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    private static string GetServiceSourceFilePath(string fileName) {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Service", fileName);
    }

    private static string GetHostSourceFilePath(string fileName) {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Host", fileName);
    }

    private static string GetToolingSourceFilePath(string fileName) {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Tooling", fileName);
    }

    private static string ResolveHostOutputDirectory() {
        var repoRoot = FindRepoRoot();
        var buildConfiguration = ResolveCurrentBuildConfiguration();
        return Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Host", "bin", buildConfiguration, "net10.0-windows");
    }

    private static string ResolveCurrentBuildConfiguration() {
        var fullPath = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathParts = fullPath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < pathParts.Length - 1; i++) {
            if (string.Equals(pathParts[i], "bin", StringComparison.OrdinalIgnoreCase)) {
                return pathParts[i + 1];
            }
        }

        return "Release";
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "IntelligenceX.sln"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate IntelligenceX repository root.");
    }
}
