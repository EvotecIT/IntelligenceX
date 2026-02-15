using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class IssueSuggestionCommentManager {
    internal const string CommentMarker = "<!-- intelligencex:pr-issue-suggestions -->";

    public static async Task<bool> UpsertAsync(string repo, int pullRequestNumber, string commentBody) {
        if (string.IsNullOrWhiteSpace(repo) || pullRequestNumber <= 0 || string.IsNullOrWhiteSpace(commentBody)) {
            return false;
        }

        var existingCommentId = await TryFindManagedCommentIdAsync(repo, pullRequestNumber).ConfigureAwait(false);
        if (existingCommentId.HasValue) {
            var (updateCode, _, updateErr) = await GhCli.RunAsync(
                "api",
                "--method", "PATCH",
                $"repos/{repo}/issues/comments/{existingCommentId.Value.ToString(CultureInfo.InvariantCulture)}",
                "-f", $"body={commentBody}"
            ).ConfigureAwait(false);

            if (updateCode == 0) {
                return true;
            }

            Console.Error.WriteLine(
                $"Warning: failed to update PR suggestion comment for {repo}#{pullRequestNumber}: {(string.IsNullOrWhiteSpace(updateErr) ? "unknown error" : updateErr.Trim())}");
            return false;
        }

        var (createCode, _, createErr) = await GhCli.RunAsync(
            "api",
            "--method", "POST",
            $"repos/{repo}/issues/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}/comments",
            "-f", $"body={commentBody}"
        ).ConfigureAwait(false);
        if (createCode == 0) {
            return true;
        }

        Console.Error.WriteLine(
            $"Warning: failed to create PR suggestion comment for {repo}#{pullRequestNumber}: {(string.IsNullOrWhiteSpace(createErr) ? "unknown error" : createErr.Trim())}");
        return false;
    }

    private static async Task<long?> TryFindManagedCommentIdAsync(string repo, int pullRequestNumber) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            "api",
            $"repos/{repo}/issues/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}/comments?per_page=100"
        ).ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to list PR comments for {repo}#{pullRequestNumber}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return null;
        }

        try {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return null;
            }

            var matches = new List<long>();
            foreach (var item in doc.RootElement.EnumerateArray()) {
                if (!item.TryGetProperty("body", out var bodyProp) || bodyProp.ValueKind != JsonValueKind.String) {
                    continue;
                }
                var body = bodyProp.GetString() ?? string.Empty;
                if (body.IndexOf(CommentMarker, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                if (!item.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number) {
                    continue;
                }
                if (!idProp.TryGetInt64(out var id) || id <= 0) {
                    continue;
                }

                matches.Add(id);
            }

            if (matches.Count == 0) {
                return null;
            }

            return matches.Max();
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse PR comments JSON for {repo}#{pullRequestNumber}: {ex.Message}");
            return null;
        }
    }
}
