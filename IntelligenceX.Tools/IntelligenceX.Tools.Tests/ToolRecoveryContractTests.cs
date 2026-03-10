using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolRecoveryContractTests {
    [Fact]
    public void Validate_ShouldAcceptRecoveryToolNamesWhenNonEmptyToolsAreProvided() {
        var contract = new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 1,
            RetryableErrorCodes = new[] { "timeout" },
            RecoveryToolNames = new[] { "custom_environment_discover", "custom_pack_info" }
        };

        var ex = Record.Exception(contract.Validate);
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_ShouldRejectRecoveryToolNamesWhenAllValuesAreBlank() {
        var contract = new ToolRecoveryContract {
            IsRecoveryAware = true,
            RecoveryToolNames = new[] { " ", "" }
        };

        var ex = Assert.Throws<InvalidOperationException>(contract.Validate);
        Assert.Contains("RecoveryToolNames", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
