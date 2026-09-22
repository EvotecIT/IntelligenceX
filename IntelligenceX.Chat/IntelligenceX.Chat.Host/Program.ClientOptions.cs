using IntelligenceX.OpenAI;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static IntelligenceXClientOptions BuildTransportClientOptions(ReplOptions options) {
        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = options.OpenAITransport,
            DefaultModel = options.Model
        };
        if (clientOptions.TransportKind == OpenAITransportKind.CompatibleHttp) {
            clientOptions.CompatibleHttpOptions.BaseUrl = options.OpenAIBaseUrl;
            clientOptions.CompatibleHttpOptions.ApiKey = options.OpenAIApiKey;
            clientOptions.CompatibleHttpOptions.Streaming = options.OpenAIStreaming;
            clientOptions.CompatibleHttpOptions.AllowInsecureHttp = options.OpenAIAllowInsecureHttp;
            clientOptions.CompatibleHttpOptions.AllowInsecureHttpNonLoopback = options.OpenAIAllowInsecureHttpNonLoopback;
        }

        if (clientOptions.TransportKind == OpenAITransportKind.CopilotNative) {
            clientOptions.CopilotOptions.Streaming = options.OpenAIStreaming;
        }

        return clientOptions;
    }
}
