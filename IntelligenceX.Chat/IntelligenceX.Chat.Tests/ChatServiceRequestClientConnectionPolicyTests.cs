using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceRequestClientConnectionPolicyTests {
    public static IEnumerable<object[]> RequestConnectionPolicyCases() {
        yield return new object[] { new HelloRequest { RequestId = "req_hello" }, false };
        yield return new object[] { new ListToolsRequest { RequestId = "req_tools" }, false };
        yield return new object[] { new CheckToolHealthRequest { RequestId = "req_health" }, false };
        yield return new object[] { new ListProfilesRequest { RequestId = "req_profiles" }, false };
        yield return new object[] { new SetProfileRequest { RequestId = "req_profile_set", ProfileName = "local" }, false };
        yield return new object[] { new ApplyRuntimeSettingsRequest { RequestId = "req_runtime_apply", OpenAITransport = "native" }, false };
        yield return new object[] { new EnsureLoginRequest { RequestId = "req_login" }, false };
        yield return new object[] { new StartChatGptLoginRequest { RequestId = "req_login_start", TimeoutSeconds = 120 }, false };
        yield return new object[] { new ListModelsRequest { RequestId = "req_models" }, true };
        yield return new object[] { new ChatRequest { RequestId = "req_chat", Text = "hello" }, true };
    }

    public static IEnumerable<object[]> RequestToolingBootstrapPolicyCases() {
        yield return new object[] { new HelloRequest { RequestId = "req_hello" }, false };
        yield return new object[] { new EnsureLoginRequest { RequestId = "req_login" }, false };
        yield return new object[] { new ListProfilesRequest { RequestId = "req_profiles" }, false };
        yield return new object[] { new ListToolsRequest { RequestId = "req_tools" }, true };
        yield return new object[] { new CheckToolHealthRequest { RequestId = "req_health" }, true };
        yield return new object[] { new InvokeToolRequest { RequestId = "req_invoke", ToolName = "system_info", ArgumentsJson = "{}" }, true };
        yield return new object[] { new SetProfileRequest { RequestId = "req_profile_set", ProfileName = "local" }, true };
        yield return new object[] { new ApplyRuntimeSettingsRequest { RequestId = "req_runtime_apply", OpenAITransport = "native" }, true };
        yield return new object[] { new ChatRequest { RequestId = "req_chat", Text = "hello" }, true };
    }

    [Theory]
    [MemberData(nameof(RequestConnectionPolicyCases))]
    public void RequestRequiresConnectedClient_ReturnsExpected(ChatServiceRequest request, bool expected) {
        var result = ChatServiceSession.RequestRequiresConnectedClient(request);

        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(RequestToolingBootstrapPolicyCases))]
    public void RequestRequiresToolingBootstrap_ReturnsExpected(ChatServiceRequest request, bool expected) {
        var result = ChatServiceSession.RequestRequiresToolingBootstrap(request);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildClientConnectFailureMessage_ForListModels_IncludesContext() {
        var message = ChatServiceSession.BuildClientConnectFailureMessage(
            new ListModelsRequest { RequestId = "req_models" },
            new InvalidOperationException("Copilot CLI not found on PATH."));

        Assert.Contains("listing models", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Copilot CLI not found on PATH.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClientConnectFailureMessage_ForChat_IncludesContext() {
        var message = ChatServiceSession.BuildClientConnectFailureMessage(
            new ChatRequest { RequestId = "req_chat", Text = "hello" },
            new InvalidOperationException("Provider unavailable"));

        Assert.Contains("for chat request", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Provider unavailable", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClientConnectFailureMessage_UsesFallbackDetail_WhenExceptionMessageEmpty() {
        var message = ChatServiceSession.BuildClientConnectFailureMessage(
            new ListModelsRequest { RequestId = "req_models" },
            new Exception(string.Empty));

        Assert.Contains("Runtime provider connection failed.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildToolingBootstrapFailureMessage_ForListTools_IncludesContext() {
        var message = ChatServiceSession.BuildToolingBootstrapFailureMessage(
            new ListToolsRequest { RequestId = "req_tools" },
            new InvalidOperationException("Plugin manifest invalid"));

        Assert.Contains("tool catalog", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plugin manifest invalid", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    public void ShouldBypassToolingBootstrapWaitForListTools_ReturnsExpectedValue(
        bool isListToolsRequest,
        bool startupToolingBootstrapCompletedSuccessfully,
        bool hasCachedToolCatalog,
        bool expected) {
        var shouldBypass = ChatServiceSession.ShouldBypassToolingBootstrapWaitForListTools(
            isListToolsRequest: isListToolsRequest,
            startupToolingBootstrapCompletedSuccessfully: startupToolingBootstrapCompletedSuccessfully,
            hasCachedToolCatalog: hasCachedToolCatalog);

        Assert.Equal(expected, shouldBypass);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldUseCachedToolCatalogFallbackForListTools_ReturnsExpectedValue(
        bool startupToolingBootstrapInProgress,
        bool expected) {
        var shouldUseCachedFallback = ChatServiceSession.ShouldUseCachedToolCatalogFallbackForListTools(
            startupToolingBootstrapInProgress: startupToolingBootstrapInProgress);

        Assert.Equal(expected, shouldUseCachedFallback);
    }
}
