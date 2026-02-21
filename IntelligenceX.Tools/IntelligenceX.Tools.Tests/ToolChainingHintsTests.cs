using System;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolChainingHintsTests {
    [Fact]
    public void Create_ShouldNormalizeAndClampContractValues() {
        var action = ToolChainingHints.NextAction(
            tool: "officeimo_read",
            reason: "follow up",
            suggestedArguments: ToolChainingHints.Map(("path", @"C:\docs")));

        var chain = ToolChainingHints.Create(
            nextActions: new[] { action, action },
            cursor: "  c1  ",
            resumeToken: "  r1  ",
            handoff: ToolChainingHints.Map(("contract", "test")),
            confidence: 2.2d);

        Assert.Single(chain.NextActions);
        Assert.Equal("officeimo_read", chain.NextActions[0].Tool);
        Assert.Equal("follow up", chain.NextActions[0].Reason);
        Assert.Equal("c1", chain.Cursor);
        Assert.Equal("r1", chain.ResumeToken);
        Assert.True(chain.Handoff.TryGetValue("contract", out var contract));
        Assert.Equal("test", contract?.ToString());
        Assert.Equal(1d, chain.Confidence);
    }

    [Fact]
    public void BuildToken_ShouldEncodeKeyValueParts() {
        var token = ToolChainingHints.BuildToken(
            "eventlog",
            ("name", "Kerberos auth"),
            ("machine", "dc01.contoso.com"));

        Assert.StartsWith("eventlog:", token, StringComparison.Ordinal);
        Assert.Contains("name=Kerberos%20auth", token, StringComparison.Ordinal);
        Assert.Contains("machine=dc01.contoso.com", token, StringComparison.Ordinal);
    }
}
