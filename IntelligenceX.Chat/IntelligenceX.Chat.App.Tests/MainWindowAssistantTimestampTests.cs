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

    /// <summary>
    /// Ensures assistant updates replace the trailing assistant entry when it is the latest row.
    /// </summary>
    [Fact]
    public void ResolveAssistantReplaceIndexForUpdate_ReturnsLastAssistantWhenItIsTrailingEntry() {
        var messages = new[] {
            ("User", "Hello", DateTime.Now, (string?)null),
            ("Assistant", "Hi!", DateTime.Now, (string?)null)
        };

        var index = MainWindow.ResolveAssistantReplaceIndexForUpdate(messages);

        Assert.Equal(1, index);
    }

    /// <summary>
    /// Ensures non-user metadata rows after assistant output still update the same assistant bubble.
    /// </summary>
    [Fact]
    public void ResolveAssistantReplaceIndexForUpdate_ReusesAssistantWhenOnlySystemAndToolsFollow() {
        var messages = new[] {
            ("User", "Check replication", DateTime.Now, (string?)null),
            ("Assistant", "Running checks...", DateTime.Now, (string?)null),
            ("System", "route strategy weighted", DateTime.Now, (string?)null),
            ("Tools", "ad_replication_summary output", DateTime.Now, (string?)null)
        };

        var index = MainWindow.ResolveAssistantReplaceIndexForUpdate(messages);

        Assert.Equal(1, index);
    }

    /// <summary>
    /// Ensures assistant replacement does not cross a newer user turn boundary.
    /// </summary>
    [Fact]
    public void ResolveAssistantReplaceIndexForUpdate_DoesNotReuseAssistantAcrossNewUserTurn() {
        var messages = new[] {
            ("User", "Check replication", DateTime.Now, (string?)null),
            ("Assistant", "Replication healthy.", DateTime.Now, (string?)null),
            ("User", "Now check ldap", DateTime.Now, (string?)null),
            ("System", "route strategy weighted", DateTime.Now, (string?)null)
        };

        var index = MainWindow.ResolveAssistantReplaceIndexForUpdate(messages);

        Assert.Equal(-1, index);
    }
}
