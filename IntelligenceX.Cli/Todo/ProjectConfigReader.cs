using System;
using System.IO;
using System.Text.Json;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectConfigFeatures(
    bool PrWatchGovernanceLabels = false,
    bool PrWatchGovernanceFields = false,
    bool PrWatchGovernanceViews = false
);

internal sealed record ProjectConfigDocument(
    string Owner,
    int ProjectNumber,
    string Repo,
    ProjectConfigFeatures Features
);

internal static class ProjectConfigReader {
    public static bool TryReadFromFile(string path, out ProjectConfigDocument config, out string error) {
        config = new ProjectConfigDocument(string.Empty, 0, string.Empty, new ProjectConfigFeatures());
        error = string.Empty;

        if (!File.Exists(path)) {
            error = $"Project config file not found: {path}";
            return false;
        }

        try {
            return TryReadFromJson(File.ReadAllText(path), out config, out error);
        } catch (Exception ex) {
            error = $"Failed to read project config at {path}: {ex.Message}";
            return false;
        }
    }

    public static bool TryReadFromJson(string json, out ProjectConfigDocument config, out string error) {
        config = new ProjectConfigDocument(string.Empty, 0, string.Empty, new ProjectConfigFeatures());
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json)) {
            error = "Project config JSON is empty.";
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var owner = ReadString(root, "owner");
            var repo = ReadString(root, "repo");
            var projectNumber = 0;
            if (TryGetProperty(root, "project", out var projectObj) &&
                projectObj.ValueKind == JsonValueKind.Object) {
                projectNumber = ReadInt(projectObj, "number");
            }

            if (string.IsNullOrWhiteSpace(owner) || projectNumber <= 0) {
                error = "Project config JSON is missing owner/project number.";
                return false;
            }

            var labels = false;
            var fields = false;
            var views = false;

            if (TryGetProperty(root, "features", out var featuresObj) &&
                featuresObj.ValueKind == JsonValueKind.Object &&
                TryGetProperty(featuresObj, "prWatchGovernance", out var prWatchGovernanceObj) &&
                prWatchGovernanceObj.ValueKind == JsonValueKind.Object) {
                labels = ReadBool(prWatchGovernanceObj, "labels");
                fields = ReadBool(prWatchGovernanceObj, "fields");
                views = ReadBool(prWatchGovernanceObj, "views");
            }

            // Backward-compatible fallback for configs written before the explicit features block.
            if (!views &&
                TryGetProperty(root, "views", out var viewsObj) &&
                viewsObj.ValueKind == JsonValueKind.Object) {
                views = ReadBool(viewsObj, "includePrWatchGovernanceViews");
            }

            config = new ProjectConfigDocument(
                owner.Trim(),
                projectNumber,
                repo.Trim(),
                new ProjectConfigFeatures(labels, fields, views));
            return true;
        } catch (Exception ex) {
            error = $"Failed to parse project config JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return element.TryGetProperty(name, out value);
    }

    private static string ReadString(JsonElement element, string name) {
        return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string name) {
        return TryGetProperty(element, name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
    }

    private static bool ReadBool(JsonElement element, string name) {
        return TryGetProperty(element, name, out var value) &&
               (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) &&
               value.GetBoolean();
    }
}
