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
    [InlineData(false)]
    [InlineData(true)]
    public void SessionPreservesNativeCopilotCredentialAndStreamingChoices(bool streaming) {
        var options = new EasySessionOptions { TransportKind = OpenAITransportKind.CopilotNative };
        options.CopilotOptions.Streaming = streaming;
        options.CopilotOptions.GitHubToken = "test-token";
        options.CopilotOptions.RequestTimeout = TimeSpan.FromSeconds(17);
        var actual = Build(options).CopilotOptions;
        Assert.Equal(streaming, actual.Streaming);
        Assert.Equal("test-token", actual.GitHubToken);
        Assert.Equal(TimeSpan.FromSeconds(17), actual.RequestTimeout);
        Assert.NotSame(options.CopilotOptions, actual);
    }

    private static IntelligenceXClientOptions Build(EasySessionOptions options) =>
        (IntelligenceXClientOptions)typeof(EasySession).GetMethod("BuildClientOptions", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { options })!;
}
