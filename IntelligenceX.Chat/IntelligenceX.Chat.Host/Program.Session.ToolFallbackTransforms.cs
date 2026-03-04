using System;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private sealed partial class ReplSession {
        private void TryStoreSessionToolOutputCache(string cacheKey, string output, bool hasSessionCacheKey) {
            if (!hasSessionCacheKey || !ShouldCacheSessionToolOutput(output)) {
                return;
            }

            _sessionToolOutputCache[cacheKey] = output;
        }

        private static bool TryGetSessionToolOutputCacheKey(ToolCall call, out string cacheKey) {
            cacheKey = string.Empty;
            var toolName = (call.Name ?? string.Empty).Trim();
            if (!toolName.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (call.Arguments is not null && call.Arguments.Count > 0) {
                return false;
            }

            var normalizedInput = (call.Input ?? string.Empty).Trim();
            if (normalizedInput.Length > 0 && !string.Equals(normalizedInput, "{}", StringComparison.Ordinal)) {
                return false;
            }

            cacheKey = toolName.ToLowerInvariant();
            return true;
        }

        private static bool ShouldCacheSessionToolOutput(string output) {
            return TryReadToolOutputOk(output, out var ok) && ok;
        }

    }
}
