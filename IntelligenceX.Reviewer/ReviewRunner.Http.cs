using System;
using System.Net.Http;
using System.Threading;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    // Infinite timeout here; each call applies its own CTS-based timeout.
    private static readonly HttpClient PreflightHttp = CreatePreflightHttp();
    private static readonly SocketsHttpHandler OpenAiCompatibleHandler = CreateOpenAiCompatibleHandler();
    private static readonly HttpClient OpenAiCompatibleHttp = CreateOpenAiCompatibleHttp(OpenAiCompatibleHandler);

    private static HttpClient CreatePreflightHttp() {
        return new HttpClient {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static SocketsHttpHandler CreateOpenAiCompatibleHandler() {
        // We handle redirects ourselves to avoid "POST becomes GET" behavior on 301/302/303,
        // and to apply security checks consistently.
        return new SocketsHttpHandler {
            AllowAutoRedirect = false
        };
    }

    private static HttpClient CreateOpenAiCompatibleHttp(SocketsHttpHandler handler) {
        // Align handler + HttpClient lifetimes explicitly; avoids subtle disposal issues if this ever changes.
        return new HttpClient(handler, disposeHandler: false) {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}
