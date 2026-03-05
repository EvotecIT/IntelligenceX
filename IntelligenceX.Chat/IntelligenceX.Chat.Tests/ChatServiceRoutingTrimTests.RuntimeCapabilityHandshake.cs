using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void RuntimeCapabilityHandshake_IncludesCapabilitySnapshotWhenRuntimeMetadataPresent() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var nowTicks = DateTime.UtcNow.Ticks;
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "eventlog",
                    Name = "Event Log",
                    SourceKind = "builtin",
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "disabled-pack",
                    Name = "Disabled",
                    SourceKind = "builtin",
                    Enabled = false
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 17,
                RoutingAwareTools = 12,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 5
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 4
                    }
                }
            });
        session.SetToolRoutingStatsForTesting(new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> {
            ["ad_replication_summary"] = (nowTicks, nowTicks),
            ["eventlog_live_query"] = (nowTicks, nowTicks)
        });

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting(
            resolvedModel: "gpt-5",
            baseInstructions: "Base instructions");
        var instructionsText = Assert.IsType<string>(instructions);

        Assert.StartsWith("Base instructions", instructionsText);
        Assert.Contains("ix:runtime-identity:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:capability-snapshot:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:skills:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools: 17", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_packs: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routing_families: ad_domain, public_domain", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills: ad_domain.scope_hosts, public_domain.query_whois", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("healthy_tools:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_replication_summary", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_live_query", instructionsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_ProducesStructuredCapabilityArtifact() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var nowTicks = DateTime.UtcNow.Ticks;
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 9,
                RoutingAwareTools = 9,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 2
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 1
                    }
                }
            });
        session.SetToolRoutingStatsForTesting(new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> {
            ["ad_replication_summary"] = (nowTicks, nowTicks)
        });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.True(snapshot.ToolingAvailable);
        Assert.Equal(9, snapshot.RegisteredTools);
        Assert.Equal(1, snapshot.EnabledPackCount);
        Assert.Equal("active_directory", Assert.Single(snapshot.EnabledPackIds));
        Assert.Equal(2, snapshot.RoutingFamilies.Length);
        Assert.Equal(2, snapshot.FamilyActions.Length);
        Assert.Equal("ad_domain.scope_hosts", snapshot.Skills[0]);
        Assert.Equal("ad_replication_summary", Assert.Single(snapshot.HealthyTools));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_ReportsZeroToolingWhenNoPacksLoaded() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5");
        var instructionsText = Assert.IsType<string>(instructions);

        Assert.Contains("ix:runtime-identity:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:capability-snapshot:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:skills:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enabled_packs:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Null(TryReadInstructionLine(instructionsText, "skills:"));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BoundsCapabilityLists() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var nowTicks = DateTime.UtcNow.Ticks;
        session.SetCapabilitySnapshotContextForTesting(
            Enumerable.Range(1, 20)
                .Select(index => new ToolPackAvailabilityInfo {
                    Id = $"Pack_{index:00}",
                    Name = $"Pack {index:00}",
                    SourceKind = "builtin",
                    Enabled = true
                })
                .ToArray(),
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 120,
                RoutingAwareTools = 120,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 8,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Enumerable.Range(1, 8)
                    .Select(index => new ToolRoutingFamilyActionSummary {
                        Family = $"custom_{index:00}_domain",
                        ActionId = $"action_{index:00}",
                        ToolCount = 1
                    })
                    .ToArray()
            });
        session.SetToolRoutingStatsForTesting(
            Enumerable.Range(1, 20)
                .ToDictionary(
                    index => $"tool_{index:00}",
                    index => (LastUsedUtcTicks: nowTicks - index, LastSuccessUtcTicks: nowTicks - index)));

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5");
        var instructionsText = Assert.IsType<string>(instructions);

        var enabledPackLine = TryReadInstructionLine(instructionsText, "enabled_packs:");
        var healthyToolsLine = TryReadInstructionLine(instructionsText, "healthy_tools:");
        var routingFamiliesLine = TryReadInstructionLine(instructionsText, "routing_families:");
        var skillsLine = TryReadInstructionLine(instructionsText, "skills:");
        Assert.NotNull(enabledPackLine);
        Assert.NotNull(healthyToolsLine);
        Assert.NotNull(routingFamiliesLine);
        Assert.NotNull(skillsLine);
        Assert.Equal(8, CountCsvItemsFromInstructionLine(enabledPackLine!, "enabled_packs:"));
        Assert.Equal(12, CountCsvItemsFromInstructionLine(healthyToolsLine!, "healthy_tools:"));
        Assert.Equal(6, CountCsvItemsFromInstructionLine(routingFamiliesLine!, "routing_families:"));
        Assert.Equal(8, CountCsvItemsFromInstructionLine(skillsLine!, "skills:"));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsIncludeCapabilitySnapshot() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.AllowedRoots.Add(@"C:\logs");
        options.AllowedRoots.Add(@"D:\exports");
        var session = new ChatServiceSession(options, System.IO.Stream.Null);
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 9,
                RoutingAwareTools = 9,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 2
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 1
                    }
                }
            });

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("marker='ix:capability-snapshot:v1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills_marker='ix:skills:v1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_count='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools='9'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowed_roots='2'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_available='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote_reachability_mode='", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count='2'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routing_families='ad_domain,public_domain'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills='ad_domain.scope_hosts,public_domain.query_whois'", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsIncludeBootstrapProgressAndCapabilitySnapshot() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var pendingBootstrap = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var warnings = session.BuildHelloStartupWarningsForTesting(pendingBootstrap.Task);

        Assert.Contains(
            warnings,
            static warning => warning.Contains("Tool bootstrap in progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryReadInstructionLine(string input, string prefix) {
        var normalizedPrefix = (prefix ?? string.Empty).Trim();
        if (normalizedPrefix.Length == 0) {
            return null;
        }

        var lines = (input ?? string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (line.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) {
                return line;
            }
        }

        return null;
    }

    private static int CountCsvItemsFromInstructionLine(string line, string prefix) {
        var normalizedLine = (line ?? string.Empty).Trim();
        var normalizedPrefix = (prefix ?? string.Empty).Trim();
        if (normalizedLine.Length == 0 || normalizedPrefix.Length == 0) {
            return 0;
        }

        var value = normalizedLine.Substring(normalizedPrefix.Length).Trim();
        if (value.Length == 0) {
            return 0;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }
}
