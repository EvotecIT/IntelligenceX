using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class RepositoryLabelManager {
    internal sealed record LabelSyncPlan(
        IReadOnlyList<string> LabelsToAdd,
        IReadOnlyList<string> LabelsToRemove
    );

    private static readonly IReadOnlyList<string> ManagedLabelPrefixes = new[] {
        "ix/category:",
        "ix/tag:",
        "ix/vision:",
        "ix/match:",
        "ix/decision:",
        "ix/signal:",
        "ix/duplicate:"
    };

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

    public static async Task<bool> SyncManagedLabelsAsync(
        string repo,
        string kind,
        int number,
        IReadOnlyList<string> desiredLabels,
        IReadOnlyList<string>? existingLabels = null) {
        if (number <= 0) {
            return true;
        }

        IReadOnlyList<string> currentLabels = existingLabels ?? Array.Empty<string>();
        if (existingLabels is null) {
            var loaded = await TryGetItemLabelNamesAsync(repo, kind, number).ConfigureAwait(false);
            if (loaded is null) {
                return false;
            }
            currentLabels = loaded;
        }

        var plan = BuildManagedLabelSyncPlan(desiredLabels, currentLabels);
        if (plan.LabelsToAdd.Count == 0 && plan.LabelsToRemove.Count == 0) {
            return true;
        }

        var command = kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) ? "pr" : "issue";
        var args = new List<string> {
            command, "edit",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo
        };
        if (plan.LabelsToAdd.Count > 0) {
            args.Add("--add-label");
            args.Add(string.Join(",", plan.LabelsToAdd));
        }
        if (plan.LabelsToRemove.Count > 0) {
            args.Add("--remove-label");
            args.Add(string.Join(",", plan.LabelsToRemove));
        }

        var (code, _, stderr) = await GhCli.RunAsync(args.ToArray()).ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to sync managed labels on {kind} #{number}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return false;
        }

        return true;
    }

    internal static LabelSyncPlan BuildManagedLabelSyncPlan(
        IReadOnlyList<string> desiredLabels,
        IReadOnlyList<string> currentLabels) {
        var desired = desiredLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = currentLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = desired
            .Where(label => !current.Contains(label))
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var toRemove = current
            .Where(label => IsManagedLabelName(label) && !desired.Contains(label))
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LabelSyncPlan(toAdd, toRemove);
    }

    internal static bool IsManagedLabelName(string labelName) {
        if (string.IsNullOrWhiteSpace(labelName)) {
            return false;
        }

        foreach (var prefix in ManagedLabelPrefixes) {
            if (labelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static async Task<IReadOnlyList<string>?> TryGetItemLabelNamesAsync(string repo, string kind, int number) {
        var command = kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) ? "pr" : "issue";
        var (code, stdout, stderr) = await GhCli.RunAsync(
            command, "view",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", "labels"
        ).ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to list current labels for {kind} #{number}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return null;
        }

        try {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("labels", out var labelsArray) ||
                labelsArray.ValueKind != JsonValueKind.Array) {
                return Array.Empty<string>();
            }

            var labels = new List<string>();
            foreach (var label in labelsArray.EnumerateArray()) {
                if (label.ValueKind != JsonValueKind.Object ||
                    !label.TryGetProperty("name", out var nameProp) ||
                    nameProp.ValueKind != JsonValueKind.String) {
                    continue;
                }
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name)) {
                    labels.Add(name.Trim());
                }
            }

            return labels;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse current label JSON for {kind} #{number}: {ex.Message}");
            return null;
        }
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
