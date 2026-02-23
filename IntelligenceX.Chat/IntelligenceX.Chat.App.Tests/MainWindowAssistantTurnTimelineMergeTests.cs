using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies final timeline merge behavior keeps live trace when final envelope omits timeline data.
/// </summary>
public sealed class MainWindowAssistantTurnTimelineMergeTests {
    [Fact]
    public void MergeFinalTimeline_NullFinalTimeline_PreservesExistingTimeline() {
        var existing = new List<string> { "plan", "tool route", "phase wait" };

        var changed = InvokeMergeFinalTimeline(existing, null);

        Assert.False(changed);
        Assert.Equal(new[] { "plan", "tool route", "phase wait" }, existing);
    }

    [Fact]
    public void MergeFinalTimeline_EmptyFinalTimeline_PreservesExistingTimeline() {
        var existing = new List<string> { "plan", "tool route", "phase wait" };

        var changed = InvokeMergeFinalTimeline(existing, Array.Empty<string>());

        Assert.False(changed);
        Assert.Equal(new[] { "plan", "tool route", "phase wait" }, existing);
    }

    [Fact]
    public void MergeFinalTimeline_NonEmptyFinalTimeline_ReplacesExistingTimeline() {
        var existing = new List<string> { "plan", "phase wait" };

        var changed = InvokeMergeFinalTimeline(existing, new[] { "plan", "execute", "review" });

        Assert.True(changed);
        Assert.Equal(new[] { "plan", "execute", "review" }, existing);
    }

    private static bool InvokeMergeFinalTimeline(List<string> existingTimeline, IReadOnlyList<string>? finalTimeline) {
        return MainWindow.MergeFinalTimeline(existingTimeline, finalTimeline);
    }
}
