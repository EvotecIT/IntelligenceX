using System;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private sealed partial class ReplSession {
        private const string LegacyPackInfoToolNameSuffix = "_pack_info";

        private void TryStoreSessionToolOutputCache(string cacheKey, string output, bool hasSessionCacheKey) {
            if (!hasSessionCacheKey || !ShouldCacheSessionToolOutput(output)) {
                return;
            }

            _sessionToolOutputCache[cacheKey] = output;
        }

        private bool TryGetSessionToolOutputCacheKey(ToolCall call, out string cacheKey) {
            return TryGetSessionToolOutputCacheKey(_registry, call, out cacheKey);
        }

        private static bool TryGetSessionToolOutputCacheKey(ToolRegistry registry, ToolCall call, out string cacheKey) {
            cacheKey = string.Empty;
            var toolName = (call.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                return false;
            }

            if (!IsSessionCacheablePackInfoTool(registry, toolName)) {
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

        private static bool IsSessionCacheablePackInfoTool(ToolRegistry registry, string toolName) {
            if (registry.TryGetDefinition(toolName, out var definition)) {
                if (HasPackInfoRoutingRole(definition)) {
                    return true;
                }

                if (HasExplicitNonPackInfoRoutingRole(definition)) {
                    return false;
                }
            }

            return LooksLikeLegacyPackInfoToolName(toolName);
        }

        private static bool HasPackInfoRoutingRole(ToolDefinition definition) {
            return string.Equals(definition.Routing?.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasExplicitNonPackInfoRoutingRole(ToolDefinition definition) {
            var role = (definition.Routing?.Role ?? string.Empty).Trim();
            return role.Length > 0
                   && !string.Equals(role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeLegacyPackInfoToolName(string toolName) {
            return toolName.EndsWith(LegacyPackInfoToolNameSuffix, StringComparison.OrdinalIgnoreCase);
        }

    }
}
