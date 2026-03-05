using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdDsHeuristicsToolTests {
    [Fact]
    public void BindRequestContract_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = AdDsHeuristicsTool.BindRequestContract(arguments: null);
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdDsHeuristicsTool.DsHeuristicsBindingContract>(binding.Request);
        Assert.Null(request.ForestName);
        Assert.False(request.IncludePositions);
        Assert.False(request.NonDefaultOnly);
        Assert.Equal(64, request.MaxPositionRows);
    }

    [Fact]
    public void BindRequestContract_NormalizesForestNameAndClampsMaxPositionRows() {
        var binding = AdDsHeuristicsTool.BindRequestContract(new JsonObject()
            .Add("forest_name", " ad.evotec.xyz ")
            .Add("include_positions", true)
            .Add("non_default_only", true)
            .Add("max_position_rows", 0));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdDsHeuristicsTool.DsHeuristicsBindingContract>(binding.Request);
        Assert.Equal("ad.evotec.xyz", request.ForestName);
        Assert.True(request.IncludePositions);
        Assert.True(request.NonDefaultOnly);
        Assert.Equal(1, request.MaxPositionRows);
    }

    [Fact]
    public void BindRequestContract_CapsMaxPositionRowsToSafetyLimit() {
        var binding = AdDsHeuristicsTool.BindRequestContract(new JsonObject()
            .Add("max_position_rows", 999999));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdDsHeuristicsTool.DsHeuristicsBindingContract>(binding.Request);
        Assert.Equal(2048, request.MaxPositionRows);
    }
}
