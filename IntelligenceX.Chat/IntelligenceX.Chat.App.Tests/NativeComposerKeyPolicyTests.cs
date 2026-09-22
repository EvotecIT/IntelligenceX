using IntelligenceX.Chat.App.Native;
using Windows.System;
using Windows.UI.Core;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the native composer keyboard contract.
/// </summary>
public sealed class NativeComposerKeyPolicyTests {
    /// <summary>Enter sends while Shift+Enter remains available for multiline input.</summary>
    [Theory]
    [InlineData(VirtualKey.Enter, CoreVirtualKeyStates.None, true)]
    [InlineData(VirtualKey.Enter, CoreVirtualKeyStates.Down, false)]
    [InlineData(VirtualKey.A, CoreVirtualKeyStates.None, false)]
    public void ShouldSendComposerKey_UsesChatComposerConvention(
        VirtualKey key,
        CoreVirtualKeyStates shiftState,
        bool expected) {
        Assert.Equal(expected, NativeChatWindow.ShouldSendComposerKey(key, shiftState));
    }
}
