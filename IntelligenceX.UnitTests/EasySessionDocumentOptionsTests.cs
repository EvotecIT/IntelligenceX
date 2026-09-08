using System.Reflection;
using IntelligenceX.OpenAI;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class EasySessionDocumentOptionsTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionPreservesExplicitNativeFallbackAndDiagnosticsChoices(bool enabled) {
        var options = new EasySessionOptions();
        options.NativeOptions.EnableModelFallback = enabled;
        options.NativeOptions.AllowSensitiveDiagnostics = enabled;
        var actual = Build(options);
        Assert.Equal(enabled, actual.NativeOptions.EnableModelFallback);
        Assert.Equal(enabled, actual.NativeOptions.AllowSensitiveDiagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionPreservesExplicitCompatibleRoutingChoices(bool enabled) {
        var options = new EasySessionOptions { TransportKind = OpenAITransportKind.CompatibleHttp };
        options.CompatibleHttpOptions.AllowAutoRedirect = enabled;
        options.CompatibleHttpOptions.UseProxy = enabled;
        var actual = Build(options);
        Assert.Equal(enabled, actual.CompatibleHttpOptions.AllowAutoRedirect);
        Assert.Equal(enabled, actual.CompatibleHttpOptions.UseProxy);
    }

    [Theory]
    [InlineData(256L)]
    [InlineData(268435456L)]
    public void SessionPreservesSmallerAndLargerCopilotReceiveBudgets(long maximum) {
        var options = new EasySessionOptions { TransportKind = OpenAITransportKind.CopilotCli };
        options.CopilotOptions.MaxReceivedBytes = maximum;
        Assert.Equal(maximum, Build(options).CopilotOptions.MaxReceivedBytes);
    }

    private static IntelligenceXClientOptions Build(EasySessionOptions options) =>
        (IntelligenceXClientOptions)typeof(EasySession).GetMethod("BuildClientOptions", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { options })!;
}
