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
        Assert.NotNull(state.PendingTurns);
        Assert.NotNull(state.QueuedTurnsAfterLogin);
        Assert.Empty(state.PendingTurns);
        Assert.Empty(state.QueuedTurnsAfterLogin);
    }

    /// <summary>
    /// Ensures old persisted disabled image-generation state keeps emitting an explicit disable override.
    /// </summary>
    [Fact]
    public void ResolveLocalProviderImageGenerationOverrideActive_MigratesLegacyPersistedDisable() {
        var state = new ChatAppState {
            LocalProviderImageGenerationEnabled = false,
            LocalProviderImageGenerationOverrideActive = false,
            LocalProviderImageGenerationOverrideActiveWasPresent = false
        };

        Assert.True(MainWindow.ResolveLocalProviderImageGenerationOverrideActive(state, isLoadedProfile: true));
    }

    /// <summary>
    /// Ensures new persisted state can intentionally keep image-generation override unset.
    /// </summary>
    [Fact]
    public void ResolveLocalProviderImageGenerationOverrideActive_PreservesNewPersistedUnsetDisable() {
        var state = new ChatAppState {
            LocalProviderImageGenerationEnabled = false,
            LocalProviderImageGenerationOverrideActive = false,
            LocalProviderImageGenerationOverrideActiveWasPresent = true
        };

        Assert.False(MainWindow.ResolveLocalProviderImageGenerationOverrideActive(state, isLoadedProfile: true));
    }
}
