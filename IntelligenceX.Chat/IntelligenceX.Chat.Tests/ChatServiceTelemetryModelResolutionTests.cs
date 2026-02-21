using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceTelemetryModelResolutionTests {
    [Fact]
    public void ResolveEffectiveTurnModelForTelemetry_UsesResolvedModel_WhenAvailable() {
        var model = ChatServiceSession.ResolveEffectiveTurnModelForTelemetry(
            resolvedModel: "gpt-5.3-codex-spark",
            requestedModel: "gpt-5.3-codex",
            runtimeDefaultModel: "gpt-5.3");

        Assert.Equal("gpt-5.3-codex-spark", model);
    }

    [Fact]
    public void ResolveEffectiveTurnModelForTelemetry_FallsBackToRequestedModel() {
        var model = ChatServiceSession.ResolveEffectiveTurnModelForTelemetry(
            resolvedModel: " ",
            requestedModel: "  gpt-5.3-codex  ",
            runtimeDefaultModel: "gpt-5.3");

        Assert.Equal("gpt-5.3-codex", model);
    }

    [Fact]
    public void ResolveEffectiveTurnModelForTelemetry_FallsBackToRuntimeDefault() {
        var model = ChatServiceSession.ResolveEffectiveTurnModelForTelemetry(
            resolvedModel: null,
            requestedModel: null,
            runtimeDefaultModel: "  gpt-5.3  ");

        Assert.Equal("gpt-5.3", model);
    }
}
