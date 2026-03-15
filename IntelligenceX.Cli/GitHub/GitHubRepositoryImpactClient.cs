using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubRepositoryImpactClient {
    private const int PageSize = 100;

    public async Task<GitHubRepositoryImpactSummary> GetRepositoryImpactAsync(IReadOnlyList<string> owners) {
        if (owners is null || owners.Count == 0) {
            return new GitHubRepositoryImpactSummary(Array.Empty<GitHubRepositoryOwnerImpact>(), Array.Empty<GitHubRepositoryImpactRepository>());
        }

        var results = new List<GitHubRepositoryOwnerImpact>();
        foreach (var owner in owners
                     .Select(static value => value?.Trim())
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)!) {
            var repositories = await GetOwnerRepositoriesAsync(owner!).ConfigureAwait(false);
            results.Add(BuildOwnerImpact(owner!, repositories));
        }

        var topRepositories = results
            .SelectMany(static owner => owner.Repositories)
            .OrderByDescending(static repo => repo.Stars)
            .ThenByDescending(static repo => repo.Forks)
            .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return new GitHubRepositoryImpactSummary(results, topRepositories);
    }

    private static GitHubRepositoryOwnerImpact BuildOwnerImpact(string owner, IReadOnlyList<GitHubRepositoryImpactRepository> repositories) {
        var totalStars = repositories.Sum(static repo => repo.Stars);
        var totalForks = repositories.Sum(static repo => repo.Forks);
        var topRepository = repositories
            .OrderByDescending(static repo => repo.Stars)
            .ThenByDescending(static repo => repo.Forks)
            .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new GitHubRepositoryOwnerImpact(
            owner,
            repositories.Count,
            totalStars,
            totalForks,
            repositories,
            topRepository);
    }

    private static async Task<IReadOnlyList<GitHubRepositoryImpactRepository>> GetOwnerRepositoriesAsync(string owner) {
        var repositories = new List<GitHubRepositoryImpactRepository>();
        string? cursor = null;

        while (true) {
            var root = await QueryOwnerRepositoriesAsync(owner, cursor).ConfigureAwait(false);
            if (!GitHubGraphQlCli.TryGetProperty(root, "data", out var data) ||
                !GitHubGraphQlCli.TryGetProperty(data, "repositoryOwner", out var repositoryOwner)) {
                break;
            }

            if (repositoryOwner.ValueKind == JsonValueKind.Null) {
                break;
            }

            if (!GitHubGraphQlCli.TryGetProperty(repositoryOwner, "repositories", out var repositoryConnection) ||
                repositoryConnection.ValueKind != JsonValueKind.Object) {
                break;
            }

            if (GitHubGraphQlCli.TryGetProperty(repositoryConnection, "nodes", out var nodes) &&
                nodes.ValueKind == JsonValueKind.Array) {
                foreach (var node in nodes.EnumerateArray()) {
                    if (node.ValueKind != JsonValueKind.Object) {
                        continue;
                    }

                    var nameWithOwner = GitHubGraphQlCli.ReadString(node, "nameWithOwner");
                    if (string.IsNullOrWhiteSpace(nameWithOwner)) {
                        continue;
                    }

                    repositories.Add(new GitHubRepositoryImpactRepository(
                        nameWithOwner,
                        GitHubGraphQlCli.ReadString(node, "url"),
                        ReadInt32(node, "stargazerCount"),
                        ReadInt32(node, "forkCount"),
                        ReadStringFromNested(node, "primaryLanguage", "name"),
                        ReadStringFromNested(node, "primaryLanguage", "color"),
                        GitHubGraphQlCli.ReadString(node, "pushedAt"),
                        ReadInt32FromNested(node, "watchers", "totalCount"),
                        ReadInt32FromNested(node, "issues", "totalCount"),
                        GitHubGraphQlCli.ReadString(node, "description"),
                        ReadBoolean(node, "isArchived"),
                        ReadBoolean(node, "isFork")));
                }
            }

            if (!GitHubGraphQlCli.TryGetProperty(repositoryConnection, "pageInfo", out var pageInfo) ||
                pageInfo.ValueKind != JsonValueKind.Object) {
                break;
            }

            var hasNextPage = false;
            if (GitHubGraphQlCli.TryGetProperty(pageInfo, "hasNextPage", out var hasNextPageValue) &&
                hasNextPageValue.ValueKind == JsonValueKind.True) {
                hasNextPage = true;
            }

            var endCursor = GitHubGraphQlCli.ReadString(pageInfo, "endCursor");
            if (!hasNextPage || string.IsNullOrWhiteSpace(endCursor)) {
                break;
            }

            cursor = endCursor;
        }

        return repositories;
    }

    private static async Task<JsonElement> QueryOwnerRepositoriesAsync(string owner, string? afterCursor) {
        var query = """
query($login: String!, $first: Int!, $after: String) {
  repositoryOwner(login: $login) {
    login
    repositories(
      first: $first
      after: $after
      ownerAffiliations: OWNER
      isFork: false
      privacy: PUBLIC
      orderBy: { field: STARGAZERS, direction: DESC }
    ) {
      nodes {
        nameWithOwner
        url
        description
        stargazerCount
        forkCount
        isArchived
        isFork
        pushedAt
        watchers {
          totalCount
        }
        issues(states: OPEN) {
          totalCount
        }
        primaryLanguage {
          name
          color
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
            ("login", owner),
            ("first", PageSize.ToString(CultureInfo.InvariantCulture)),
            ("after", afterCursor)).ConfigureAwait(false);
    }

    private static int ReadInt32(JsonElement obj, string name) {
        if (GitHubGraphQlCli.TryGetProperty(obj, name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed)) {
            return parsed;
        }

        return 0;
    }

    private static string? ReadStringFromNested(JsonElement obj, string parentName, string name) {
        return GitHubGraphQlCli.TryGetProperty(obj, parentName, out var parent) &&
               parent.ValueKind == JsonValueKind.Object
            ? GitHubGraphQlCli.ReadString(parent, name)
            : null;
    }

    private static int ReadInt32FromNested(JsonElement obj, string parentName, string name) {
        if (!GitHubGraphQlCli.TryGetProperty(obj, parentName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object) {
            return 0;
        }

        return ReadInt32(parent, name);
    }

    private static bool ReadBoolean(JsonElement obj, string name) {
        return GitHubGraphQlCli.TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}

internal sealed class GitHubRepositoryImpactSummary {
    public GitHubRepositoryImpactSummary(
        IReadOnlyList<GitHubRepositoryOwnerImpact> owners,
        IReadOnlyList<GitHubRepositoryImpactRepository> topRepositories) {
        Owners = owners ?? Array.Empty<GitHubRepositoryOwnerImpact>();
        TopRepositories = topRepositories ?? Array.Empty<GitHubRepositoryImpactRepository>();
    }

    public IReadOnlyList<GitHubRepositoryOwnerImpact> Owners { get; }
    public IReadOnlyList<GitHubRepositoryImpactRepository> TopRepositories { get; }
    public int TotalRepositories => Owners.Sum(static owner => owner.RepositoryCount);
    public int TotalStars => Owners.Sum(static owner => owner.TotalStars);
    public int TotalForks => Owners.Sum(static owner => owner.TotalForks);
}

internal sealed class GitHubRepositoryOwnerImpact {
    public GitHubRepositoryOwnerImpact(
        string owner,
        int repositoryCount,
        int totalStars,
        int totalForks,
        IReadOnlyList<GitHubRepositoryImpactRepository> repositories,
        GitHubRepositoryImpactRepository? topRepository) {
        Owner = owner;
        RepositoryCount = Math.Max(0, repositoryCount);
        TotalStars = Math.Max(0, totalStars);
        TotalForks = Math.Max(0, totalForks);
        Repositories = repositories ?? Array.Empty<GitHubRepositoryImpactRepository>();
        TopRepository = topRepository;
    }

    public string Owner { get; }
    public int RepositoryCount { get; }
    public int TotalStars { get; }
    public int TotalForks { get; }
    public IReadOnlyList<GitHubRepositoryImpactRepository> Repositories { get; }
    public GitHubRepositoryImpactRepository? TopRepository { get; }
}

internal sealed class GitHubRepositoryImpactRepository {
    public GitHubRepositoryImpactRepository(
        string nameWithOwner,
        string? url,
        int stars,
        int forks,
        string? primaryLanguage,
        string? primaryLanguageColor,
        string? pushedAt,
        int watchers = 0,
        int openIssues = 0,
        string? description = null,
        bool isArchived = false,
        bool isFork = false) {
        NameWithOwner = nameWithOwner;
        Url = url;
        Stars = Math.Max(0, stars);
        Forks = Math.Max(0, forks);
        PrimaryLanguage = primaryLanguage;
        PrimaryLanguageColor = primaryLanguageColor;
        PushedAt = pushedAt;
        Watchers = Math.Max(0, watchers);
        OpenIssues = Math.Max(0, openIssues);
        Description = description;
        IsArchived = isArchived;
        IsFork = isFork;
    }

    public string NameWithOwner { get; }
    public string? Url { get; }
    public int Stars { get; }
    public int Forks { get; }
    public string? PrimaryLanguage { get; }
    public string? PrimaryLanguageColor { get; }
    public string? PushedAt { get; }
    public int Watchers { get; }
    public int OpenIssues { get; }
    public string? Description { get; }
    public bool IsArchived { get; }
    public bool IsFork { get; }
}
