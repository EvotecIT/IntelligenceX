using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public async Task LiveSmoke_CompatibleHttp_NonEmptyAssistantText_OptIn() {
        if (!string.Equals(Environment.GetEnvironmentVariable("IX_CHAT_LIVE_SMOKE"), "1", StringComparison.Ordinal)) {
            return;
        }

        var baseUrl = (Environment.GetEnvironmentVariable("IX_CHAT_LIVE_SMOKE_BASE_URL") ?? string.Empty).Trim();
        if (baseUrl.Length == 0) {
            throw new InvalidOperationException("IX_CHAT_LIVE_SMOKE_BASE_URL is required when IX_CHAT_LIVE_SMOKE=1.");
        }

        var model = (Environment.GetEnvironmentVariable("IX_CHAT_LIVE_SMOKE_MODEL") ?? "gpt-4.1-mini").Trim();
        var authMode = ParseLiveSmokeAuthMode(Environment.GetEnvironmentVariable("IX_CHAT_LIVE_SMOKE_AUTH_MODE"));
        var apiKey = (Environment.GetEnvironmentVariable("IX_CHAT_LIVE_SMOKE_API_KEY") ?? string.Empty).Trim();
        var allowInsecure = ParseLiveSmokeBoolean(Environment.GetEnvironmentVariable("IX_CHAT_LIVE_SMOKE_ALLOW_INSECURE_HTTP"));

        if (authMode != OpenAICompatibleHttpAuthMode.None && apiKey.Length == 0) {
            throw new InvalidOperationException(
                "IX_CHAT_LIVE_SMOKE_API_KEY is required when IX_CHAT_LIVE_SMOKE=1 and auth mode is not 'none'.");
        }

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = baseUrl,
            OpenAIAllowInsecureHttp = allowInsecure,
            OpenAIStreaming = false,
            OpenAIAuthMode = authMode,
            OpenAIApiKey = apiKey.Length == 0 ? null : apiKey,
            Model = model,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);

        // Keep smoke deterministic: no tools, one direct response.
        SetSessionRegistry(session, new ToolRegistry());

        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = model
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = baseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = authMode;
        clientOptions.CompatibleHttpOptions.ApiKey = apiKey;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = allowInsecure;

        using var client = await IntelligenceXClient.ConnectAsync(clientOptions);
        var thread = await client.StartNewThreadAsync(model);
        var request = new ChatRequest {
            RequestId = "req-live-smoke-compatible-http",
            ThreadId = thread.Id,
            Text = "Reply with one short sentence proving the assistant is online and responsive.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer,
            request,
            thread.Id,
            CancellationToken.None);

        var result = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.DoesNotContain("No response text was produced", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ParseLiveSmokeBoolean(string? raw) {
        var value = (raw ?? string.Empty).Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static OpenAICompatibleHttpAuthMode ParseLiveSmokeAuthMode(string? raw) {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) {
            return OpenAICompatibleHttpAuthMode.Bearer;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase)) {
            return OpenAICompatibleHttpAuthMode.None;
        }

        if (value.Equals("basic", StringComparison.OrdinalIgnoreCase)) {
            return OpenAICompatibleHttpAuthMode.Basic;
        }

        return OpenAICompatibleHttpAuthMode.Bearer;
    }
}
