using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubOwnerScopeResolver {
    private readonly Func<string, Task<JsonElement>> _queryUserAsync;

    public GitHubOwnerScopeResolver()
        : this(QueryUserOrganizationsAsync) {
    }

    internal GitHubOwnerScopeResolver(Func<string, Task<JsonElement>> queryUserAsync) {
        _queryUserAsync = queryUserAsync ?? throw new ArgumentNullException(nameof(queryUserAsync));
    }

    public async Task<IReadOnlyList<string>> ResolveAdministeredOwnersAsync(string login) {
        if (string.IsNullOrWhiteSpace(login)) {
            return Array.Empty<string>();
        }

        try {
            var root = await _queryUserAsync(login.Trim()).ConfigureAwait(false);
            return ParseOwners(root);
        } catch (InvalidOperationException) {
            return Array.Empty<string>();
        }
    }

    private static async Task<JsonElement> QueryUserOrganizationsAsync(string login) {
        var query = """
query($login: String!) {
  user(login: $login) {
    organizations(first: 100) {
      nodes {
        login
        viewerCanAdminister
        repositories(first: 1, isFork: false, privacy: PUBLIC) {
          totalCount
        }
      }
    }
  }
}
""";

        return await GitHubGraphQlCli.QueryAsync(
            query,
            TimeSpan.FromSeconds(90),
            ("login", login)).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ParseOwners(JsonElement root) {
        if (!GitHubGraphQlCli.TryGetProperty(root, "data", out var data) ||
            !GitHubGraphQlCli.TryGetProperty(data, "user", out var user) ||
            user.ValueKind != JsonValueKind.Object ||
            !GitHubGraphQlCli.TryGetProperty(user, "organizations", out var organizations) ||
            organizations.ValueKind != JsonValueKind.Object ||
            !GitHubGraphQlCli.TryGetProperty(organizations, "nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var owners = new List<string>();
        foreach (var node in nodes.EnumerateArray()) {
            if (node.ValueKind != JsonValueKind.Object) {
                continue;
            }

            var owner = GitHubGraphQlCli.ReadString(node, "login");
            if (string.IsNullOrWhiteSpace(owner) || !ReadBoolean(node, "viewerCanAdminister")) {
                continue;
            }

            if (ReadNestedInt32(node, "repositories", "totalCount") <= 0) {
                continue;
            }

            owners.Add(owner.Trim());
        }

        return owners
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static owner => owner, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ReadBoolean(JsonElement obj, string name) {
        return GitHubGraphQlCli.TryGetProperty(obj, name, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static int ReadNestedInt32(JsonElement obj, string parentName, string name) {
        if (!GitHubGraphQlCli.TryGetProperty(obj, parentName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object ||
            !GitHubGraphQlCli.TryGetProperty(parent, name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed)) {
            return 0;
        }

        return Math.Max(0, parsed);
    }
}
