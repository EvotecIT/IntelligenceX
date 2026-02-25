using System;
using System.Collections.Generic;
using System.Text.Json;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private static bool TryReadDiscoveryStatusObject(JsonElement root, out JsonElement discoveryStatus) {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("discovery_status", out discoveryStatus)
            && discoveryStatus.ValueKind == JsonValueKind.Object) {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("discoveryStatus", out discoveryStatus)
            && discoveryStatus.ValueKind == JsonValueKind.Object) {
            return true;
        }

        discoveryStatus = default;
        return false;
    }

    private static void CopyHintIfPresent(JsonElement source, JsonObject destination, string propertyName) {
        if (!source.TryGetProperty(propertyName, out var node)) {
            return;
        }

        switch (node.ValueKind) {
            case JsonValueKind.String: {
                    var value = (node.GetString() ?? string.Empty).Trim();
                    if (value.Length > 0) {
                        destination.Add(propertyName, value);
                    }
                    break;
                }
            case JsonValueKind.True:
            case JsonValueKind.False:
                destination.Add(propertyName, node.GetBoolean());
                break;
        }
    }

    private static string? ReadNonEmptyHint(JsonObject hints, string propertyName) {
        if (!hints.TryGetValue(propertyName, out var node) || node is null || node.Kind != IntelligenceX.Json.JsonValueKind.String) {
            return null;
        }

        var value = (node.AsString() ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private static bool? ReadHintBoolean(JsonObject hints, string propertyName) {
        if (!hints.TryGetValue(propertyName, out var node) || node is null) {
            return null;
        }

        return node.Kind switch {
            IntelligenceX.Json.JsonValueKind.Boolean => node.AsBoolean(),
            _ => null
        };
    }

    private static bool TryReadPositiveIntProperty(JsonElement source, string propertyName, out int value) {
        value = 0;
        if (!source.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Number) {
            return false;
        }

        if (!node.TryGetInt32(out value)) {
            return false;
        }

        return value > 0;
    }

    private static bool TryReadArrayLength(JsonElement source, string propertyName, out int length) {
        length = 0;
        if (!source.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array) {
            return false;
        }

        length = node.GetArrayLength();
        return true;
    }

    private static bool TryReadBooleanProperty(JsonElement source, string propertyName, out bool value) {
        value = false;
        if (!source.TryGetProperty(propertyName, out var node)) {
            return false;
        }

        return node.ValueKind switch {
            JsonValueKind.True => value = true,
            JsonValueKind.False => true,
            JsonValueKind.String => TryParseProtocolBoolean((node.GetString() ?? string.Empty).Trim(), out value),
            JsonValueKind.Number => node.TryGetInt64(out var numeric)
                                    && TryMapIntegerBoolean(numeric, out value),
            _ => false
        };
    }

    private static bool TryMapIntegerBoolean(long numeric, out bool value) {
        value = false;
        if (numeric == 0) {
            return true;
        }

        if (numeric == 1) {
            value = true;
            return true;
        }

        return false;
    }

    private static bool ContainsToolName(IReadOnlyList<string> tools, string toolName) {
        for (var i = 0; i < tools.Count; i++) {
            if (string.Equals(tools[i], toolName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

}
