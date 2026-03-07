using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for human-facing capability self-knowledge derived from session policy.
/// </summary>
public sealed class MainWindowCapabilitySelfKnowledgeTests {
    /// <summary>
    /// Ensures capability self-knowledge summarizes enabled packs and key routing families in human terms.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_SummarizesEnabledCapabilities() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(new SessionPolicyDto {
            ReadOnly = true,
            DangerousToolsEnabled = false,
            MaxToolRounds = 24,
            ParallelTools = true,
            AllowMutatingParallelToolCalls = false,
            Packs = new[] {
                new ToolPackInfoDto { Id = "adplayground", Name = "Active Directory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "eventlog", Name = "Event Viewer", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "dnsclientx", Name = "DnsClientX", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                RegisteredTools = 12,
                EnabledPackCount = 2,
                PluginCount = 2,
                EnabledPluginCount = 2,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                FamilyActions = new[] {
                    new SessionRoutingFamilyActionSummaryDto { Family = "ad_domain", ActionId = "act_ad", ToolCount = 5 }
                },
                HealthyTools = new[] { "ad_search", "eventlog_live_query" },
                RemoteReachabilityMode = "remote_capable"
            }
        });

        Assert.Contains(lines, line => line.Contains("Active Directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Active Directory checks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("public-domain signals", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("live session tools", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Recently healthy tool count", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("remote-capable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("few practical examples", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures runtime-introspection mode avoids generic capability-answer tail guidance.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeIntrospectionMode_UsesModeSpecificTailGuidance() {
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
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            },
            runtimeIntrospectionMode: true);

        Assert.Contains(lines, line => line.Contains("only the live tooling or capability areas", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("runtime capability handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Active Directory checks", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("invite the user's task", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures operational status lines are prioritized ahead of category/example filler so budgeting cannot hide them later.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_PrioritizesToolingAndReachabilityStatus() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "adplayground", Name = "Active Directory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "eventlog", Name = "Event Viewer", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "dnsclientx", Name = "DnsClientX", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 12,
                    EnabledPackCount = 4,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = false,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            });

        var toolingIndex = FindLineIndex(lines, "Tooling is not currently available");
        var reachabilityIndex = FindLineIndex(lines, "Remote reachability right now is local-only.");
        var exampleIndex = FindLineIndex(lines, "Concrete examples you can mention");

        Assert.True(toolingIndex >= 0);
        Assert.True(reachabilityIndex >= 0);
        Assert.True(exampleIndex >= 0);
        Assert.True(toolingIndex < exampleIndex);
        Assert.True(reachabilityIndex < exampleIndex);
    }

    /// <summary>
    /// Ensures capability self-knowledge stays cautious when session policy/tooling data is not ready yet.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_StaysCautiousWithoutPolicy() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(sessionPolicy: null);

        Assert.Contains(lines, line => line.Contains("still loading", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("invite the task", StringComparison.OrdinalIgnoreCase));
    }

    private static int FindLineIndex(IReadOnlyList<string> lines, string expectedFragment) {
        for (var i = 0; i < lines.Count; i++) {
            if (lines[i].Contains(expectedFragment, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }
}
