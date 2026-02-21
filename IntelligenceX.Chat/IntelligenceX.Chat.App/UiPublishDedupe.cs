using System;

namespace IntelligenceX.Chat.App;

internal static class UiPublishDedupe {
    internal static bool TryBeginPublish(object sync, ref string? lastPublishedPayload, string payload) {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(payload);

        lock (sync) {
            if (string.Equals(lastPublishedPayload, payload, StringComparison.Ordinal)) {
                return false;
            }

            lastPublishedPayload = payload;
            return true;
        }
    }

    internal static void RollbackFailedPublish(object sync, ref string? lastPublishedPayload, string payload) {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(payload);

        lock (sync) {
            if (string.Equals(lastPublishedPayload, payload, StringComparison.Ordinal)) {
                lastPublishedPayload = null;
            }
        }
    }
}
