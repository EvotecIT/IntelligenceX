using System;
using System.Reflection;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatSchemaRecoveryFallbackTests {
    private static readonly Type ChatServiceSessionType =
        Type.GetType("IntelligenceX.Chat.Service.ChatServiceSession, IntelligenceX.Chat.Service")
        ?? throw new InvalidOperationException("ChatServiceSession type not found.");

    private static readonly MethodInfo ServiceShouldRetryWithoutToolsMethod = ChatServiceSessionType.GetMethod(
        "ShouldRetryWithoutTools",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ChatServiceSession.ShouldRetryWithoutTools not found.");

    [Fact]
    public void ServiceShouldRetryWithoutTools_RetriesOnContextWindowFailure() {
        var options = BuildOptionsWithTools();
        var ex = new InvalidOperationException("Chat request failed (400): {\"error\":\"Cannot truncate prompt with n_keep (12378) >= n_ctx (4096)\"}");

        var shouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryWithoutTools_DoesNotRetryWhenNoTools() {
        var options = new ChatOptions {
            Tools = null,
            ToolChoice = null
        };
        var ex = new InvalidOperationException("maximum context length exceeded");

        var shouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryWithoutTools_RetriesOnStructuredNativeToolSchemaError() {
        var options = BuildOptionsWithTools();
        var ex = new InvalidOperationException("request failed");
        ex.Data["openai:native_transport"] = true;
        ex.Data["openai:error_code"] = "missing_required_parameter";
        ex.Data["openai:error_param"] = "tools[0].name";

        var shouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryWithoutTools_RetriesOnStructuredNativeContextLengthError() {
        var options = BuildOptionsWithTools();
        var ex = new InvalidOperationException("request failed");
        ex.Data["openai:native_transport"] = true;
        ex.Data["openai:error_code"] = "context_length_exceeded";

        var shouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryWithoutTools_DoesNotRetryOnStructuredNativeNonRetryableError() {
        var options = BuildOptionsWithTools();
        var ex = new InvalidOperationException("request failed");
        ex.Data["openai:native_transport"] = true;
        ex.Data["openai:error_code"] = "invalid_request_error";
        ex.Data["openai:error_param"] = "input";

        var shouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);

        Assert.False(shouldRetry);
    }

    private static ChatOptions BuildOptionsWithTools() {
        return new ChatOptions {
            Tools = new[] { new ToolDefinition("fs_list", "List files") },
            ToolChoice = ToolChoice.Auto
        };
    }

    private static bool InvokeShouldRetry(MethodInfo method, Exception ex, ChatOptions options) {
        var result = method.Invoke(null, new object?[] { ex, options });
        return Assert.IsType<bool>(result);
    }
}
