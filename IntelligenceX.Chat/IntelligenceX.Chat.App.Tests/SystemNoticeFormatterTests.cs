using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for system notice formatting.
/// </summary>
public sealed class SystemNoticeFormatterTests {
    /// <summary>
    /// Ensures notices with detail include that detail.
    /// </summary>
    [Fact]
    public void Format_UsesDetail_ForDetailedNotices() {
        var text = SystemNoticeFormatter.Format(SystemNotice.ConnectFailed("Timed out waiting for service pipe."));
        Assert.Equal("Couldn't connect to local runtime: Timed out waiting for service pipe.", text);
    }

    /// <summary>
    /// Ensures notices without detail use fallback text.
    /// </summary>
    [Fact]
    public void Format_UsesUnknownFallback_WhenDetailMissing() {
        var text = SystemNoticeFormatter.Format(SystemNotice.StateSaveFailed(" "));
        Assert.Equal("State save failed: Unknown error.", text);
    }

    /// <summary>
    /// Ensures fixed notices remain stable.
    /// </summary>
    [Fact]
    public void Format_UsesStableValues_ForFixedNotices() {
        Assert.Equal("Local runtime is unavailable.", SystemNoticeFormatter.Format(SystemNotice.ServiceSidecarUnavailable()));
        Assert.Equal("Export failed: missing rows payload.", SystemNoticeFormatter.Format(SystemNotice.ExportMissingRowsPayload()));
        Assert.Equal("[service] exited", SystemNoticeFormatter.Format(SystemNotice.ServiceExited()));
        Assert.Equal(
            "Prompt queued for retry. Use **Switch Account** in the top-right menu; after sign-in, the prompt will run automatically.",
            SystemNoticeFormatter.Format(SystemNotice.PromptQueuedAfterUsageLimit()));
        Assert.Equal(
            "Prompt queued for retry because ChatGPT (przemyslaw.klys+openai@evotec.pl) hit its usage limit. Use **Switch Account** in the top-right menu; after sign-in, the prompt will run automatically.",
            SystemNoticeFormatter.Format(SystemNotice.PromptQueuedAfterUsageLimit("ChatGPT (przemyslaw.klys+openai@evotec.pl)")));
    }

    /// <summary>
    /// Ensures service error notice keeps both error and code.
    /// </summary>
    [Fact]
    public void Format_ServiceError_UsesErrorAndCode() {
        var text = SystemNoticeFormatter.Format(SystemNotice.ServiceError("boom", "chat_failed"));
        Assert.Equal("service error: boom (chat_failed)", text);
    }
}
