using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies final timeline merge behavior keeps live trace when final envelope omits timeline data.
/// </summary>
public sealed class MainWindowAssistantTurnTimelineMergeTests {
    /// <summary>
    /// Ensures a missing final timeline does not clobber already collected live trace entries.
    /// </summary>
    [Fact]
    public void MergeFinalTimeline_NullFinalTimeline_PreservesExistingTimeline() {
        var existing = new List<string> { "plan", "tool route", "phase wait" };

        var changed = InvokeMergeFinalTimeline(existing, null);

        Assert.False(changed);
        Assert.Equal(new[] { "plan", "tool route", "phase wait" }, existing);
    }

    /// <summary>
    /// Ensures an empty final timeline keeps prior live timeline values intact.
    /// </summary>
    [Fact]
    public void MergeFinalTimeline_EmptyFinalTimeline_PreservesExistingTimeline() {
        var existing = new List<string> { "plan", "tool route", "phase wait" };

        var changed = InvokeMergeFinalTimeline(existing, Array.Empty<string>());

        Assert.False(changed);
        Assert.Equal(new[] { "plan", "tool route", "phase wait" }, existing);
    }

    /// <summary>
    /// Ensures a populated final timeline replaces provisional/live timeline state.
    /// </summary>
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
