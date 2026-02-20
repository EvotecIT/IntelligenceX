using System;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards stale thread recovery when runtime transport switches and provider-local thread registries differ.
/// </summary>
public sealed class ChatServiceThreadRecoveryPolicyTests {
    /// <summary>
    /// Ensures compatible-http missing-thread failures trigger thread recovery.
    /// </summary>
    [Fact]
    public void ShouldRecoverMissingTransportThread_ReturnsTrue_ForCompatibleHttpNotFoundMessage() {
        var ex = new InvalidOperationException("Thread '8faf6e71d03c481bb7ea125a0d2a844d' not found in CompatibleHttp transport.");

        var shouldRecover = ChatServiceSession.ShouldRecoverMissingTransportThread(ex);

        Assert.True(shouldRecover);
    }

    /// <summary>
    /// Ensures copilot-cli missing-thread failures trigger thread recovery.
    /// </summary>
    [Fact]
    public void ShouldRecoverMissingTransportThread_ReturnsTrue_ForCopilotCliNotFoundMessage() {
        var ex = new InvalidOperationException("Thread 'abc' was not found in Copilot CLI transport.");

        var shouldRecover = ChatServiceSession.ShouldRecoverMissingTransportThread(ex);

        Assert.True(shouldRecover);
    }

    /// <summary>
    /// Ensures unrelated errors do not trigger automatic thread recovery.
    /// </summary>
    [Fact]
    public void ShouldRecoverMissingTransportThread_ReturnsFalse_ForUnrelatedMessage() {
        var ex = new InvalidOperationException("Profile not found: default");

        var shouldRecover = ChatServiceSession.ShouldRecoverMissingTransportThread(ex);

        Assert.False(shouldRecover);
    }
}
