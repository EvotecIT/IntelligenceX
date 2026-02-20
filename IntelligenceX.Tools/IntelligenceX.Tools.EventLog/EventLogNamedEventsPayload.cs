using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using EventViewerX;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogNamedEventsPayload {
    private static readonly ConcurrentDictionary<Type, PayloadExtractionPlan> PayloadExtractionPlanCache = new();

    internal static string ResolveNamedEventName(EventObjectSlim item) {
        return EventLogNamedEventsHelper.TryParseOne(item.Type, out var parsedNamedEvent)
            ? EventLogNamedEventsHelper.GetQueryName(parsedNamedEvent)
            : EventLogNamedEventsQueryShared.ToSnakeCase(item.Type);
    }

    internal static Dictionary<string, object?> ExtractPayload(EventObjectSlim item) {
        ArgumentNullException.ThrowIfNull(item);

        var plan = PayloadExtractionPlanCache.GetOrAdd(
            item.GetType(),
            static type => BuildPayloadExtractionPlan(type));
        var payload = new Dictionary<string, object?>(
            plan.FieldAccessors.Length + plan.PropertyAccessors.Length,
            StringComparer.OrdinalIgnoreCase);

        foreach (var accessor in plan.FieldAccessors) {
            var value = accessor.Field.GetValue(item);
            payload[accessor.Key] = NormalizeValue(value);
        }

        foreach (var accessor in plan.PropertyAccessors) {
            object? value;
            try {
                value = accessor.Property.GetValue(item);
            } catch (Exception ex) {
                Debug.WriteLine($"[EventLogNamedEventsPayload] Failed to read payload property '{accessor.Property.Name}': {ex.Message}");
                continue;
            }

            payload[accessor.Key] = NormalizeValue(value);
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

        if (value is string text && TryParseUtcValue(text, out var parsedUtc)) {
            return parsedUtc.ToString("O");
        }

        if (value is DateTimeOffset dateTimeOffset) {
            return dateTimeOffset.UtcDateTime.ToString("O");
        }

        if (value is DateTime dateTime) {
            var parsed = dateTime.Kind switch {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            };
            return parsed.ToString("O");
        }

        return value.ToString();
    }

    internal static bool TryParseUtcValue(string? value, out DateTime utc) {
        utc = default;
        var text = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        text = text.Trim();
        var hasExplicitOffset = HasExplicitOffsetOrUtcDesignator(text);

        if (hasExplicitOffset) {
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out var parsedOffset)) {
                return false;
            }

            utc = parsedOffset.UtcDateTime;
            return true;
        }

        if (!DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDateTime)) {
            return false;
        }

        utc = parsedDateTime.Kind switch {
            DateTimeKind.Utc => parsedDateTime,
            DateTimeKind.Local => parsedDateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Utc)
        };
        return true;
    }

    private static bool HasExplicitOffsetOrUtcDesignator(string value) {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var searchStart = 0;
        var tIndex = value.IndexOf('T');
        if (tIndex >= 0 && tIndex + 1 < value.Length) {
            searchStart = tIndex + 1;
        } else {
            var spaceIndex = value.IndexOf(' ');
            if (spaceIndex >= 0 && spaceIndex + 1 < value.Length) {
                searchStart = spaceIndex + 1;
            }
        }

        for (var i = value.Length - 1; i >= searchStart; i--) {
            var ch = value[i];
            if (ch == '+' || ch == '-') {
                return true;
            }
        }

        return false;
    }

    private static PayloadExtractionPlan BuildPayloadExtractionPlan(Type type) {
        var fieldAccessors = new List<PayloadFieldAccessor>();
        var propertyAccessors = new List<PayloadPropertyAccessor>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (!ShouldIncludeField(field)) {
                continue;
            }

            var key = EventLogNamedEventsQueryShared.ToSnakeCase(field.Name);
            if (!seenKeys.Add(key)) {
                continue;
            }

            fieldAccessors.Add(new PayloadFieldAccessor(field, key));
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!ShouldIncludeProperty(property)) {
                continue;
            }

            var key = EventLogNamedEventsQueryShared.ToSnakeCase(property.Name);
            if (!seenKeys.Add(key)) {
                continue;
            }

            propertyAccessors.Add(new PayloadPropertyAccessor(property, key));
        }

        return new PayloadExtractionPlan(fieldAccessors.ToArray(), propertyAccessors.ToArray());
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

    private sealed class PayloadExtractionPlan {
        public PayloadExtractionPlan(
            PayloadFieldAccessor[] fieldAccessors,
            PayloadPropertyAccessor[] propertyAccessors) {
            FieldAccessors = fieldAccessors;
            PropertyAccessors = propertyAccessors;
        }

        public PayloadFieldAccessor[] FieldAccessors { get; }
        public PayloadPropertyAccessor[] PropertyAccessors { get; }
    }

    private sealed class PayloadFieldAccessor {
        public PayloadFieldAccessor(FieldInfo field, string key) {
            Field = field;
            Key = key;
        }

        public FieldInfo Field { get; }
        public string Key { get; }
    }

    private sealed class PayloadPropertyAccessor {
        public PayloadPropertyAccessor(PropertyInfo property, string key) {
            Property = property;
            Key = key;
        }

        public PropertyInfo Property { get; }
        public string Key { get; }
    }
}
