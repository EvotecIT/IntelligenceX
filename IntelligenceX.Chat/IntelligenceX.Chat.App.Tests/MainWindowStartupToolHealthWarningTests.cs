using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies startup tool-health warning formatting shown in transcript warning cards.
/// </summary>
public sealed class MainWindowStartupToolHealthWarningTests {
    /// <summary>
    /// Formats structured startup tool-health warnings into readable pack-labeled copy.
    /// </summary>
    [Fact]
    public void FormatStartupToolHealthWarningForDisplay_HumanizesStructuredWarning() {
        const string warning = "[tool health][open_source][dnsclientx] dnsclientx_pack_info failed (smoke_invalid_argument): dnsclientx_ping: Provide target or targets for ping checks.";
        var packs = new[] {
            new ToolPackInfoDto {
                Id = "dnsclientx",
                Name = "DnsClientX",
                Tier = CapabilityTier.ReadOnly,
                Enabled = true,
                IsDangerous = false
            }
        };

        var formatted = MainWindow.FormatStartupToolHealthWarningForDisplay(warning, packs);

        Assert.Equal("**DnsClientX (Open)** startup smoke check needs input selection: dnsclientx_ping: Provide target or targets for ping checks.", formatted);
    }

    /// <summary>
    /// Resolves alias-form pack ids against canonical session pack metadata before rendering startup tool-health labels.
    /// </summary>
    [Fact]
    public void FormatStartupToolHealthWarningForDisplay_NormalizesAliasPackIdsAgainstPackCatalog() {
        const string warning = "[tool health][open_source][ADPlayground] ad_pack_info failed (smoke_not_configured): Select a domain before running the startup probe.";
        var packs = new[] {
            new ToolPackInfoDto {
                Id = "active_directory",
                Name = "Active Directory",
                Tier = CapabilityTier.ReadOnly,
                Enabled = true,
                IsDangerous = false
            }
        };

        var formatted = MainWindow.FormatStartupToolHealthWarningForDisplay(warning, packs);

        Assert.Equal("**Active Directory (Open)** startup smoke check is not configured: Select a domain before running the startup probe.", formatted);
    }

    /// <summary>
    /// Preserves the original warning text when the startup warning does not follow the structured tool-health envelope.
    /// </summary>
    [Fact]
    public void FormatStartupToolHealthWarningForDisplay_FallsBackToOriginalText_WhenWarningIsUnstructured() {
        const string warning = "Startup probe priming failed: transport unavailable.";

        var formatted = MainWindow.FormatStartupToolHealthWarningForDisplay(warning);

        Assert.Equal(warning, formatted);
    }

    /// <summary>
    /// Uses the shared startup warning preview policy for tool-health transcript cards.
    /// </summary>
    [Fact]
    public void BuildStartupToolHealthWarningLines_TruncatesUsingSharedPreviewPolicy() {
        var packs = new[] {
            new ToolPackInfoDto {
                Id = "dnsclientx",
                Name = "DnsClientX",
                Tier = CapabilityTier.ReadOnly,
                Enabled = true,
                IsDangerous = false
            }
        };
        var warning = "[tool health][open_source][dnsclientx] dnsclientx_pack_info failed (smoke_invalid_argument): dnsclientx_ping: Provide target or targets for ping checks.";
        var lines = MainWindow.BuildStartupToolHealthWarningLines(new[] { warning, warning, warning, warning, warning }, packs);

        Assert.Contains("Found 5 startup tool health warning(s):", lines);
        Assert.Contains("- +1 more", lines);
        Assert.Equal("Check the runtime policy panel for the full startup warning list.", lines[^1]);
    }
}
