using System;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests sample-mode transcript generation without constructing WinUI controls.
/// </summary>
public sealed class NativeSampleTranscriptFactoryTests {
    /// <summary>
    /// Ensures sample transcripts are generated as user and assistant turns for every sidebar item.
    /// </summary>
    [Fact]
    public void Create_ReturnsRenderablePairForEverySidebarItem() {
        var now = new DateTimeOffset(2026, 6, 13, 20, 30, 0, TimeSpan.Zero);

        foreach (var item in NativeSidebarItem.All) {
            var transcript = NativeSampleTranscriptFactory.Create(item, now);

            Assert.Equal(2, transcript.Count);
            Assert.True(transcript[0].IsUser);
            Assert.True(transcript[1].IsAssistant);
            Assert.Equal("Complete", transcript[1].Status);
            Assert.NotEmpty(transcript[1].Content);
        }
    }

    /// <summary>
    /// Ensures non-default sidebar selections do not silently reuse the default risky-admin sample.
    /// </summary>
    [Fact]
    public void Create_NonDefaultItemsUseDistinctPromptText() {
        const string defaultPrompt = "Review risky inactive admins and show the evidence as tables and diagrams.";

        foreach (var item in NativeSidebarItem.All) {
            var transcript = NativeSampleTranscriptFactory.Create(item, DateTimeOffset.Now);
            if (item.Id == NativeSidebarItem.Default.Id) {
                Assert.Equal(defaultPrompt, transcript[0].Text);
            } else {
                Assert.NotEqual(defaultPrompt, transcript[0].Text);
            }
        }
    }

    /// <summary>
    /// Ensures the pinned topology sample exercises native visual projection.
    /// </summary>
    [Fact]
    public void Create_AdTopology_ProjectsVisualContent() {
        var item = Assert.Single(NativeSidebarItem.All, entry => entry.Id == "artifact-ad-topology");

        var transcript = NativeSampleTranscriptFactory.Create(item, DateTimeOffset.Now);

        Assert.Contains(transcript[1].Content, content => content.Visual != null);
    }

    /// <summary>
    /// Ensures table-focused samples exercise native table projection.
    /// </summary>
    [Fact]
    public void Create_DirectoryObjects_ProjectsTableContent() {
        var item = Assert.Single(NativeSidebarItem.All, entry => entry.Id == "artifact-directory-objects");

        var transcript = NativeSampleTranscriptFactory.Create(item, DateTimeOffset.Now);

        Assert.Contains(transcript[1].Content, content => content.Table != null);
    }
}
