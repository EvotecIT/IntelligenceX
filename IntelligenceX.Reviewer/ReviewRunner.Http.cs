using System;
using System.Net.Http;
using System.Threading;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    // Infinite timeout here; each call applies its own CTS-based timeout.
    private static readonly HttpClient PreflightHttp = CreatePreflightHttp();
    private static readonly HttpClient OpenAiCompatibleHttp = CreateOpenAiCompatibleHttp();

    private static HttpClient CreatePreflightHttp() {
        return new HttpClient {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static HttpClient CreateOpenAiCompatibleHttp() {
        // We handle redirects ourselves to avoid "POST becomes GET" behavior on 301/302/303,
        // and to apply security checks consistently.
        var handler = new SocketsHttpHandler {
            AllowAutoRedirect = false
        };

        return new HttpClient(handler) {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}

