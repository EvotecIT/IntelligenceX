using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for assistant-to-app persistent memory envelope parsing.
/// </summary>
public sealed class MemoryModelProtocolTests {
    /// <summary>
    /// Ensures a valid ix_memory envelope is parsed and stripped from visible text.
    /// </summary>
    [Fact]
    public void TryExtractLastMemoryUpdate_ParsesUpdateAndStripsEnvelope() {
        var text = """
                   Here is what I will remember.

                   ```ix_memory
                   {"upserts":[{"fact":"User prefers concise AD summaries","weight":4,"tags":["preference","ad"]}],"deleteFacts":["legacy-note"]}
                   ```
                   """;

        var ok = MemoryModelProtocol.TryExtractLastMemoryUpdate(text, out var update, out var cleaned);

        Assert.True(ok);
        Assert.Single(update.Upserts);
        Assert.Equal("User prefers concise AD summaries", update.Upserts[0].Fact);
        Assert.Equal(4, update.Upserts[0].Weight);
        Assert.Equal(new[] { "preference", "ad" }, update.Upserts[0].Tags);
        Assert.Equal(new[] { "legacy-note" }, update.DeleteFacts);
        Assert.DoesNotContain("```ix_memory", cleaned);
    }

    /// <summary>
    /// Ensures parser returns false and preserves text when no ix_memory envelope exists.
    /// </summary>
    [Fact]
    public void TryExtractLastMemoryUpdate_ReturnsFalseWhenNoEnvelope() {
        var ok = MemoryModelProtocol.TryExtractLastMemoryUpdate("No memory block here.", out var update, out var cleaned);

        Assert.False(ok);
        Assert.Empty(update.Upserts);
        Assert.Empty(update.DeleteFacts);
        Assert.Equal("No memory block here.", cleaned);
    }
}
