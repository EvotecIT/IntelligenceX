using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Protects the native shell's runtime-readiness labels.
/// </summary>
public sealed class NativeRuntimeStatusFormatterTests {
    /// <summary>
    /// Keeps the generic label when the service does not expose a capability snapshot.
    /// </summary>
    [Fact]
    public void FormatReady_WithoutCapabilitySnapshot_PreservesGenericReadyState() {
        Assert.Equal("Ready", NativeRuntimeStatusFormatter.FormatReady(null));
    }

    /// <summary>
    /// Makes an empty tool registry visible instead of presenting it as fully ready.
    /// </summary>
    [Fact]
    public void FormatReady_WithoutRegisteredTools_DoesNotClaimFullReadiness() {
        var policy = CreatePolicy(new SessionCapabilitySnapshotDto {
            RegisteredTools = 0,
            EnabledPackCount = 0,
            PluginCount = 0,
            EnabledPluginCount = 0,
            ToolingAvailable = false,
            AllowedRootCount = 0
        });

        Assert.Equal("No tools loaded", NativeRuntimeStatusFormatter.FormatReady(policy));
    }

    /// <summary>
    /// Surfaces the shared runtime's enabled-pack count without teaching the shell pack identities.
    /// </summary>
    [Fact]
    public void FormatReady_WithEnabledPacks_SurfacesSharedPackCount() {
        var policy = CreatePolicy(new SessionCapabilitySnapshotDto {
            RegisteredTools = 12,
            EnabledPackCount = 2,
            PluginCount = 2,
            EnabledPluginCount = 2,
            ToolingAvailable = true,
            AllowedRootCount = 0,
            EnabledPackIds = new[] { "pack-a", "pack-b" }
        });

        Assert.Equal("Ready · 2 tool packs", NativeRuntimeStatusFormatter.FormatReady(policy));
    }

    private static SessionPolicyDto CreatePolicy(SessionCapabilitySnapshotDto capability) =>
        new() {
            ReadOnly = true,
            DangerousToolsEnabled = false,
            MaxToolRounds = 8,
            ParallelTools = true,
            AllowMutatingParallelToolCalls = false,
            AllowedRoots = System.Array.Empty<string>(),
            CapabilitySnapshot = capability
        };
}
