using System;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class NativeTransportStreamingPolicyTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServicePropagatesStreamingToNativeCopilot(bool streaming) {
        var options = new ServiceOptions { OpenAITransport = OpenAITransportKind.CopilotNative, OpenAIStreaming = streaming,
            Model = "account-model" };
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("BuildClientOptions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var client = (IntelligenceXClientOptions)method.Invoke(session, null)!;
        Assert.Equal(OpenAITransportKind.CopilotNative, client.TransportKind);
        Assert.Equal("account-model", client.DefaultModel);
        Assert.Equal(streaming, client.CopilotOptions.Streaming);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HostPropagatesStreamingToNativeCopilot(bool streaming) {
        var host = typeof(IntelligenceX.Chat.Host.Program);
        var optionsType = host.GetNestedType("ReplOptions", BindingFlags.NonPublic)!;
        var options = Activator.CreateInstance(optionsType, nonPublic: true)!;
        optionsType.GetProperty("OpenAITransport")!.SetValue(options, OpenAITransportKind.CopilotNative);
        optionsType.GetProperty("OpenAIStreaming")!.SetValue(options, streaming);
        optionsType.GetProperty("Model")!.SetValue(options, "account-model");
        var method = host.GetMethod("BuildTransportClientOptions", BindingFlags.NonPublic | BindingFlags.Static)!;
        var client = (IntelligenceXClientOptions)method.Invoke(null, new[] { options })!;
        Assert.Equal(OpenAITransportKind.CopilotNative, client.TransportKind);
        Assert.Equal("account-model", client.DefaultModel);
        Assert.Equal(streaming, client.CopilotOptions.Streaming);
    }
}
