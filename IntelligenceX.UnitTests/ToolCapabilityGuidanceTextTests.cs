using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolCapabilityGuidanceTextTests {
    [Fact]
    public void BuildToolingAvailabilityLine_ShouldReturnLiveToolingText_WhenAvailable() {
        var line = ToolCapabilityGuidanceText.BuildToolingAvailabilityLine(toolingAvailable: true);

        Assert.Contains("live session tools", line);
        Assert.DoesNotContain("not currently available", line);
    }

    [Fact]
    public void BuildToolingAvailabilityLine_ShouldReturnConversationalFallback_WhenUnavailable() {
        var line = ToolCapabilityGuidanceText.BuildToolingAvailabilityLine(toolingAvailable: false);

        Assert.Contains("not currently available", line);
        Assert.Contains("conversational", line);
    }

    [Fact]
    public void BuildRemoteReadyAreasLine_ShouldFormatDisplayNames() {
        var line = ToolCapabilityGuidanceText.BuildRemoteReadyAreasLine(new[] { "Active Directory", "Event Log" });

        Assert.Equal("Remote-ready capability areas currently include Active Directory, Event Log.", line);
    }

    [Fact]
    public void BuildContractGuidedAutonomyLine_ShouldReturnStableSharedText() {
        var line = ToolCapabilityGuidanceText.BuildContractGuidedAutonomyLine();

        Assert.Contains("contract-guided setup, handoff, and recovery", line);
        Assert.Contains("unsupported manual steps", line);
    }
}
