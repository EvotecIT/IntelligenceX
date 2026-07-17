using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the shared desktop runtime identity authority rules.
/// </summary>
public sealed class DesktopRuntimeIdentityResolverTests {
    /// <summary>
    /// Ensures app-owned settings remain authoritative when the operator applied runtime overrides.
    /// </summary>
    [Fact]
    public void Resolve_AppOwnedRuntimeUsesAppIdentity() {
        var identity = DesktopRuntimeIdentityResolver.Resolve(
            appRuntimeOverridesActive: true,
            appTransport: "compatible-http",
            requestModel: "app-model",
            servicePolicy: CreatePolicy("native", "service-model"));

        Assert.Equal("compatible-http", identity.TransportLabel);
        Assert.Equal("app-model", identity.ModelLabel);
    }

    /// <summary>
    /// Ensures a per-conversation model wins without hiding the load-only service transport.
    /// </summary>
    [Fact]
    public void Resolve_LoadOnlyRuntimeHonorsConversationModelOverride() {
        var identity = DesktopRuntimeIdentityResolver.Resolve(
            appRuntimeOverridesActive: false,
            appTransport: "native",
            requestModel: "conversation-model",
            servicePolicy: CreatePolicy("compatible-http", "service-model"));

        Assert.Equal("compatible-http", identity.TransportLabel);
        Assert.Equal("conversation-model", identity.ModelLabel);
    }

    /// <summary>
    /// Ensures both desktop shells summarize tooling from service metadata instead of app provider guesses.
    /// </summary>
    [Fact]
    public void ResolveTooling_UsesLiveSessionCapabilityMetadata() {
        var policy = CreatePolicy("compatible-http", "service-model") with {
            Packs = [
                new ToolPackInfoDto {
                    Id = "active-directory",
                    Name = "Active Directory",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            ],
            CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                ToolingAvailable = true,
                RegisteredTools = 42,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                AllowedRootCount = 0
            }
        };

        var summary = DesktopRuntimeToolingSummaryResolver.Resolve(policy);

        Assert.True(summary.HasMetadata);
        Assert.Equal(1, summary.EnabledPacks);
        Assert.Contains("registered tools: 42", summary.DetailedAvailability, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_packs=1", summary.CompactAvailability, StringComparison.OrdinalIgnoreCase);
    }

    private static SessionPolicyDto CreatePolicy(string transport, string model) {
        return new SessionPolicyDto {
            ReadOnly = true,
            DangerousToolsEnabled = false,
            MaxToolRounds = 12,
            ParallelTools = true,
            AllowMutatingParallelToolCalls = false,
            RuntimeIdentity = new SessionRuntimeIdentityDto {
                Transport = transport,
                Model = model
            }
        };
    }
}
