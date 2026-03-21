using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolPackCapabilityTagsTests {
    [Fact]
    public void PrioritizeForPlanner_PromotesGovernanceAndScopeTags() {
        var prioritized = ToolPackCapabilityTags.PrioritizeForPlanner(
            new[] {
                "directory",
                "remote_analysis",
                "governed_write",
                "identity_lifecycle",
                "write_capable"
            },
            maxCount: 4);

        Assert.Equal(
            new[] {
                ToolPackCapabilityTags.GovernedWrite,
                ToolPackCapabilityTags.WriteCapable,
                ToolPackCapabilityTags.RemoteAnalysis,
                "directory"
            },
            prioritized);
    }

    [Fact]
    public void HasExecutionOrAnalysisScope_ReturnsTrueForStandardLocalityTags() {
        Assert.True(ToolPackCapabilityTags.HasExecutionOrAnalysisScope(new[] { ToolPackCapabilityTags.LocalAnalysis }));
        Assert.True(ToolPackCapabilityTags.HasExecutionOrAnalysisScope(new[] { ToolPackCapabilityTags.RemoteExecution }));
        Assert.False(ToolPackCapabilityTags.HasExecutionOrAnalysisScope(new[] { "dns", "directory" }));
    }

    [Fact]
    public void HasWriteOrGovernanceTag_ReturnsTrueForStandardMutatingTags() {
        Assert.True(ToolPackCapabilityTags.HasWriteOrGovernanceTag(new[] { ToolPackCapabilityTags.WriteCapable }));
        Assert.True(ToolPackCapabilityTags.HasWriteOrGovernanceTag(new[] { ToolPackCapabilityTags.GovernedWrite }));
        Assert.False(ToolPackCapabilityTags.HasWriteOrGovernanceTag(new[] { ToolPackCapabilityTags.RemoteAnalysis }));
    }

    [Fact]
    public void DeferredCapabilityTags_NormalizeAndRoundTripCapabilityIdentifiers() {
        var tag = ToolPackCapabilityTags.CreateDeferredCapabilityTag("Email Alerts");

        Assert.Equal("deferred_capability:email_alerts", tag);
        Assert.True(ToolPackCapabilityTags.TryGetDeferredCapabilityId(tag, out var capabilityId));
        Assert.Equal("email_alerts", capabilityId);
        Assert.False(ToolPackCapabilityTags.TryGetDeferredCapabilityId("reporting", out _));
    }
}
