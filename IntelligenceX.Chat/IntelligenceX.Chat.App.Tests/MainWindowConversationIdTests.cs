using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers stable client-provided conversation ids used by optimistic sidebar creation.
/// </summary>
public sealed class MainWindowConversationIdTests {
    /// <summary>
    /// Ensures optimistic client ids survive host-side conversation creation.
    /// </summary>
    [Fact]
    public void ResolveConversationCreationId_PreservesRequestedClientId() {
        const string requestedConversationId = "chat-client-sidebar-123";

        var actual = MainWindow.ResolveConversationCreationId(requestedConversationId);

        Assert.Equal(requestedConversationId, actual);
    }

    /// <summary>
    /// Ensures invalid reserved ids still fall back to generated chat ids.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("chat-system")]
    [InlineData(" CHAT-SYSTEM ")]
    [InlineData("system")]
    [InlineData("System")]
    [InlineData("chat-default")]
    [InlineData(" default ")]
    public void ResolveConversationCreationId_ReplacesBlankOrReservedIds(string requestedConversationId) {
        var actual = MainWindow.ResolveConversationCreationId(requestedConversationId);

        Assert.StartsWith("chat-", actual, StringComparison.Ordinal);
        Assert.NotEqual("chat-system", actual);
        Assert.NotEqual("chat-default", actual);
    }

    /// <summary>
    /// Ensures the client-supplied conversation-id trust boundary rejects every reserved alias variant.
    /// </summary>
    [Theory]
    [InlineData("chat-system")]
    [InlineData("system")]
    [InlineData("chat-default")]
    [InlineData("default")]
    public void IsReservedConversationCreationId_ReturnsTrue_ForReservedAliases(string requestedConversationId) {
        Assert.True(MainWindow.IsReservedConversationCreationId(requestedConversationId));
    }
}
