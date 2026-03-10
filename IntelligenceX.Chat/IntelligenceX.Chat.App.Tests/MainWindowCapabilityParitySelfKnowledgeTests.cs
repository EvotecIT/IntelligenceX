using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for capability self-knowledge lines that surface runtime parity cautions.
/// </summary>
public sealed class MainWindowCapabilityParitySelfKnowledgeTests {
    /// <summary>
    /// Ensures human-facing capability guidance stays honest when parity gaps still exist.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_WarnsAboutParityGapsWhenPresent() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 4,
                    EnabledPackCount = 1,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "remote_capable",
                    ParityAttentionCount = 1,
                    ParityMissingCapabilityCount = 2
                }
            });

        Assert.Contains(lines, line => line.Contains("upstream read-only capability gaps", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("do not promise them as live tools yet", StringComparison.OrdinalIgnoreCase));
    }
}
