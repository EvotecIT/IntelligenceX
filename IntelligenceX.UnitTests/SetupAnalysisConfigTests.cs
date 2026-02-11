using System.Text.Json.Nodes;
using IntelligenceX.Cli.Setup;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class SetupAnalysisConfigTests {
    [Fact]
    public void Apply_PreservesUnrelatedKeys_OnExistingAnalysisObject() {
        var root = new JsonObject {
            ["review"] = new JsonObject {
                ["customReviewFlag"] = true
            },
            ["analysis"] = new JsonObject {
                ["enabled"] = false,
                ["customAnalysisFlag"] = "keep",
                ["results"] = new JsonObject {
                    ["summary"] = false
                },
                ["gate"] = new JsonObject {
                    ["enabled"] = true,
                    ["minSeverity"] = "error",
                    ["customGateFlag"] = 1
                }
            }
        };

        SetupAnalysisConfig.Apply(
            root,
            enabledSet: true,
            enabled: true,
            gateEnabledSet: true,
            gateEnabled: false,
            packsSet: true,
            packs: new[] { "all-100" });

        var review = Assert.IsType<JsonObject>(root["review"]);
        Assert.True(review["customReviewFlag"]?.GetValue<bool>());

        var analysis = Assert.IsType<JsonObject>(root["analysis"]);
        Assert.True(analysis["enabled"]?.GetValue<bool>());
        Assert.Equal("keep", analysis["customAnalysisFlag"]?.GetValue<string>());

        var packs = Assert.IsType<JsonArray>(analysis["packs"]);
        Assert.Single(packs);
        Assert.Equal("all-100", packs[0]?.GetValue<string>());

        var results = Assert.IsType<JsonObject>(analysis["results"]);
        Assert.False(results["summary"]?.GetValue<bool>());

        var gate = Assert.IsType<JsonObject>(analysis["gate"]);
        Assert.False(gate["enabled"]?.GetValue<bool>());
        Assert.Equal("error", gate["minSeverity"]?.GetValue<string>());
        Assert.Equal(1, gate["customGateFlag"]?.GetValue<int>());
    }

    [Fact]
    public void Apply_PacksOnly_InfersEnabledAndPreservesCustomFields() {
        var root = new JsonObject {
            ["analysis"] = new JsonObject {
                ["customAnalysisFlag"] = "keep"
            }
        };

        SetupAnalysisConfig.Apply(
            root,
            enabledSet: false,
            enabled: false,
            gateEnabledSet: false,
            gateEnabled: false,
            packsSet: true,
            packs: new[] { "all-50", "powershell-50" });

        var analysis = Assert.IsType<JsonObject>(root["analysis"]);
        Assert.True(analysis["enabled"]?.GetValue<bool>());
        Assert.Equal("keep", analysis["customAnalysisFlag"]?.GetValue<string>());

        var packs = Assert.IsType<JsonArray>(analysis["packs"]);
        Assert.Equal(2, packs.Count);
        Assert.Equal("all-50", packs[0]?.GetValue<string>());
        Assert.Equal("powershell-50", packs[1]?.GetValue<string>());
    }
}
