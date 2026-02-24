using System;
using System.Reflection;
using System.Threading;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
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

    private static readonly MethodInfo ServiceShouldRetryModelPhaseAttemptMethod = ChatServiceSessionType.GetMethod(
        "ShouldRetryModelPhaseAttempt",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ChatServiceSession.ShouldRetryModelPhaseAttempt not found.");

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

    [Fact]
    public void ServiceShouldRetryWithoutTools_RetriesWhenContextFailureIsInInnerException() {
        var options = BuildOptionsWithTools();
        var ex = new AggregateException(
            "provider batch failed",
            new InvalidOperationException(
                "Chat request failed (400): {\"error\":\"Cannot truncate prompt with n_keep (8192) >= n_ctx (4096)\"}"));

        var shouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void SharedClassifier_MatchesServiceResult_ForStructuredSchemaError() {
        var options = BuildOptionsWithTools();
        var ex = new InvalidOperationException("request failed");
        ex.Data["openai:native_transport"] = true;
        ex.Data["openai:error_code"] = "missing_required_parameter";
        ex.Data["openai:error_param"] = "tools[0].name";

        var serviceShouldRetry = InvokeShouldRetry(ServiceShouldRetryWithoutToolsMethod, ex, options);
        var sharedShouldRetry = ToolSchemaRecoveryClassifier.ShouldRetryWithoutTools(ex, options);

        Assert.Equal(serviceShouldRetry, sharedShouldRetry);
    }

    [Fact]
    public void SharedClassifier_RetriesWhenContextFailureIsInInnerException() {
        var options = BuildOptionsWithTools();
        var ex = new InvalidOperationException(
            "transport failed",
            new InvalidOperationException("maximum context length exceeded"));

        var shouldRetry = ToolSchemaRecoveryClassifier.ShouldRetryWithoutTools(ex, options);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void SharedClassifier_DoesNotRetry_WhenToolsAreMissing() {
        var options = new ChatOptions {
            Tools = null,
            ToolChoice = null
        };
        var ex = new InvalidOperationException("maximum context length exceeded");

        var shouldRetry = ToolSchemaRecoveryClassifier.ShouldRetryWithoutTools(ex, options);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_RetriesOnTransportDrop() {
        var ex = new InvalidOperationException("connection reset by peer");

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_RetriesOnToolOutputPairingGap() {
        var ex = new InvalidOperationException("No tool call found for custom tool call output with call_id host_next_action_abc.");

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_RetriesOnMissingFunctionCallOutputGap() {
        var ex = new InvalidOperationException("No tool output found for function call host_next_action_abc.");

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_RetriesOnMissingCustomToolCallOutputGap() {
        var ex = new InvalidOperationException("No tool output found for custom tool call host_next_action_abc.");

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_RetriesOnNestedFunctionCallOutputReferenceGap() {
        var ex = new InvalidOperationException(
            "model step failed",
            new InvalidOperationException(
                "assistant projection failed",
                new InvalidOperationException("No tool call found for function call output with call_id host_next_action_abc.")));

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_RetriesOnNestedFunctionCallOutputGap() {
        var ex = new InvalidOperationException(
            "model step failed",
            new InvalidOperationException("No tool output found for function call host_next_action_abc."));

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_DoesNotRetryAuthenticationErrors() {
        var ex = new OpenAIAuthenticationRequiredException("auth required");

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ServiceShouldRetryModelPhaseAttempt_DoesNotRetryAfterLastAttempt() {
        var ex = new InvalidOperationException("connection refused");

        var shouldRetry = InvokeShouldRetryModelPhaseAttempt(
            ServiceShouldRetryModelPhaseAttemptMethod,
            ex,
            attempt: 1,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

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

    private static bool InvokeShouldRetryModelPhaseAttempt(
        MethodInfo method,
        Exception ex,
        int attempt,
        int maxAttempts,
        CancellationToken cancellationToken) {
        var result = method.Invoke(null, new object?[] { ex, attempt, maxAttempts, cancellationToken });
        return Assert.IsType<bool>(result);
    }
}
