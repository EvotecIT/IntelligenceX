using System;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies app-side tooling metadata readers prefer the shared nested tooling snapshot over legacy arrays.
/// </summary>
public sealed class RuntimeToolingMetadataResolverTests {
    /// <summary>
    /// Ensures session capability-snapshot pack provenance wins over older policy and preview arrays.
    /// </summary>
    [Fact]
    public void ResolveEffectivePacks_PrefersSessionToolingSnapshot() {
        var packs = RuntimeToolingMetadataResolver.ResolveEffectivePacks(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 4,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto {
                        Id = "legacy_pack",
                        Name = "Legacy Pack",
                        Tier = CapabilityTier.ReadOnly,
                        Enabled = false,
                        IsDangerous = false
                    }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                        Source = "service_runtime",
                        Packs = new[] {
                            new ToolPackInfoDto {
                                Id = "eventlog",
                                Name = "Event Viewer",
                                Tier = CapabilityTier.ReadOnly,
                                Enabled = true,
                                IsDangerous = false,
                                EngineId = "windows_eventing"
                            }
                        },
                        Plugins = Array.Empty<PluginInfoDto>()
                    }
                }
            },
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "preview_pack",
                    Name = "Preview Pack",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                    Source = "preview_snapshot",
                    Packs = new[] {
                        new ToolPackInfoDto {
                            Id = "preview_snapshot_pack",
                            Name = "Preview Snapshot Pack",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false
                        }
                    },
                    Plugins = Array.Empty<PluginInfoDto>()
                }
            });

        var pack = Assert.Single(packs);
        Assert.Equal("eventlog", pack.Id);
        Assert.True(pack.Enabled);
    }

    /// <summary>
    /// Ensures session capability-snapshot plugin provenance wins over older policy and preview arrays.
    /// </summary>
    [Fact]
    public void ResolveEffectivePlugins_PrefersSessionToolingSnapshot() {
        var plugins = RuntimeToolingMetadataResolver.ResolveEffectivePlugins(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 4,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Plugins = new[] {
                    new PluginInfoDto {
                        Id = "legacy_plugin",
                        Name = "Legacy Plugin",
                        Enabled = false,
                        DefaultEnabled = false,
                        Origin = "legacy",
                        IsDangerous = false
                    }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 0,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                        Source = "service_runtime",
                        Packs = Array.Empty<ToolPackInfoDto>(),
                        Plugins = new[] {
                            new PluginInfoDto {
                                Id = "ops_bundle",
                                Name = "Ops Bundle",
                                Enabled = true,
                                DefaultEnabled = true,
                                Origin = "plugin_folder",
                                SourceKind = ToolPackSourceKind.ClosedSource,
                                IsDangerous = false,
                                PackIds = new[] { "eventlog" }
                            }
                        }
                    }
                }
            },
            toolCatalogPlugins: new[] {
                new PluginInfoDto {
                    Id = "preview_plugin",
                    Name = "Preview Plugin",
                    Enabled = true,
                    DefaultEnabled = false,
                    Origin = "preview",
                    IsDangerous = false
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 0,
                PluginCount = 1,
                EnabledPluginCount = 1,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                    Source = "preview_snapshot",
                    Packs = Array.Empty<ToolPackInfoDto>(),
                    Plugins = new[] {
                        new PluginInfoDto {
                            Id = "preview_snapshot_plugin",
                            Name = "Preview Snapshot Plugin",
                            Enabled = true,
                            DefaultEnabled = true,
                            Origin = "preview_snapshot",
                            IsDangerous = false
                        }
                    }
                }
            });

        var plugin = Assert.Single(plugins);
        Assert.Equal("ops_bundle", plugin.Id);
        Assert.True(plugin.Enabled);
    }

    /// <summary>
    /// Ensures the app publishes nested tooling provenance inside capability-snapshot JSON for the shell fallback path.
    /// </summary>
    [Fact]
    public void BuildCapabilitySnapshotState_IncludesToolingSnapshotProjection() {
        var state = MainWindow.BuildCapabilitySnapshotState(
            new SessionCapabilitySnapshotDto {
                RegisteredTools = 2,
                EnabledPackCount = 1,
                PluginCount = 1,
                EnabledPluginCount = 1,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                    Source = "service_runtime",
                    Packs = new[] {
                        new ToolPackInfoDto {
                            Id = "eventlog",
                            Name = "Event Viewer",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.Builtin
                        }
                    },
                    Plugins = new[] {
                        new PluginInfoDto {
                            Id = "ops_bundle",
                            Name = "Ops Bundle",
                            Enabled = true,
                            DefaultEnabled = true,
                            Origin = "plugin_folder",
                            SourceKind = ToolPackSourceKind.ClosedSource,
                            IsDangerous = false,
                            PackIds = new[] { "eventlog" }
                        }
                    }
                }
            });

        var json = JsonSerializer.Serialize(state);

        Assert.Contains("\"toolingSnapshot\":", json, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"service_runtime\"", json, StringComparison.Ordinal);
        Assert.Contains("\"packs\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"plugins\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"eventlog\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"ops_bundle\"", json, StringComparison.Ordinal);
    }
}
