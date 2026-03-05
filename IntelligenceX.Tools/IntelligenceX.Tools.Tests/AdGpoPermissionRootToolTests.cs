using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdGpoPermissionRootToolTests {
    [Fact]
    public void BindRequestContract_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = AdGpoPermissionRootTool.BindRequestContract(arguments: null);
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionRootTool.GpoPermissionRootBindingContract>(binding.Request);
        Assert.Null(request.Permission);
        Assert.False(request.DenyOnly);
        Assert.False(request.InheritedOnly);
        Assert.Equal(100000, request.MaxRows);
    }

    [Fact]
    public void BindRequestContract_ReadsFlagsAndClampsNonPositiveMaxRows() {
        var binding = AdGpoPermissionRootTool.BindRequestContract(new JsonObject()
            .Add("permission", " GpoRootOwner ")
            .Add("deny_only", true)
            .Add("inherited_only", true)
            .Add("max_rows", 0));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionRootTool.GpoPermissionRootBindingContract>(binding.Request);
        Assert.Equal("GpoRootOwner", request.Permission);
        Assert.True(request.DenyOnly);
        Assert.True(request.InheritedOnly);
        Assert.Equal(1, request.MaxRows);
    }

    [Fact]
    public void BindRequestContract_CapsMaxRowsToSafetyLimit() {
        var binding = AdGpoPermissionRootTool.BindRequestContract(new JsonObject()
            .Add("max_rows", 99999999));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionRootTool.GpoPermissionRootBindingContract>(binding.Request);
        Assert.Equal(1000000, request.MaxRows);
    }

    [Fact]
    public void BindRequestContract_WhenPermissionInvalid_FailsBinding() {
        var binding = AdGpoPermissionRootTool.BindRequestContract(new JsonObject()
            .Add("permission", "invalid"));
        Assert.False(binding.IsValid);
        Assert.Contains("permission must be one of", binding.Error);
    }
}
