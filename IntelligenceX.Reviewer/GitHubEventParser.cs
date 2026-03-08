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
        var head = pr.GetObject("head");
        var baseRef = pr.GetObject("base");
        var headSha = head?.GetString("sha");
        var baseSha = baseRef?.GetString("sha");
        var baseRefName = baseRef?.GetString("ref");
        var headRepo = head?.GetObject("repo");
        var headRepoFullName = headRepo?.GetString("full_name");
        var isFork = headRepo?.GetBoolean("fork") ?? false;
        var authorAssociation = pr.GetString("author_association");

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
        return new PullRequestContext(repoFullName, owner, repo, number, title, body, draft, headSha, baseSha,
            labels, headRepoFullName, isFork, authorAssociation, headRepo is not null, baseRefName);
    }

    private static (string owner, string repo) SplitRepo(string fullName) {
        var parts = fullName.Split('/');
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo name '{fullName}'.");
        }
        return (parts[0], parts[1]);
    }
}
