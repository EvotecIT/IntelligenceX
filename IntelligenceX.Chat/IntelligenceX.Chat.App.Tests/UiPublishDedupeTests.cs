using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies dedupe cache semantics for UI publish payloads.
/// </summary>
public sealed class UiPublishDedupeTests {
    /// <summary>
    /// Ensures first publish for a payload is accepted and marks the cache.
    /// </summary>
    [Fact]
    public void TryBeginPublish_FirstPayload_ReturnsTrueAndCachesPayload() {
        var sync = new object();
        string? lastPayload = null;

        var accepted = UiPublishDedupe.TryBeginPublish(sync, ref lastPayload, "payload-a");

        Assert.True(accepted);
        Assert.Equal("payload-a", lastPayload);
    }

    /// <summary>
    /// Ensures duplicate payloads are suppressed when the cache already matches.
    /// </summary>
    [Fact]
    public void TryBeginPublish_DuplicatePayload_ReturnsFalse() {
        var sync = new object();
        string? lastPayload = "payload-a";

        var accepted = UiPublishDedupe.TryBeginPublish(sync, ref lastPayload, "payload-a");

        Assert.False(accepted);
        Assert.Equal("payload-a", lastPayload);
    }

    /// <summary>
    /// Ensures rollback clears cache marker when publish failed for the current payload.
    /// </summary>
    [Fact]
    public void RollbackFailedPublish_MatchingPayload_ClearsCache() {
        var sync = new object();
        string? lastPayload = "payload-a";

        UiPublishDedupe.RollbackFailedPublish(sync, ref lastPayload, "payload-a");

        Assert.Null(lastPayload);
    }

    /// <summary>
    /// Ensures rollback does not clear a newer payload marker.
    /// </summary>
    [Fact]
    public void RollbackFailedPublish_NonMatchingPayload_DoesNotClearCache() {
        var sync = new object();
        string? lastPayload = "payload-b";

        UiPublishDedupe.RollbackFailedPublish(sync, ref lastPayload, "payload-a");

        Assert.Equal("payload-b", lastPayload);
    }

    /// <summary>
    /// Ensures failed publish path allows retry of the same payload.
    /// </summary>
    [Fact]
    public void RollbackFailedPublish_AllowsRetryForSamePayload() {
        var sync = new object();
        string? lastPayload = null;

        var firstAttemptAccepted = UiPublishDedupe.TryBeginPublish(sync, ref lastPayload, "payload-a");
        UiPublishDedupe.RollbackFailedPublish(sync, ref lastPayload, "payload-a");
        var retryAccepted = UiPublishDedupe.TryBeginPublish(sync, ref lastPayload, "payload-a");

        Assert.True(firstAttemptAccepted);
        Assert.True(retryAccepted);
        Assert.Equal("payload-a", lastPayload);
    }
}
