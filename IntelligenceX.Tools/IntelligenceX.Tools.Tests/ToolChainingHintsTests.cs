using System;
using System.Collections.Generic;
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

    [Fact]
    public void Map_ShouldReturnReadOnlyDictionary() {
        var map = ToolChainingHints.Map(("contract", "test"));

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(map);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void Create_WhenInputsEmpty_ShouldReturnReadOnlyEmptyMap() {
        var chain = ToolChainingHints.Create();

        Assert.Empty(chain.NextActions);
        Assert.Equal(string.Empty, chain.Cursor);
        Assert.Equal(string.Empty, chain.ResumeToken);

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(chain.Handoff);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void ToolChainContractModel_ObjectInitializer_ShouldNormalizeAndDefensivelyCopy() {
        var actions = new List<ToolNextActionModel> {
            new() {
                Tool = " officeimo_read ",
                Reason = " follow up ",
                SuggestedArguments = new Dictionary<string, string>(StringComparer.Ordinal) {
                    [" path "] = @"C:\docs"
                }
            }
        };
        var handoff = new Dictionary<string, string>(StringComparer.Ordinal) {
            [" contract "] = "officeimo_read_handoff"
        };

        var chain = new ToolChainContractModel {
            NextActions = actions,
            Cursor = " c1 ",
            ResumeToken = " r1 ",
            Handoff = handoff,
            Confidence = 2.0d
        };

        actions.Add(new ToolNextActionModel { Tool = "x", Reason = "y" });
        handoff["new"] = "value";

        Assert.Single(chain.NextActions);
        Assert.Equal("officeimo_read", chain.NextActions[0].Tool);
        Assert.Equal("follow up", chain.NextActions[0].Reason);
        Assert.Equal("c1", chain.Cursor);
        Assert.Equal("r1", chain.ResumeToken);
        Assert.Equal(1d, chain.Confidence);
        Assert.True(chain.Handoff.ContainsKey("contract"));
        Assert.False(chain.Handoff.ContainsKey("new"));

        var actionsList = Assert.IsAssignableFrom<IList<ToolNextActionModel>>(chain.NextActions);
        Assert.Throws<NotSupportedException>(() => actionsList.Add(new ToolNextActionModel { Tool = "z", Reason = "r" }));
        var handoffMap = Assert.IsAssignableFrom<IDictionary<string, string>>(chain.Handoff);
        Assert.Throws<NotSupportedException>(() => handoffMap.Add("x", "1"));
    }

    [Fact]
    public void ToolNextActionModel_ObjectInitializer_ShouldNormalizeAndDefensivelyCopyArguments() {
        var suggestedArguments = new Dictionary<string, string>(StringComparer.Ordinal) {
            [" path "] = @"C:\docs"
        };

        var action = new ToolNextActionModel {
            Tool = " ad_scope_discovery ",
            Reason = " investigate trust path ",
            SuggestedArguments = suggestedArguments
        };

        suggestedArguments["server"] = "dc01.contoso.com";

        Assert.Equal("ad_scope_discovery", action.Tool);
        Assert.Equal("investigate trust path", action.Reason);
        Assert.True(action.SuggestedArguments.ContainsKey("path"));
        Assert.False(action.SuggestedArguments.ContainsKey("server"));

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(action.SuggestedArguments);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void ToolChainContractModel_ObjectInitializer_WhenAssignedNulls_ShouldUseSafeDefaults() {
        var chain = new ToolChainContractModel {
            NextActions = null!,
            Cursor = null!,
            ResumeToken = null!,
            Handoff = null!,
            Confidence = 0.5d
        };

        Assert.Empty(chain.NextActions);
        Assert.Equal(string.Empty, chain.Cursor);
        Assert.Equal(string.Empty, chain.ResumeToken);
        Assert.Equal(0.5d, chain.Confidence);

        var actions = Assert.IsAssignableFrom<IList<ToolNextActionModel>>(chain.NextActions);
        Assert.Throws<NotSupportedException>(() => actions.Add(new ToolNextActionModel { Tool = "x", Reason = "y" }));
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(chain.Handoff);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
    }

    [Fact]
    public void ToolChainContractModel_ObjectInitializer_ShouldClampNegativeConfidenceToZero() {
        var chain = new ToolChainContractModel {
            Confidence = -0.1d
        };

        Assert.Equal(0d, chain.Confidence);
    }
}
