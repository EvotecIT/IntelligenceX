using System;
using IntelligenceX.Chat.App.Launch;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for typed service launch argument construction.
/// </summary>
public sealed class ServiceLaunchArgumentsTests {
    /// <summary>
    /// Ensures pipe and parent-lifecycle flags are included in attached mode.
    /// </summary>
    [Fact]
    public void Build_IncludesLifecycleFlags_WhenNotDetached() {
        var args = ServiceLaunchArguments.Build("intelligencex.chat", detachedServiceMode: false, parentProcessId: 12345);

        Assert.Equal(new[] {
            "--pipe",
            "intelligencex.chat",
            "--exit-on-disconnect",
            "--parent-pid",
            "12345"
        }, args);
    }

    /// <summary>
    /// Ensures detached mode only carries the pipe argument pair.
    /// </summary>
    [Fact]
    public void Build_OmitsLifecycleFlags_WhenDetached() {
        var args = ServiceLaunchArguments.Build("intelligencex.chat", detachedServiceMode: true, parentProcessId: 12345);

        Assert.Equal(new[] {
            "--pipe",
            "intelligencex.chat"
        }, args);
    }

    /// <summary>
    /// Ensures an empty pipe name is rejected.
    /// </summary>
    [Fact]
    public void Build_Throws_WhenPipeNameMissing() {
        Assert.Throws<ArgumentException>(() => ServiceLaunchArguments.Build("  ", detachedServiceMode: false, parentProcessId: 42));
    }

    /// <summary>
    /// Ensures profile/runtime overrides are emitted when provided.
    /// </summary>
    [Fact]
    public void Build_IncludesProfileAndTransportOverrides_WhenConfigured() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                LoadProfileName = "default",
                SaveProfileName = "default",
                Model = "gpt-4.1-mini",
                OpenAITransport = "compatible-http",
                OpenAIBaseUrl = "http://127.0.0.1:11434",
                OpenAIApiKey = "token",
                OpenAIStreaming = true,
                OpenAIAllowInsecureHttp = true
            });

        Assert.Equal(new[] {
            "--pipe",
            "intelligencex.chat",
            "--profile",
            "default",
            "--save-profile",
            "default",
            "--model",
            "gpt-4.1-mini",
            "--openai-transport",
            "compatible-http",
            "--openai-base-url",
            "http://127.0.0.1:11434",
            "--openai-api-key",
            "token",
            "--openai-stream",
            "--openai-allow-insecure-http"
        }, args);
    }

    /// <summary>
    /// Ensures explicit API-key clearing emits a dedicated clear flag.
    /// </summary>
    [Fact]
    public void Build_IncludesClearApiKeyFlag_WhenRequested() {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions {
                LoadProfileName = "default",
                SaveProfileName = "default",
                OpenAITransport = "compatible-http",
                OpenAIBaseUrl = "http://127.0.0.1:1234/v1",
                ClearOpenAIApiKey = true
            });

        Assert.DoesNotContain("--openai-api-key", args);
        Assert.Contains("--openai-clear-api-key", args);
    }

    /// <summary>
    /// Ensures unknown transport values are rejected.
    /// </summary>
    [Fact]
    public void Build_Throws_WhenTransportUnknown() {
        Assert.Throws<ArgumentException>(() => ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions { OpenAITransport = "invalid" }));
    }

    /// <summary>
    /// Ensures Copilot transport aliases normalize to copilot-cli.
    /// </summary>
    [Theory]
    [InlineData("copilot")]
    [InlineData("copilot-cli")]
    [InlineData("github-copilot")]
    [InlineData("githubcopilot")]
    public void Build_NormalizesCopilotTransportAliases(string inputTransport) {
        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 12345,
            new ServiceLaunchArguments.ProfileOptions { OpenAITransport = inputTransport });

        var transportIndex = -1;
        for (var i = 0; i < args.Count; i++) {
            if (!string.Equals(args[i], "--openai-transport", StringComparison.Ordinal)) {
                continue;
            }
            transportIndex = i;
            break;
        }
        Assert.True(transportIndex >= 0);
        Assert.True(transportIndex + 1 < args.Count);
        Assert.Equal("copilot-cli", args[transportIndex + 1]);
    }
}
