using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests native sidebar item filtering without constructing WinUI controls.
/// </summary>
public sealed class NativeSidebarItemTests {
    /// <summary>
    /// Ensures empty searches keep all native sidebar entries visible.
    /// </summary>
    [Fact]
    public void Matches_EmptySearch_ReturnsTrue() {
        Assert.All(NativeSidebarItem.All, item => Assert.True(item.Matches("  ")));
    }

    /// <summary>
    /// Ensures sidebar search matches project, chat, and pinned artifact metadata.
    /// </summary>
    [Theory]
    [InlineData("dkim", "DNS / Mail Auth")]
    [InlineData("exception", "MFA exceptions")]
    [InlineData("topology", "AD Topology")]
    [InlineData("74 rows", "Directory Objects")]
    public void Matches_SearchesVisibleAndWorkspaceMetadata(string query, string expectedTitle) {
        Assert.Contains(NativeSidebarItem.All, entry => entry.Matches(query) && entry.Title == expectedTitle);
    }

    /// <summary>
    /// Ensures unrelated searches do not keep decorative rows visible.
    /// </summary>
    [Fact]
    public void Matches_UnrelatedSearch_ReturnsFalse() {
        Assert.DoesNotContain(NativeSidebarItem.All, item => item.Matches("not-a-real-project"));
    }
}
