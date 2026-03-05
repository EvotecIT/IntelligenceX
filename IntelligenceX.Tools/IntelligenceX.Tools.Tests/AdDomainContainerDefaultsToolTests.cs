using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdDomainContainerDefaultsToolTests {
    [Fact]
    public void BindRequestContract_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = AdDomainContainerDefaultsTool.BindRequestContract(arguments: null);
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdDomainContainerDefaultsTool.DomainContainerDefaultsBindingContract>(binding.Request);
        Assert.Null(request.DomainName);
        Assert.Null(request.ForestName);
        Assert.False(request.ChangedOnly);
    }

    [Fact]
    public void BindRequestContract_NormalizesScopeNamesAndReadsChangedOnlyFlag() {
        var binding = AdDomainContainerDefaultsTool.BindRequestContract(new JsonObject()
            .Add("domain_name", " ad.evotec.xyz ")
            .Add("forest_name", " evotec.xyz ")
            .Add("changed_only", true));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdDomainContainerDefaultsTool.DomainContainerDefaultsBindingContract>(binding.Request);
        Assert.Equal("ad.evotec.xyz", request.DomainName);
        Assert.Equal("evotec.xyz", request.ForestName);
        Assert.True(request.ChangedOnly);
    }

    [Fact]
    public void BindRequestContract_WhenChangedOnlyProvidedAsString_ParsesCompatibly() {
        var binding = AdDomainContainerDefaultsTool.BindRequestContract(new JsonObject()
            .Add("changed_only", "true"));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdDomainContainerDefaultsTool.DomainContainerDefaultsBindingContract>(binding.Request);
        Assert.True(request.ChangedOnly);
    }

    [Fact]
    public void BindRequestContract_WhenChangedOnlyStringIsInvalid_FailsBinding() {
        var binding = AdDomainContainerDefaultsTool.BindRequestContract(new JsonObject()
            .Add("changed_only", "notabool"));
        Assert.False(binding.IsValid);
        Assert.Contains("changed_only", binding.Error);
    }
}
