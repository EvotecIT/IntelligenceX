using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdReplicationStatusToolTests {
    [Fact]
    public void BindRequestContract_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = AdReplicationStatusTool.BindRequestContract(arguments: null);
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdReplicationStatusTool.ReplicationStatusBindingContract>(binding.Request);
        Assert.Empty(request.RequestedComputerNames);
        Assert.False(request.HealthOnly);
    }

    [Fact]
    public void BindRequestContract_NormalizesAndDeduplicatesRequestedComputerNames() {
        var binding = AdReplicationStatusTool.BindRequestContract(new JsonObject()
            .Add("computer_names", new JsonArray()
                .Add(" dc1.ad.evotec.xyz ")
                .Add("DC1.AD.EVOTEC.XYZ")
                .Add(string.Empty)
                .Add("dc2.ad.evotec.xyz"))
            .Add("health_only", true));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdReplicationStatusTool.ReplicationStatusBindingContract>(binding.Request);
        Assert.Equal(
            new[] { "dc1.ad.evotec.xyz", "dc2.ad.evotec.xyz" },
            request.RequestedComputerNames);
        Assert.True(request.HealthOnly);
    }

    [Fact]
    public void BindRequestContract_WhenComputerNamesArgumentNotArray_UsesDefaults() {
        var binding = AdReplicationStatusTool.BindRequestContract(new JsonObject()
            .Add("computer_names", "dc1.ad.evotec.xyz"));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdReplicationStatusTool.ReplicationStatusBindingContract>(binding.Request);
        Assert.Empty(request.RequestedComputerNames);
        Assert.False(request.HealthOnly);
    }
}
