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
    /// Preserves the original warning text when the startup warning does not follow the structured tool-health envelope.
    /// </summary>
    [Fact]
    public void FormatStartupToolHealthWarningForDisplay_FallsBackToOriginalText_WhenWarningIsUnstructured() {
        const string warning = "Startup probe priming failed: transport unavailable.";

        var formatted = MainWindow.FormatStartupToolHealthWarningForDisplay(warning);

        Assert.Equal(warning, formatted);
    }
}
