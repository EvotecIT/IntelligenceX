using System;
using System.Collections.Generic;
using System.Reflection;
using EventViewerX;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogNamedEventsPayload {
    internal static string ResolveNamedEventName(EventObjectSlim item) {
        return EventLogNamedEventsHelper.TryParseOne(item.Type, out var parsedNamedEvent)
            ? EventLogNamedEventsHelper.GetQueryName(parsedNamedEvent)
            : EventLogNamedEventsQueryShared.ToSnakeCase(item.Type);
    }

    internal static Dictionary<string, object?> ExtractPayload(EventObjectSlim item) {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var type = item.GetType();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (!ShouldIncludeField(field)) {
                continue;
            }

            var value = field.GetValue(item);
            payload[EventLogNamedEventsQueryShared.ToSnakeCase(field.Name)] = NormalizeValue(value);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!ShouldIncludeProperty(property)) {
                continue;
            }

            var key = EventLogNamedEventsQueryShared.ToSnakeCase(property.Name);
            if (payload.ContainsKey(key)) {
                continue;
            }

            object? value;
            try {
                value = property.GetValue(item);
            } catch {
                continue;
            }

            payload[key] = NormalizeValue(value);
        }

        return payload;
    }

    internal static Dictionary<string, object?> ProjectPayload(
        Dictionary<string, object?> payload,
        HashSet<string>? payloadKeySet) {
        if (payloadKeySet is null || payloadKeySet.Count == 0) {
            return payload;
        }

        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in payloadKeySet) {
            if (payload.TryGetValue(key, out var value)) {
                projected[key] = value;
            }
        }

        return projected;
    }

    internal static string? ReadPayloadString(IReadOnlyDictionary<string, object?> payload, string key) {
        if (!payload.TryGetValue(key, out var value) || value is null) {
            return null;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    internal static string? ReadPayloadUtc(IReadOnlyDictionary<string, object?> payload, string key) {
        if (!payload.TryGetValue(key, out var value) || value is null) {
            return null;
        }

        if (value is string text && DateTime.TryParse(text, out var parsed)) {
            return parsed.ToUniversalTime().ToString("O");
        }

        if (value is DateTime dateTime) {
            return dateTime.ToUniversalTime().ToString("O");
        }

        return value.ToString();
    }

    private static bool ShouldIncludeField(FieldInfo field) {
        if (field.Name.StartsWith("_", StringComparison.Ordinal)) {
            return false;
        }

        if (string.Equals(field.Name, nameof(EventObjectSlim.EventID), StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.Name, nameof(EventObjectSlim.RecordID), StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.Name, nameof(EventObjectSlim.GatheredFrom), StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.Name, nameof(EventObjectSlim.GatheredLogName), StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (string.Equals(field.FieldType.Name, "EventObject", StringComparison.Ordinal)) {
            return false;
        }

        return true;
    }

    private static bool ShouldIncludeProperty(PropertyInfo property) {
        if (!property.CanRead || property.GetMethod is null || !property.GetMethod.IsPublic) {
            return false;
        }

        return property.GetIndexParameters().Length == 0;
    }

    private static object? NormalizeValue(object? value) {
        if (value is null) {
            return null;
        }

        if (value is DateTime dateTime) {
            return dateTime.ToUniversalTime().ToString("O");
        }

        if (value is DateTimeOffset dateTimeOffset) {
            return dateTimeOffset.ToUniversalTime().ToString("O");
        }

        if (value is Enum enumValue) {
            return enumValue.ToString();
        }

        return value;
    }
}
