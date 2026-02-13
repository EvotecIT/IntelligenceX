using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Builds table-view columns from typed engine row models so wrappers do not duplicate property projection lists.
/// </summary>
public static class ToolAutoTableColumns {
    /// <summary>
    /// Returns cached column specifications for a row type.
    /// </summary>
    public static IReadOnlyList<ToolTableColumnSpec<TRow>> GetColumnSpecs<TRow>() => Cache<TRow>.ColumnSpecs;

    /// <summary>
    /// Returns cached projection column keys for a row type.
    /// </summary>
    public static IReadOnlyList<string> GetColumnKeys<TRow>() => Cache<TRow>.ColumnKeys;

    private static IReadOnlyList<ToolTableColumnSpec<TRow>> BuildColumnSpecs<TRow>() {
        var rowType = typeof(TRow);
        var properties = rowType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.CanRead && p.GetMethod is not null && p.GetMethod.IsPublic && p.GetIndexParameters().Length == 0)
            .OrderBy(static p => p.MetadataToken)
            .ToArray();

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs = new List<ToolTableColumnSpec<TRow>>(properties.Length);
        foreach (var property in properties) {
            var key = JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key)) {
                continue;
            }

            var column = new ToolColumn(
                key: key,
                label: ToDisplayLabel(property.Name),
                type: MapColumnType(property.PropertyType));

            specs.Add(new ToolTableColumnSpec<TRow>(
                column,
                row => property.GetValue(row)));
        }

        if (specs.Count > 0) {
            return specs;
        }

        return new[] {
            new ToolTableColumnSpec<TRow>(
                new ToolColumn("value", "Value", "object"),
                static row => row)
        };
    }

    private static IReadOnlyList<string> BuildColumnKeys<TRow>() {
        var specs = GetColumnSpecs<TRow>();
        var keys = new string[specs.Count];
        for (var i = 0; i < specs.Count; i++) {
            keys[i] = specs[i].Column.Key;
        }

        return keys;
    }

    private static string MapColumnType(Type type) {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        if (effectiveType == typeof(string) || effectiveType == typeof(Guid) || effectiveType == typeof(Uri)) {
            return "string";
        }
        if (effectiveType == typeof(bool)) {
            return "bool";
        }
        if (effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset)) {
            return "datetime";
        }
        if (effectiveType == typeof(TimeSpan)) {
            return "timespan";
        }
        if (effectiveType.IsEnum) {
            return "string";
        }
        if (IsInteger(effectiveType)) {
            return "int";
        }
        if (IsNumber(effectiveType)) {
            return "number";
        }
        if (effectiveType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(effectiveType)) {
            return "array";
        }
        if (effectiveType.IsClass || (effectiveType.IsValueType && !effectiveType.IsPrimitive && !effectiveType.IsEnum)) {
            return "object";
        }

        return "string";
    }

    private static bool IsInteger(Type type) {
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong);
    }

    private static bool IsNumber(Type type) {
        return type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    private static string ToDisplayLabel(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return "Value";
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++) {
            var c = name[i];
            if (c == '_') {
                builder.Append(' ');
                continue;
            }

            if (i > 0 &&
                char.IsUpper(c) &&
                (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]))) {
                builder.Append(' ');
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    private static class Cache<TRow> {
        internal static readonly IReadOnlyList<ToolTableColumnSpec<TRow>> ColumnSpecs = BuildColumnSpecs<TRow>();
        internal static readonly IReadOnlyList<string> ColumnKeys = BuildColumnKeys<TRow>();
    }
}
