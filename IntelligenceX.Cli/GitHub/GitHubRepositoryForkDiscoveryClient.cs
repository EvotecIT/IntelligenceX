using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubRepositoryForkDiscoveryClient {
    private const int DefaultPageSize = 50;

    public async Task<IReadOnlyList<GitHubRepositoryForkInsight>> GetUsefulForksAsync(string repositoryNameWithOwner, int limit = 20) {
        if (string.IsNullOrWhiteSpace(repositoryNameWithOwner)) {
            throw new ArgumentException("Repository name is required.", nameof(repositoryNameWithOwner));
        }

        if (limit <= 0) {
            return Array.Empty<GitHubRepositoryForkInsight>();
        }

        var (owner, repository) = SplitRepositoryName(repositoryNameWithOwner);
        var records = await GetForksAsync(owner, repository, Math.Max(limit, DefaultPageSize), QueryForksAsync).ConfigureAwait(false);
        return GitHubRepositoryForkScoring.Score(records, repositoryNameWithOwner)
            .Take(limit)
            .ToArray();
    }

    internal static Task<IReadOnlyList<GitHubRepositoryForkRecord>> GetForksForTestAsync(
        string repositoryNameWithOwner,
        int limit,
        Func<string, string, int, string?, Task<JsonElement>> queryForksAsync) {
        if (string.IsNullOrWhiteSpace(repositoryNameWithOwner)) {
            throw new ArgumentException("Repository name is required.", nameof(repositoryNameWithOwner));
        }

        ArgumentNullException.ThrowIfNull(queryForksAsync);

        var (owner, repository) = SplitRepositoryName(repositoryNameWithOwner);
        return GetForksAsync(owner, repository, Math.Max(limit, DefaultPageSize), queryForksAsync);
    }

    private static async Task<IReadOnlyList<GitHubRepositoryForkRecord>> GetForksAsync(
        string owner,
        string repository,
        int limit,
        Func<string, string, int, string?, Task<JsonElement>> queryForksAsync) {
        var forks = new List<GitHubRepositoryForkRecord>();
        string? cursor = null;
        var remaining = Math.Max(limit, DefaultPageSize);

        while (remaining > 0) {
            var batchSize = Math.Min(DefaultPageSize, remaining);
            var root = await queryForksAsync(owner, repository, batchSize, cursor).ConfigureAwait(false);
            if (!GitHubGraphQlCli.TryGetProperty(root, "data", out var data) ||
                !GitHubGraphQlCli.TryGetProperty(data, "repository", out var repositoryNode) ||
                repositoryNode.ValueKind != JsonValueKind.Object) {
                break;
            }

            if (!GitHubGraphQlCli.TryGetProperty(repositoryNode, "forks", out var forksConnection) ||
                forksConnection.ValueKind != JsonValueKind.Object) {
                break;
            }

            if (GitHubGraphQlCli.TryGetProperty(forksConnection, "nodes", out var nodes) &&
                nodes.ValueKind == JsonValueKind.Array) {
                var addedCount = 0;
                foreach (var node in nodes.EnumerateArray()) {
                    if (node.ValueKind != JsonValueKind.Object) {
                        continue;
                    }

                    var nameWithOwner = GitHubGraphQlCli.ReadString(node, "nameWithOwner");
                    if (string.IsNullOrWhiteSpace(nameWithOwner)) {
                        continue;
                    }

                    forks.Add(new GitHubRepositoryForkRecord(
                        repositoryNameWithOwner: nameWithOwner,
                        url: GitHubGraphQlCli.ReadString(node, "url"),
                        stars: ReadInt32(node, "stargazerCount"),
                        forks: ReadInt32(node, "forkCount"),
                        watchers: ReadInt32FromNested(node, "watchers", "totalCount"),
                        openIssues: ReadInt32FromNested(node, "issues", "totalCount"),
                        description: GitHubGraphQlCli.ReadString(node, "description"),
                        primaryLanguage: ReadStringFromNested(node, "primaryLanguage", "name"),
                        pushedAt: GitHubGraphQlCli.ReadString(node, "pushedAt"),
                        updatedAt: GitHubGraphQlCli.ReadString(node, "updatedAt"),
                        createdAt: GitHubGraphQlCli.ReadString(node, "createdAt"),
                        isArchived: ReadBoolean(node, "isArchived")));
                    addedCount++;
                }

                if (addedCount <= 0) {
                    break;
                }

                remaining -= addedCount;
            } else {
                break;
            }
            if (!GitHubGraphQlCli.TryGetProperty(forksConnection, "pageInfo", out var pageInfo) ||
                pageInfo.ValueKind != JsonValueKind.Object) {
                break;
            }

            var hasNextPage = GitHubGraphQlCli.TryGetProperty(pageInfo, "hasNextPage", out var hasNextPageValue) &&
                              hasNextPageValue.ValueKind == JsonValueKind.True;
            var endCursor = GitHubGraphQlCli.ReadString(pageInfo, "endCursor");
            if (!hasNextPage || string.IsNullOrWhiteSpace(endCursor)) {
                break;
            }

            cursor = endCursor;
        }

        return forks;
    }

    private static async Task<JsonElement> QueryForksAsync(string owner, string repository, int first, string? afterCursor) {
        var query = """
query($owner: String!, $repo: String!, $first: Int!, $after: String) {
  repository(owner: $owner, name: $repo) {
    nameWithOwner
    forks(first: $first, after: $after, orderBy: { field: STARGAZERS, direction: DESC }) {
      nodes {
        nameWithOwner
        url
        description
        stargazerCount
        forkCount
        updatedAt
        createdAt
        pushedAt
        isArchived
        watchers {
          totalCount
        }
        issues(states: OPEN) {
          totalCount
        }
        primaryLanguage {
          name
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}
""";

        return await GitHubGraphQlCli.QueryAsync(
            query,
            TimeSpan.FromSeconds(90),
            ("owner", owner),
            ("repo", repository),
            ("first", first.ToString(CultureInfo.InvariantCulture)),
            ("after", afterCursor)).ConfigureAwait(false);
    }

    private static (string Owner, string Repository) SplitRepositoryName(string repositoryNameWithOwner) {
        var parts = repositoryNameWithOwner.Trim().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            throw new ArgumentException("Repository name must be in owner/name format.", nameof(repositoryNameWithOwner));
        }

        return (parts[0].Trim(), parts[1].Trim());
    }

    private static int ReadInt32(JsonElement obj, string name) {
        return GitHubGraphQlCli.TryGetProperty(obj, name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
    }

    private static int ReadInt32FromNested(JsonElement obj, string parentName, string name) {
        if (!GitHubGraphQlCli.TryGetProperty(obj, parentName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object) {
            return 0;
        }

        return ReadInt32(parent, name);
    }

    private static string? ReadStringFromNested(JsonElement obj, string parentName, string name) {
        if (!GitHubGraphQlCli.TryGetProperty(obj, parentName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var value = GitHubGraphQlCli.ReadString(parent, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ReadBoolean(JsonElement obj, string name) {
        return GitHubGraphQlCli.TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}

internal sealed class GitHubRepositoryForkRecord {
    public GitHubRepositoryForkRecord(
        string repositoryNameWithOwner,
        string? url,
        int stars,
        int forks,
        int watchers,
        int openIssues,
        string? description,
        string? primaryLanguage,
        string? pushedAt,
        string? updatedAt,
        string? createdAt,
        bool isArchived) {
        RepositoryNameWithOwner = repositoryNameWithOwner;
        Url = url;
        Stars = Math.Max(0, stars);
        Forks = Math.Max(0, forks);
        Watchers = Math.Max(0, watchers);
        OpenIssues = Math.Max(0, openIssues);
        Description = description;
        PrimaryLanguage = primaryLanguage;
        PushedAt = pushedAt;
        UpdatedAt = updatedAt;
        CreatedAt = createdAt;
        IsArchived = isArchived;
    }

    public string RepositoryNameWithOwner { get; }
    public string? Url { get; }
    public int Stars { get; }
    public int Forks { get; }
    public int Watchers { get; }
    public int OpenIssues { get; }
    public string? Description { get; }
    public string? PrimaryLanguage { get; }
    public string? PushedAt { get; }
    public string? UpdatedAt { get; }
    public string? CreatedAt { get; }
    public bool IsArchived { get; }
}

internal sealed class GitHubRepositoryForkInsight {
    public GitHubRepositoryForkInsight(
        GitHubRepositoryForkRecord fork,
        double score,
        string tier,
        IReadOnlyList<string> reasons) {
        Fork = fork ?? throw new ArgumentNullException(nameof(fork));
        Score = score;
        Tier = tier;
        Reasons = reasons ?? Array.Empty<string>();
    }

    public GitHubRepositoryForkRecord Fork { get; }
    public double Score { get; }
    public string Tier { get; }
    public IReadOnlyList<string> Reasons { get; }
}

internal static class GitHubRepositoryForkScoring {
    public static IReadOnlyList<GitHubRepositoryForkInsight> Score(
        IEnumerable<GitHubRepositoryForkRecord> forks,
        string? parentRepositoryNameWithOwner = null,
        Func<DateTimeOffset>? utcNow = null) {
        if (forks is null) {
            throw new ArgumentNullException(nameof(forks));
        }

        var now = (utcNow ?? (() => DateTimeOffset.UtcNow))();
        var parentLanguage = InferParentLanguage(parentRepositoryNameWithOwner);
        return forks
            .Select(fork => BuildInsight(fork, now, parentLanguage))
            .OrderByDescending(static insight => insight.Score)
            .ThenByDescending(static insight => insight.Fork.Stars)
            .ThenBy(static insight => insight.Fork.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static GitHubRepositoryForkInsight BuildInsight(
        GitHubRepositoryForkRecord fork,
        DateTimeOffset now,
        string? parentLanguage) {
        var score = 0d;
        var reasons = new List<string>();

        if (fork.Stars > 0) {
            score += Math.Min(30d, fork.Stars * 1.6d);
            reasons.Add(fork.Stars.ToString(CultureInfo.InvariantCulture) + " stars");
        }
        if (fork.Watchers > 0) {
            score += Math.Min(18d, fork.Watchers * 1.5d);
            reasons.Add(fork.Watchers.ToString(CultureInfo.InvariantCulture) + " watchers");
        }
        if (fork.Forks > 0) {
            score += Math.Min(10d, fork.Forks * 0.8d);
            reasons.Add(fork.Forks.ToString(CultureInfo.InvariantCulture) + " downstream forks");
        }
        if (!fork.IsArchived) {
            score += 8d;
            reasons.Add("active");
        }
        if (!string.IsNullOrWhiteSpace(fork.Description)) {
            score += 5d;
            reasons.Add("described");
        }

        var recencyDays = GetRecencyDays(fork.PushedAt, fork.UpdatedAt, now);
        if (recencyDays.HasValue) {
            if (recencyDays.Value <= 14) {
                score += 20d;
                reasons.Add("updated within 14 days");
            } else if (recencyDays.Value <= 45) {
                score += 12d;
                reasons.Add("updated within 45 days");
            } else if (recencyDays.Value <= 120) {
                score += 6d;
                reasons.Add("updated within 120 days");
            }
        }

        if (!string.IsNullOrWhiteSpace(parentLanguage) &&
            string.Equals(parentLanguage, fork.PrimaryLanguage, StringComparison.OrdinalIgnoreCase)) {
            score += 6d;
            reasons.Add("same primary language");
        }

        if (fork.OpenIssues > 20) {
            score -= 4d;
        }
        if (fork.IsArchived) {
            score -= 10d;
        }

        var tier = score >= 60d
            ? "high"
            : score >= 35d
                ? "medium"
                : "low";
        return new GitHubRepositoryForkInsight(fork, Math.Round(score, 2), tier, reasons);
    }

    private static double? GetRecencyDays(string? pushedAt, string? updatedAt, DateTimeOffset now) {
        var text = string.IsNullOrWhiteSpace(pushedAt) ? updatedAt : pushedAt;
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? (now.ToUniversalTime() - parsed.ToUniversalTime()).TotalDays
            : null;
    }

    private static string? InferParentLanguage(string? parentRepositoryNameWithOwner) {
        if (string.IsNullOrWhiteSpace(parentRepositoryNameWithOwner)) {
            return null;
        }

        var normalized = parentRepositoryNameWithOwner.Trim();
        return normalized.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? "PowerShell" : null;
    }
}
