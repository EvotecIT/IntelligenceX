using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public async Task RunModelPhaseWithProgressAsync_RecoversMissingThreadAndPersistsAlias() {
        using var server = new DeterministicCompatibleHttpServer();

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model"
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);

        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = "mock-local-model"
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = server.BaseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = OpenAICompatibleHttpAuthMode.None;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = true;

        using var client = await IntelligenceXClient.ConnectAsync(clientOptions);
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        const string missingThreadId = "missing-thread-for-recovery";
        var result = await session.RunModelPhaseWithProgressAsync(
                client,
                writer,
                requestId: "req-thread-recovery",
                threadId: missingThreadId,
                input: ChatInput.FromText("Run domain summary."),
                options: new ChatOptions { Model = "mock-local-model" },
                cancellationToken: CancellationToken.None,
                phaseStatus: "phase_plan",
                phaseMessage: "Planning...",
                heartbeatLabel: "Planning",
                heartbeatSeconds: 0);

        Assert.NotNull(result);
        Assert.Equal(1, server.ChatCompletionRequestCount);

        var recoveredThreadId = session.ResolveRecoveredThreadAliasForTesting(missingThreadId);
        Assert.False(string.IsNullOrWhiteSpace(recoveredThreadId));
        Assert.NotEqual(missingThreadId, recoveredThreadId);
    }
}
