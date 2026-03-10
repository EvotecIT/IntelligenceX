using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolMutabilityHintNamesTests {
    [Theory]
    [InlineData(" Read Write ", "read_write")]
    [InlineData("Enable-Feature", "enable_feature")]
    [InlineData("operation:id", "operation_id")]
    [InlineData("", "")]
    public void NormalizeHintToken_ShouldCanonicalizeSeparatorsAndCase(string input, string expected) {
        Assert.Equal(expected, ToolMutabilityHintNames.NormalizeHintToken(input));
    }

    [Theory]
    [InlineData("send")]
    [InlineData("Execute")]
    [InlineData("write-operation-id")]
    [InlineData("state change")]
    public void LooksLikeMutatingHint_ShouldRecognizeCanonicalMutatingTokens(string input) {
        Assert.True(ToolMutabilityHintNames.LooksLikeMutatingHint(input));
    }

    [Theory]
    [InlineData("read_only")]
    [InlineData("ReadOnly")]
    [InlineData("safe-read")]
    [InlineData("inventory")]
    public void LooksLikeReadOnlyHint_ShouldRecognizeCanonicalReadOnlyTokens(string input) {
        Assert.True(ToolMutabilityHintNames.LooksLikeReadOnlyHint(input));
    }
}
