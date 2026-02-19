using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolAuthenticationConventionsTests {
    [Fact]
    public void HostManaged_ShouldBuildDefaultAuthContract() {
        ToolAuthenticationContract contract = ToolAuthenticationConventions.HostManaged(requiresAuthentication: true);

        Assert.True(contract.IsAuthenticationAware);
        Assert.True(contract.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, contract.AuthenticationContractId);
        Assert.Equal(ToolAuthenticationMode.HostManaged, contract.Mode);
        Assert.Empty(contract.GetSchemaArgumentNames());
    }

    [Fact]
    public void ProfileReference_ShouldExposeProfileArgument() {
        ToolAuthenticationContract contract = ToolAuthenticationConventions.ProfileReference();

        Assert.True(contract.IsAuthenticationAware);
        Assert.Equal(ToolAuthenticationMode.ProfileReference, contract.Mode);
        Assert.Equal(ToolAuthenticationArgumentNames.ProfileId, contract.ProfileIdArgumentName);
        Assert.Equal(new[] { ToolAuthenticationArgumentNames.ProfileId }, contract.GetSchemaArgumentNames());
    }

    [Fact]
    public void RunAsReference_MissingArgumentName_ShouldThrow() {
        Assert.Throws<ArgumentException>(() =>
            ToolAuthenticationConventions.RunAsReference(runAsProfileIdArgumentName: " "));
    }
}
