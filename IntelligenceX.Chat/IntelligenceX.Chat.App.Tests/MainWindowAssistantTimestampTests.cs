using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards assistant timestamp updates so transcript meta reflects first visible assistant output.
/// </summary>
public sealed class MainWindowAssistantTimestampTests {
    /// <summary>
    /// Ensures placeholder assistant timestamps move to the first visible assistant output time.
    /// </summary>
    [Fact]
    public void ResolveAssistantTimestampForUpdate_UpdatesTimestampWhenAssistantTextFirstAppears() {
        var current = new DateTime(2026, 2, 22, 9, 32, 30, DateTimeKind.Local);
        var now = new DateTime(2026, 2, 22, 9, 32, 47, DateTimeKind.Local);

        var resolved = MainWindow.ResolveAssistantTimestampForUpdate(
            currentTimestamp: current,
            existingText: string.Empty,
            nextText: "Good morning, Przemek!",
            nowLocal: now);

        Assert.Equal(now, resolved);
    }

    /// <summary>
    /// Ensures ongoing streaming updates keep the original first-output assistant timestamp.
    /// </summary>
    [Fact]
    public void ResolveAssistantTimestampForUpdate_KeepsTimestampWhileStreamingSubsequentDeltas() {
        var current = new DateTime(2026, 2, 22, 9, 32, 47, DateTimeKind.Local);
        var now = new DateTime(2026, 2, 22, 9, 32, 50, DateTimeKind.Local);

        var resolved = MainWindow.ResolveAssistantTimestampForUpdate(
            currentTimestamp: current,
            existingText: "Good morning,",
            nextText: "Good morning, Przemek!",
            nowLocal: now);

        Assert.Equal(current, resolved);
    }

    /// <summary>
    /// Ensures timestamp is unchanged when assistant text remains empty/whitespace.
    /// </summary>
    [Fact]
    public void ResolveAssistantTimestampForUpdate_KeepsTimestampWhenNoAssistantTextIsAvailable() {
        var current = new DateTime(2026, 2, 22, 9, 32, 30, DateTimeKind.Local);
        var now = new DateTime(2026, 2, 22, 9, 32, 55, DateTimeKind.Local);

        var resolved = MainWindow.ResolveAssistantTimestampForUpdate(
            currentTimestamp: current,
            existingText: string.Empty,
            nextText: "   ",
            nowLocal: now);

        Assert.Equal(current, resolved);
    }

}
