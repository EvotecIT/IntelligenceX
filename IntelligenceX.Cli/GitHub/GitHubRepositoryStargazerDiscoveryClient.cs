using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubRepositoryStargazerDiscoveryClient {
    private const int DefaultPageSize = 100;

    public async Task<IReadOnlyList<GitHubRepositoryStargazerRecord>> GetStargazersAsync(string repositoryNameWithOwner, int limit = 200) {
        if (string.IsNullOrWhiteSpace(repositoryNameWithOwner)) {
            throw new ArgumentException("Repository name is required.", nameof(repositoryNameWithOwner));
        }
        if (limit <= 0) {
            return Array.Empty<GitHubRepositoryStargazerRecord>();
        }

        var (owner, repository) = SplitRepositoryName(repositoryNameWithOwner);
        return await GetStargazersAsync(owner, repository, limit, QueryStargazersPageAsync).ConfigureAwait(false);
    }

    internal static Task<IReadOnlyList<GitHubRepositoryStargazerRecord>> GetStargazersForTestAsync(
        string repositoryNameWithOwner,
        int limit,
        Func<string, string, int, int, Task<JsonElement>> queryStargazersPageAsync) {
        if (string.IsNullOrWhiteSpace(repositoryNameWithOwner)) {
            throw new ArgumentException("Repository name is required.", nameof(repositoryNameWithOwner));
        }

        ArgumentNullException.ThrowIfNull(queryStargazersPageAsync);
        var (owner, repository) = SplitRepositoryName(repositoryNameWithOwner);
        return GetStargazersAsync(owner, repository, limit, queryStargazersPageAsync);
    }

    private static async Task<IReadOnlyList<GitHubRepositoryStargazerRecord>> GetStargazersAsync(
        string owner,
        string repository,
        int limit,
        Func<string, string, int, int, Task<JsonElement>> queryStargazersPageAsync) {
        var records = new List<GitHubRepositoryStargazerRecord>(Math.Max(0, limit));
        var page = 1;
        var remaining = Math.Max(1, limit);

        while (remaining > 0) {
            var batchSize = Math.Min(DefaultPageSize, remaining);
            var root = await queryStargazersPageAsync(owner, repository, batchSize, page).ConfigureAwait(false);
            if (root.ValueKind != JsonValueKind.Array) {
                break;
            }

            var addedCount = 0;
            foreach (var item in root.EnumerateArray()) {
                if (item.ValueKind != JsonValueKind.Object ||
                    !TryGetProperty(item, "user", out var user) ||
                    user.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                var login = ReadString(user, "login");
                if (string.IsNullOrWhiteSpace(login)) {
                    continue;
                }

                records.Add(new GitHubRepositoryStargazerRecord(
                    login,
                    ReadString(user, "html_url"),
                    ReadString(user, "avatar_url"),
                    ReadString(item, "starred_at")));
                addedCount++;
                if (records.Count >= limit) {
                    break;
                }
            }

            if (addedCount <= 0 || records.Count >= limit || addedCount < batchSize) {
                break;
            }

            remaining = limit - records.Count;
            page++;
        }

        return records;
    }

    private static async Task<JsonElement> QueryStargazersPageAsync(string owner, string repository, int perPage, int page) {
        var endpoint = "repos/"
                       + Uri.EscapeDataString(owner)
                       + "/"
                       + Uri.EscapeDataString(repository)
                       + "/stargazers?per_page="
                       + perPage
                       + "&page="
                       + page;
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(90),
            "api",
            endpoint,
            "-H",
            "Accept: application/vnd.github.star+json").ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "Failed to query GitHub stargazers."
                : stderr.Trim());
        }

        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.Clone();
    }

    private static (string Owner, string Repository) SplitRepositoryName(string repositoryNameWithOwner) {
        var parts = repositoryNameWithOwner.Trim().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            throw new ArgumentException("Repository name must be in owner/name format.", nameof(repositoryNameWithOwner));
        }

        return (parts[0].Trim(), parts[1].Trim());
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value);
    }

    private static string? ReadString(JsonElement obj, string name) {
        return TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

internal sealed class GitHubRepositoryStargazerRecord {
    public GitHubRepositoryStargazerRecord(
        string login,
        string? profileUrl,
        string? avatarUrl,
        string? starredAt) {
        Login = string.IsNullOrWhiteSpace(login)
            ? throw new ArgumentException("Stargazer login is required.", nameof(login))
            : login.Trim();
        ProfileUrl = string.IsNullOrWhiteSpace(profileUrl) ? null : profileUrl.Trim();
        AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim();
        StarredAt = string.IsNullOrWhiteSpace(starredAt) ? null : starredAt.Trim();
    }

    public string Login { get; }
    public string? ProfileUrl { get; }
    public string? AvatarUrl { get; }
    public string? StarredAt { get; }
}
