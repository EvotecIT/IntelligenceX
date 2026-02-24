using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards profile-state defaults for transcript visibility controls.
/// </summary>
public sealed class ChatAppStateDefaultsTests {
    /// <summary>
    /// Ensures new profile state defaults keep draft/thinking bubbles hidden and turn trace disabled.
    /// </summary>
    [Fact]
    public void Constructor_DefaultsTranscriptDebugVisibilityToOff() {
        var state = new ChatAppState();

        Assert.False(state.ShowAssistantTurnTrace);
        Assert.False(state.ShowAssistantDraftBubbles);
    }
}
