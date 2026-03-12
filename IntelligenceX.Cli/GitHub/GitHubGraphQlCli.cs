using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal static class GitHubGraphQlCli {
    public static async Task<JsonElement> QueryAsync(string query, TimeSpan timeout, params (string Key, string? Value)[] variables) {
        var args = new List<string> {
            "api",
            "graphql",
            "-f",
            $"query={query}"
        };
        var variableTypes = ParseVariableTypes(query);
        foreach (var (key, value) in variables) {
            if (value is null) {
                continue;
            }
            args.Add(UseTypedFormValue(variableTypes, key) ? "-F" : "-f");
            args.Add($"{key}={value}");
        }

        var (code, stdout, stderr) = await GhCli.RunAsync(timeout, args.ToArray()).ConfigureAwait(false);
        JsonElement root;
        try {
            using var doc = JsonDocument.Parse(stdout);
            root = doc.RootElement.Clone();
        } catch (Exception) {
            if (code != 0) {
                throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
            }
            throw;
        }

        if (code != 0) {
            if (root.TryGetProperty("errors", out var nonZeroErrors) &&
                nonZeroErrors.ValueKind == JsonValueKind.Array &&
                nonZeroErrors.GetArrayLength() > 0 &&
                ShouldIgnoreErrors(root, nonZeroErrors)) {
                return root;
            }

            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
        }

        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0) {
            if (ShouldIgnoreErrors(root, errors)) {
                return root;
            }
            throw new InvalidOperationException($"GitHub GraphQL returned errors: {FormatGraphQlErrors(errors)}");
        }

        return root;
    }

    private static string FormatGraphQlErrors(JsonElement errors) {
        var messages = errors
            .EnumerateArray()
            .Select(static error => ReadString(error, "message"))
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return messages.Length == 0
            ? "GraphQL error"
            : string.Join(" | ", messages);
    }

    private static bool ShouldIgnoreErrors(JsonElement root, JsonElement errors) {
        if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Object) {
            return false;
        }

        foreach (var error in errors.EnumerateArray()) {
            var message = ReadString(error, "message");
            if (string.IsNullOrWhiteSpace(message)) {
                return false;
            }

            if (message.Contains("Could not resolve to a User with the login of", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (message.Contains("Could not resolve to an Organization with the login of", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string> ParseVariableTypes(string query) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(query, @"\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z0-9_!\[\]]+)")) {
            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type)) {
                map[name] = type;
            }
        }
        return map;
    }

    private static bool UseTypedFormValue(IReadOnlyDictionary<string, string> variableTypes, string key) {
        if (!variableTypes.TryGetValue(key, out var graphType) || string.IsNullOrWhiteSpace(graphType)) {
            return true;
        }

        var normalized = graphType.Replace("!", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        return normalized.Equals("Int", StringComparison.Ordinal) ||
               normalized.Equals("Float", StringComparison.Ordinal) ||
               normalized.Equals("Boolean", StringComparison.Ordinal);
    }

    internal static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    internal static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }
}
