using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class TriageIndexRunner {
    private static void WriteText(string path, string content) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }

    private static (string Owner, string Name) SplitRepo(string repo) {
        var parts = repo.Split('/', 2);
        return (parts[0], parts[1]);
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static bool TryReadNumber(JsonElement obj, out int number) {
        number = 0;
        if (!TryGetProperty(obj, "number", out var prop) || prop.ValueKind != JsonValueKind.Number) {
            return false;
        }
        return prop.TryGetInt32(out number);
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static bool ReadBoolean(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False) {
            return false;
        }
        return prop.GetBoolean();
    }

    private static int ReadInt(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static int ReadNestedInt(JsonElement obj, string parentName, string childName) {
        if (!TryGetProperty(obj, parentName, out var parent) || parent.ValueKind != JsonValueKind.Object) {
            return 0;
        }
        if (!TryGetProperty(parent, childName, out var child) || child.ValueKind != JsonValueKind.Number || !child.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static string ReadNestedString(JsonElement obj, string parentName, string childName) {
        if (!TryGetProperty(obj, parentName, out var parent) || parent.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(parent, childName, out var child) || child.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return child.GetString() ?? string.Empty;
    }

    private static string ReadNestedNestedString(JsonElement obj, string parentName, string arrayName, int index,
        string objectName, string nestedObjectName, string childName) {
        if (!TryGetProperty(obj, parentName, out var parent) || parent.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(parent, arrayName, out var array) || array.ValueKind != JsonValueKind.Array) {
            return string.Empty;
        }
        if (index < 0 || index >= array.GetArrayLength()) {
            return string.Empty;
        }
        var node = array[index];
        if (!TryGetProperty(node, objectName, out var nested) || nested.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(nested, nestedObjectName, out var nestedObject) || nestedObject.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(nestedObject, childName, out var value) || value.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return value.GetString() ?? string.Empty;
    }

    private static DateTimeOffset ReadDate(JsonElement obj, string name) {
        var raw = ReadString(obj, name);
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) {
            return parsed;
        }
        return DateTimeOffset.MinValue;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement obj) {
        var labels = new List<string>();
        if (!TryGetProperty(obj, "labels", out var labelsObj) ||
            labelsObj.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(labelsObj, "nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            return labels;
        }

        foreach (var node in nodes.EnumerateArray()) {
            var name = ReadString(node, "name");
            if (!string.IsNullOrWhiteSpace(name)) {
                labels.Add(name);
            }
        }
        return labels;
    }
}
