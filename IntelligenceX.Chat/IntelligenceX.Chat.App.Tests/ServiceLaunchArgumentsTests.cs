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
}
