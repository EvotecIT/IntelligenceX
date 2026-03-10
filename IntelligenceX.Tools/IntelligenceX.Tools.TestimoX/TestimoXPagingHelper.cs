using System;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXPagingHelper {
    internal static int? ResolvePageSize(JsonObject? arguments, int maxPageSize) {
        if (!HasArgument(arguments, "page_size") && !HasArgument(arguments, "max_rules")) {
            return null;
        }

        var pageSize = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "page_size",
            defaultValue: maxPageSize,
            minInclusive: 1,
            maxInclusive: maxPageSize);

        if (!HasArgument(arguments, "page_size") && HasArgument(arguments, "max_rules")) {
            pageSize = ToolArgs.GetCappedInt32(
                arguments: arguments,
                key: "max_rules",
                defaultValue: pageSize,
                minInclusive: 1,
                maxInclusive: maxPageSize);
        }

        return pageSize;
    }

    internal static bool TryReadOffset(JsonObject? arguments, out int offset, out string? error) {
        offset = 0;
        error = null;

        var rawCursor = ToolArgs.GetOptionalTrimmed(arguments, "cursor");
        var cursorOffset = 0;
        if (!string.IsNullOrWhiteSpace(rawCursor)) {
            if (!OffsetCursor.TryDecode(rawCursor, out var decoded) || decoded < 0 || decoded > int.MaxValue) {
                error = "cursor is invalid. Use cursor returned by previous page response.";
                return false;
            }

            cursorOffset = (int)decoded;
        }

        if (!TryGetInt64Argument(arguments, "offset", out var rawOffset)) {
            offset = cursorOffset;
            return true;
        }

        if (rawOffset < 0 || rawOffset > int.MaxValue) {
            error = "offset must be between 0 and 2147483647.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rawCursor) && rawOffset != cursorOffset) {
            error = "Provide either cursor or offset (or keep them aligned), not conflicting values.";
            return false;
        }

        offset = (int)rawOffset;
        return true;
    }

    private static bool TryGetInt64Argument(JsonObject? arguments, string name, out long value) {
        value = 0;
        if (arguments is null || string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (!string.Equals((kv.Key ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var parsed = kv.Value.AsInt64();
            if (!parsed.HasValue) {
                return false;
            }

            value = parsed.Value;
            return true;
        }

        return false;
    }

    private static bool HasArgument(JsonObject? arguments, string name) {
        if (arguments is null || string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (string.Equals((kv.Key ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
