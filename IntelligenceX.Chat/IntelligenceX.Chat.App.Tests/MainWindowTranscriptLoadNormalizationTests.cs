using System;
using System.Reflection;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers persisted transcript normalization applied when conversations are loaded from state.
/// </summary>
public sealed class MainWindowTranscriptLoadNormalizationTests {
    private static readonly MethodInfo NormalizePersistedTranscriptTextMethod =
        typeof(MainWindow).GetMethod("NormalizePersistedTranscriptText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("NormalizePersistedTranscriptText method not found.");

    /// <summary>
    /// Ensures assistant transcript artifacts are normalized before they are re-persisted.
    /// </summary>
    [Fact]
    public void NormalizePersistedTranscriptText_RepairsAssistantArtifactsBeforePersistence() {
        var input = "- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers*";

        var repaired = InvokeNormalizePersistedTranscriptText("Assistant", input, out var wasRepaired);

        Assert.True(wasRepaired);
        Assert.Equal("- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers**", repaired);
    }

    /// <summary>
    /// Ensures user-authored markdown is preserved when loading persisted transcripts.
    /// </summary>
    [Fact]
    public void NormalizePersistedTranscriptText_DoesNotRewriteUserMarkdown() {
        var input = "- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers*";

        var repaired = InvokeNormalizePersistedTranscriptText("User", input, out var wasRepaired);

        Assert.False(wasRepaired);
        Assert.Equal(input, repaired);
    }

    private static string InvokeNormalizePersistedTranscriptText(string role, string text, out bool repaired) {
        var args = new object?[] { role, text, null };
        var result = Assert.IsType<string>(NormalizePersistedTranscriptTextMethod.Invoke(null, args));
        repaired = Assert.IsType<bool>(args[2]);
        return result;
    }
}
