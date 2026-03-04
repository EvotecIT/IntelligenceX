using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceExecutionLanePolicyTests {
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(32, 32)]
    public void ResolveSessionExecutionQueueLimit_ClampsNegativeValues(int configuredLimit, int expected) {
        var resolved = ChatServiceSession.ResolveSessionExecutionQueueLimit(configuredLimit);

        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    public void ResolveGlobalExecutionLaneConcurrency_ClampsNegativeValues(int configuredConcurrency, int expected) {
        var resolved = ChatServiceSession.ResolveGlobalExecutionLaneConcurrency(configuredConcurrency);

        Assert.Equal(expected, resolved);
    }
}
