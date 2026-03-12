using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies startup unavailable-pack warning projection stays canonical across alias pack ids.
/// </summary>
public sealed class MainWindowUnavailablePackWarningTests {
    /// <summary>
    /// Ensures alias and canonical ids for the same pack collapse into one warning entry.
    /// </summary>
    [Fact]
    public void BuildUnavailablePackWarningLines_DeduplicatesAliasPackIdsUsingCanonicalIds() {
        var lines = MainWindow.BuildUnavailablePackWarningLines(new[] {
            new ToolPackInfoDto {
                Id = "ADPlayground",
                Name = "Active Directory",
                Tier = CapabilityTier.ReadOnly,
                Enabled = false,
                DisabledReason = "Missing required module.",
                IsDangerous = false
            },
            new ToolPackInfoDto {
                Id = "active_directory",
                Name = "Active Directory",
                Tier = CapabilityTier.ReadOnly,
                Enabled = false,
                DisabledReason = "Missing required module.",
                IsDangerous = false
            },
            new ToolPackInfoDto {
                Id = "ComputerX",
                Name = "System",
                Tier = CapabilityTier.ReadOnly,
                Enabled = false,
                DisabledReason = "Remote endpoint unavailable.",
                IsDangerous = false
            }
        });

        Assert.Contains(lines, line => string.Equals(line, "Found 2 unavailable pack(s):", StringComparison.Ordinal));
        Assert.Contains(lines, line => string.Equals(line, "- Active Directory: Missing required module.", StringComparison.Ordinal));
        Assert.Contains(lines, line => string.Equals(line, "- System: Remote endpoint unavailable.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures canonical fallback labels are humanized when a pack name is unavailable.
    /// </summary>
    [Fact]
    public void BuildUnavailablePackWarningLines_HumanizesCanonicalFallbackLabelWhenNameMissing() {
        var lines = MainWindow.BuildUnavailablePackWarningLines(new[] {
            new ToolPackInfoDto {
                Id = "ADPlayground",
                Name = string.Empty,
                Tier = CapabilityTier.ReadOnly,
                Enabled = false,
                DisabledReason = "Startup prerequisites missing.",
                IsDangerous = false
            }
        });

        Assert.Contains(lines, line => string.Equals(line, "- Active Directory: Startup prerequisites missing.", StringComparison.Ordinal));
    }
}
