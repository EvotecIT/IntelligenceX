using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdGpoPermissionAdministrativeToolTests {
    [Fact]
    public void BindRequestContract_UsesExpectedDefaultsWhenArgumentsMissing() {
        var binding = AdGpoPermissionAdministrativeTool.BindRequestContract(arguments: null);
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionAdministrativeTool.GpoPermissionAdministrativeBindingContract>(binding.Request);
        Assert.False(request.IncludeCompliant);
        Assert.False(request.ErrorsOnly);
        Assert.Equal(50000, request.MaxGpos);
    }

    [Fact]
    public void BindRequestContract_ReadsFlagsAndClampsNonPositiveMaxGpos() {
        var binding = AdGpoPermissionAdministrativeTool.BindRequestContract(new JsonObject()
            .Add("include_compliant", true)
            .Add("errors_only", true)
            .Add("max_gpos", 0));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionAdministrativeTool.GpoPermissionAdministrativeBindingContract>(binding.Request);
        Assert.True(request.IncludeCompliant);
        Assert.True(request.ErrorsOnly);
        Assert.Equal(1, request.MaxGpos);
    }

    [Fact]
    public void BindRequestContract_CapsMaxGposToSafetyLimit() {
        var binding = AdGpoPermissionAdministrativeTool.BindRequestContract(new JsonObject()
            .Add("max_gpos", 999999));
        Assert.True(binding.IsValid);

        var request = Assert.IsType<AdGpoPermissionAdministrativeTool.GpoPermissionAdministrativeBindingContract>(binding.Request);
        Assert.Equal(200000, request.MaxGpos);
    }
}
