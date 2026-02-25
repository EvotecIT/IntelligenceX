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
        return BuildLowerHexPrefix(bytes, 6);
    }

    internal static string BuildTaskIdForTests(int prNumber, string url, string text) {
        return BuildTaskId(prNumber, url, text);
    }

    private static string BuildLowerHexPrefix(byte[] bytes, int byteCount) {
        if (bytes is null || bytes.Length == 0 || byteCount <= 0) {
            return string.Empty;
        }

        var count = Math.Min(byteCount, bytes.Length);
        return string.Create(count * 2, (bytes, count), static (chars, state) => {
            const string hex = "0123456789abcdef";
            for (var i = 0; i < state.count; i++) {
                var value = state.bytes[i];
                chars[i * 2] = hex[value >> 4];
                chars[i * 2 + 1] = hex[value & 0x0F];
            }
        });
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
        var (code, stdout, _) = await GhCli.RunAsync(BuildIssueExistsArgs(repo, id)).ConfigureAwait(false);
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

    internal static IReadOnlyList<string> BuildIssueExistsArgsForTests(string repo, string id) {
        return BuildIssueExistsArgs(repo, id);
    }

    private static string[] BuildIssueExistsArgs(string repo, string id) {
        var query = $"ix-bot-feedback-id:{id}";
        return new[] {
            "issue", "list",
            "--repo", repo,
            "--state", "open",
            "--search", query,
            "--limit", "1",
            "--json", "number"
        };
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
