using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceToolListCategoryTests {
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void ResolveToolListCategory_ReturnsOtherWhenCategoryMissing(string? category) {
        var resolved = ChatServiceSession.ResolveToolListCategory(category);

        Assert.Equal("other", resolved);
    }

    [Theory]
    [InlineData("active_directory", "active-directory")]
    [InlineData("file system", "file-system")]
    [InlineData("reviewer__setup", "reviewer-setup")]
    [InlineData("eventlog", "eventlog")]
    [InlineData("  custom-pack  ", "custom-pack")]
    public void ResolveToolListCategory_NormalizesDeclaredCategoryWithoutPackSpecificMappings(string category, string expected) {
        var resolved = ChatServiceSession.ResolveToolListCategory(category);

        Assert.Equal(expected, resolved);
    }
}
