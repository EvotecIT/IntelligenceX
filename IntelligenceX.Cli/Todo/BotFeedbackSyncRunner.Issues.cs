using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class BotFeedbackSyncRunner {
    private static string BuildTaskId(int prNumber, string url, string text) {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{prNumber}|{url}|{text}"));
        return Convert.ToHexString(bytes).Substring(0, 12).ToLowerInvariant();
    }

    private static string BuildIssueTitle(int prNumber, string taskText) {
        var prefix = $"Bot feedback (PR #{prNumber})";
        var trimmedTask = taskText.Trim();
        if (trimmedTask.Length > 90) {
            trimmedTask = trimmedTask.Substring(0, 90) + "…";
        }
        return $"{prefix}: {trimmedTask}";
    }

    private static string BuildIssueBody(PrTasks pr, TaskItem task, string id) {
        var sb = new StringBuilder();
        sb.AppendLine($"Bot checklist item from PR #{pr.Number}");
        sb.AppendLine();
        sb.AppendLine("Task:");
        sb.AppendLine($"- [ ] {task.Text}");
        sb.AppendLine();
        sb.AppendLine($"Source: {task.Url}");
        if (!string.IsNullOrWhiteSpace(pr.Url)) {
            sb.AppendLine($"PR: {pr.Url}");
        }
        sb.AppendLine();
        sb.AppendLine($"ix-bot-feedback-id:{id}");
        return sb.ToString();
    }

    private static async Task<bool> IssueExistsAsync(string repo, string id) {
        var query = $"ix-bot-feedback-id:{id}";
        var (code, stdout, _) = await GhCli.RunAsync(
            "issue", "list",
            "--repo", repo,
            "--search", query,
            "--limit", "1",
            "--json", "number"
        ).ConfigureAwait(false);
        if (code != 0) {
            return false;
        }
        try {
            using var doc = JsonDocument.Parse(stdout);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
        } catch {
            return false;
        }
    }

    private static async Task EnsureLabelAsync(string repo, string label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return;
        }
        var (code, stdout, _) = await GhCli.RunAsync("label", "list", "--repo", repo, "--limit", "200", "--json", "name")
            .ConfigureAwait(false);
        if (code == 0) {
            try {
                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.ValueKind == JsonValueKind.Array) {
                    foreach (var item in doc.RootElement.EnumerateArray()) {
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(name) && name.Equals(label, StringComparison.OrdinalIgnoreCase)) {
                            return;
                        }
                    }
                }
            } catch {
                // ignore
            }
        }
        // Best-effort label creation. If it fails due to permissions, issue creation will still run without label validation.
        await GhCli.RunAsync(
            "label", "create", label,
            "--repo", repo,
            "--color", "0e8a16",
            "--description", "Checklist items imported from bot PR reviews/comments"
        ).ConfigureAwait(false);
    }
}
