using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal static class GitHubEventParser {
    public static PullRequestContext? TryParsePullRequest(JsonObject root) {
        try {
            return ParsePullRequest(root);
        } catch {
            return null;
        }
    }

    public static PullRequestContext ParsePullRequest(JsonObject root) {
        var pr = root.GetObject("pull_request") ?? throw new InvalidOperationException("Missing pull_request object.");
        var repoFullName = root.GetObject("repository")?.GetString("full_name")
            ?? root.GetString("repository")
            ?? throw new InvalidOperationException("Missing repository full name.");

        var title = pr.GetString("title") ?? string.Empty;
        var body = pr.GetString("body");
        var draft = pr.GetBoolean("draft");
        var number = (int)(pr.GetInt64("number") ?? 0);
        var headSha = pr.GetObject("head")?.GetString("sha");

        var labels = new List<string>();
        var labelsArray = pr.GetArray("labels");
        if (labelsArray is not null) {
            foreach (var item in labelsArray) {
                var obj = item.AsObject();
                var name = obj?.GetString("name");
                if (!string.IsNullOrWhiteSpace(name)) {
                    labels.Add(name);
                }
            }
        }

        var (owner, repo) = SplitRepo(repoFullName);
        return new PullRequestContext(repoFullName, owner, repo, number, title, body, draft, headSha, labels);
    }

    private static (string owner, string repo) SplitRepo(string fullName) {
        var parts = fullName.Split('/');
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo name '{fullName}'.");
        }
        return (parts[0], parts[1]);
    }
}
