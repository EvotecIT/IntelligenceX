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

    private static string GetServiceSourceFilePath(string fileName) {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.Service", fileName);
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
