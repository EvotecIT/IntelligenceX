using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class RepositoryLabelManager {
    public static async Task EnsureLabelsAsync(string repo, IReadOnlyList<ProjectLabelDefinition> labels) {
        if (labels.Count == 0) {
            return;
        }

        var existing = await GetExistingLabelNamesAsync(repo).ConfigureAwait(false);
        foreach (var label in labels) {
            if (existing.Contains(label.Name)) {
                continue;
            }

            var (code, _, stderr) = await GhCli.RunAsync(
                "label", "create", label.Name,
                "--repo", repo,
                "--color", label.Color,
                "--description", label.Description
            ).ConfigureAwait(false);

            if (code != 0) {
                throw new InvalidOperationException(
                    $"Failed to create label '{label.Name}' in repo '{repo}': {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            }
        }
    }

    public static async Task<bool> AddLabelsAsync(string repo, string kind, int number, IReadOnlyList<string> labels) {
        if (number <= 0 || labels.Count == 0) {
            return true;
        }

        var normalized = labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) {
            return true;
        }

        var command = kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) ? "pr" : "issue";
        var (code, _, stderr) = await GhCli.RunAsync(
            command, "edit",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo,
            "--add-label", string.Join(",", normalized)
        ).ConfigureAwait(false);

        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to apply labels to {kind} #{number}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return false;
        }

        return true;
    }

    private static async Task<HashSet<string>> GetExistingLabelNamesAsync(string repo) {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (code, stdout, stderr) = await GhCli.RunAsync(
            "label", "list",
            "--repo", repo,
            "--limit", "1000",
            "--json", "name"
        ).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(
                $"Failed to list labels for repo '{repo}': {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
        }

        try {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return names;
            }

            foreach (var item in doc.RootElement.EnumerateArray()) {
                if (!item.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) {
                    continue;
                }
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name)) {
                    names.Add(name.Trim());
                }
            }
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to parse label list JSON: {ex.Message}");
        }

        return names;
    }
}
