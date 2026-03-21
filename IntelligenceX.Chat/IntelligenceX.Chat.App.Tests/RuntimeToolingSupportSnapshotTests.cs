using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies support/export tooling uses the same registration-first pack and plugin snapshot.
/// </summary>
public sealed class RuntimeToolingSupportSnapshotTests {
    /// <summary>
    /// Verifies session policy wins over tool-catalog preview when both are present.
    /// </summary>
    [Fact]
    public void Build_PrefersSessionPolicyMetadataOverToolCatalogPreview() {
        var snapshot = RuntimeToolingSupportSnapshotBuilder.Build(
            new SessionPolicyDto {
                ReadOnly = false,
                DangerousToolsEnabled = true,
                MaxToolRounds = 4,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto {
                        Id = "legacy_pack",
                        Name = "Legacy Pack",
                        Tier = CapabilityTier.ReadOnly,
                        Enabled = true,
                        IsDangerous = false
                    }
                },
                Plugins = new[] {
                    new PluginInfoDto {
                        Id = "legacy_plugin",
                        Name = "Legacy Plugin",
                        Enabled = true,
                        DefaultEnabled = true,
                        Origin = "legacy",
                        IsDangerous = false
                    }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 2,
                    EnabledPackCount = 1,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
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
                                SourceKind = ToolPackSourceKind.Builtin,
                                EngineId = "windows_eventing",
                                CapabilityTags = new[] { "events", "host_diagnostics" }
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
                                IsDangerous = true,
                                Version = "1.2.3",
                                RootPath = @"C:\plugins\ops-bundle",
                                PackIds = new[] { "eventlog" },
                                SkillIds = new[] { "event-triage" }
                            }
                        }
                    }
                }
            },
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
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
            });

        Assert.NotNull(snapshot);
        Assert.Equal("session_policy", snapshot!.Source);
        Assert.Single(snapshot.Packs);
        Assert.Single(snapshot.Plugins);
        Assert.Equal("eventlog", snapshot.Packs[0].Id);
        Assert.Equal("ops_bundle", snapshot.Plugins[0].Id);
    }

    /// <summary>
    /// Verifies tool-catalog capability snapshot tooling metadata is used when preview arrays are sparse.
    /// </summary>
    [Fact]
    public void Build_FallsBackToToolCatalogCapabilitySnapshotToolingWhenPreviewArraysMissing() {
        var snapshot = RuntimeToolingSupportSnapshotBuilder.Build(
            sessionPolicy: null,
            toolCatalogPacks: Array.Empty<ToolPackInfoDto>(),
            toolCatalogPlugins: Array.Empty<PluginInfoDto>(),
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 1,
                EnabledPluginCount = 1,
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

        Assert.NotNull(snapshot);
        Assert.Equal("tool_catalog_preview", snapshot!.Source);
        Assert.Single(snapshot.Packs);
        Assert.Single(snapshot.Plugins);
        Assert.Equal("eventlog", snapshot.Packs[0].Id);
        Assert.Equal("ops_bundle", snapshot.Plugins[0].Id);
    }

    /// <summary>
    /// Verifies copied startup diagnostics include runtime tooling provenance when available.
    /// </summary>
    [Fact]
    public void BuildClipboardText_AppendsRuntimeToolingSnapshot() {
        var snapshot = new RuntimeToolingSupportSnapshot {
            Source = "tool_catalog_preview",
            PackCount = 1,
            PluginCount = 1,
            Packs = new List<RuntimeToolingPackSnapshot> {
                new() {
                    Id = "eventlog",
                    Name = "Event Viewer",
                    Enabled = true,
                    SourceKind = "builtin",
                    EngineId = "windows_eventing",
                    CapabilityTags = new List<string> { "events" }
                }
            },
            Plugins = new List<RuntimeToolingPluginSnapshot> {
                new() {
                    Id = "ops_bundle",
                    Name = "Ops Bundle",
                    Enabled = true,
                    DefaultEnabled = true,
                    Origin = "plugin_folder",
                    SourceKind = "closed_source",
                    Version = "1.2.3",
                    RootPath = @"C:\plugins\ops-bundle",
                    PackIds = new List<string> { "eventlog", "system" },
                    SkillIds = new List<string> { "event-triage" }
                }
            }
        };

        var text = RuntimeToolingSupportSnapshotBuilder.BuildClipboardText("startup line", snapshot);

        Assert.Contains("startup line", text, StringComparison.Ordinal);
        Assert.Contains("==== Runtime Tooling Snapshot ====", text, StringComparison.Ordinal);
        Assert.Contains("source: tool_catalog_preview", text, StringComparison.Ordinal);
        Assert.Contains("- Event Viewer [eventlog]: enabled, source=builtin, engine=windows_eventing, capabilities=events", text, StringComparison.Ordinal);
        Assert.Contains("- Ops Bundle [ops_bundle]: enabled, default=enabled, origin=plugin_folder, source=closed_source, version=1.2.3, root=C:\\plugins\\ops-bundle, packs=eventlog/system, skills=event-triage", text, StringComparison.Ordinal);
    }
}
