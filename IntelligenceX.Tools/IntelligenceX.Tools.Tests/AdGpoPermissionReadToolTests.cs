using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdGpoPermissionReadToolTests {
    [Fact]
    public void BindRequestContract_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = AdGpoPermissionReadTool.BindRequestContract(arguments: null);
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionReadTool.GpoPermissionReadBindingContract>(binding.Request);
        Assert.False(request.IncludeCompliant);
        Assert.False(request.DenyOnly);
        Assert.Equal(50000, request.MaxGpos);
    }

    [Fact]
    public void BindRequestContract_ReadsFlagsAndClampsNonPositiveMaxGpos() {
        var binding = AdGpoPermissionReadTool.BindRequestContract(new JsonObject()
            .Add("include_compliant", true)
            .Add("deny_only", true)
            .Add("max_gpos", 0));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionReadTool.GpoPermissionReadBindingContract>(binding.Request);
        Assert.True(request.IncludeCompliant);
        Assert.True(request.DenyOnly);
        Assert.Equal(1, request.MaxGpos);
    }

    [Fact]
    public void BindRequestContract_CapsMaxGposToSafetyLimit() {
        var binding = AdGpoPermissionReadTool.BindRequestContract(new JsonObject()
            .Add("max_gpos", 999999));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionReadTool.GpoPermissionReadBindingContract>(binding.Request);
        Assert.Equal(200000, request.MaxGpos);
    }
}
