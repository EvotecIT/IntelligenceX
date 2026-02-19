using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolWriteGovernanceConventionsTests {
    [Fact]
    public void BooleanFlagTrue_ShouldBuildDefaultWriteContract() {
        ToolWriteGovernanceContract contract = ToolWriteGovernanceConventions.BooleanFlagTrue("send");

        Assert.True(contract.IsWriteCapable);
        Assert.True(contract.RequiresGovernanceAuthorization);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, contract.GovernanceContractId);
        Assert.Equal(ToolWriteIntentMode.BooleanFlagTrue, contract.IntentMode);
        Assert.Equal("send", contract.IntentArgumentName);
        Assert.True(contract.RequireExplicitConfirmation);
        Assert.Equal("send", contract.ConfirmationArgumentName);
    }

    [Fact]
    public void StringEquals_ShouldBuildStringMatchWriteContract() {
        ToolWriteGovernanceContract contract = ToolWriteGovernanceConventions.StringEquals(
            intentArgumentName: "intent",
            intentStringValue: "read_write",
            confirmationArgumentName: "allow_write");

        Assert.True(contract.IsWriteCapable);
        Assert.True(contract.RequiresGovernanceAuthorization);
        Assert.Equal(ToolWriteIntentMode.StringEquals, contract.IntentMode);
        Assert.Equal("intent", contract.IntentArgumentName);
        Assert.Equal("read_write", contract.IntentStringValue);
        Assert.Equal("allow_write", contract.ConfirmationArgumentName);
    }

    [Fact]
    public void BooleanFlagTrue_MissingIntentArgument_ShouldThrow() {
        Assert.Throws<ArgumentException>(() => ToolWriteGovernanceConventions.BooleanFlagTrue(" "));
    }
}
