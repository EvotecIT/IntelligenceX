using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal sealed partial class ProjectV2Client {
    public async Task<IReadOnlyDictionary<string, ProjectItem>> GetProjectItemsByUrlAsync(string owner, int number, int maxItems) {
        var items = new Dictionary<string, ProjectItem>(StringComparer.OrdinalIgnoreCase);
        if (maxItems <= 0) {
            return items;
        }

        var query = """
query($owner: String!, $number: Int!, $cursor: String, $n: Int!) {
  user(login: $owner) {
    projectV2(number: $number) {
      items(first: $n, after: $cursor) {
        nodes {
          id
          content {
            __typename
            ... on Issue {
              id
              url
            }
            ... on PullRequest {
              id
              url
            }
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
  }
  organization(login: $owner) {
    projectV2(number: $number) {
      items(first: $n, after: $cursor) {
        nodes {
          id
          content {
            __typename
            ... on Issue {
              id
              url
            }
            ... on PullRequest {
              id
              url
            }
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
  }
}
""";

        string? cursor = null;
        while (items.Count < maxItems) {
            var batchSize = Math.Min(100, maxItems - items.Count);
            var root = await QueryGraphQlAsync(
                query,
                ("owner", owner),
                ("number", number.ToString(CultureInfo.InvariantCulture)),
                ("cursor", cursor),
                ("n", batchSize.ToString(CultureInfo.InvariantCulture))
            ).ConfigureAwait(false);

            if (!TryGetProperty(root, "data", out var data)) {
                break;
            }
            var connection = GetProjectConnection(data, "items");
            if (connection is null ||
                !TryGetProperty(connection.Value, "nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array) {
                break;
            }

            foreach (var node in nodes.EnumerateArray()) {
                var itemId = ReadString(node, "id");
                if (string.IsNullOrWhiteSpace(itemId)) {
                    continue;
                }
                if (!TryGetProperty(node, "content", out var content) || content.ValueKind != JsonValueKind.Object) {
                    continue;
                }
                var contentType = ReadString(content, "__typename");
                var contentId = ReadString(content, "id");
                var url = ReadString(content, "url");
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(contentId)) {
                    continue;
                }
                items[url] = new ProjectItem(itemId, url, contentId, contentType);
                if (items.Count >= maxItems) {
                    break;
                }
            }

            if (!TryGetProperty(connection.Value, "pageInfo", out var pageInfo) ||
                !TryGetProperty(pageInfo, "hasNextPage", out var hasNextPage) ||
                hasNextPage.ValueKind != JsonValueKind.True && hasNextPage.ValueKind != JsonValueKind.False ||
                !hasNextPage.GetBoolean()) {
                break;
            }
            cursor = ReadString(pageInfo, "endCursor");
            if (string.IsNullOrWhiteSpace(cursor)) {
                break;
            }
        }

        return items;
    }

    public async Task<ContentRef?> ResolveContentByUrlAsync(string url) {
        var query = """
query($url: URI!) {
  resource(url: $url) {
    __typename
    ... on Issue {
      id
      url
    }
    ... on PullRequest {
      id
      url
    }
  }
}
""";

        var root = await QueryGraphQlAsync(
            query,
            ("url", url)
        ).ConfigureAwait(false);

        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "resource", out var resource) ||
            resource.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var contentType = ReadString(resource, "__typename");
        if (!contentType.Equals("Issue", StringComparison.Ordinal) &&
            !contentType.Equals("PullRequest", StringComparison.Ordinal)) {
            return null;
        }
        var id = ReadString(resource, "id");
        var canonicalUrl = ReadString(resource, "url");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(canonicalUrl)) {
            return null;
        }
        return new ContentRef(id, canonicalUrl, contentType);
    }

    public async Task<string> AddProjectItemByContentIdAsync(string projectId, string contentId) {
        var mutation = """
mutation($projectId: ID!, $contentId: ID!) {
  addProjectV2ItemById(input: {
    projectId: $projectId
    contentId: $contentId
  }) {
    item {
      id
    }
  }
}
""";

        var root = await QueryGraphQlAsync(
            mutation,
            ("projectId", projectId),
            ("contentId", contentId)
        ).ConfigureAwait(false);

        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "addProjectV2ItemById", out var payload) ||
            !TryGetProperty(payload, "item", out var item) ||
            item.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("GitHub GraphQL response missing addProjectV2ItemById payload.");
        }
        var itemId = ReadString(item, "id");
        if (string.IsNullOrWhiteSpace(itemId)) {
            throw new InvalidOperationException("GitHub GraphQL addProjectV2ItemById did not return an item id.");
        }
        return itemId;
    }

    public async Task SetTextFieldAsync(string projectId, string itemId, string fieldId, string value) {
        var mutation = """
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: String!) {
  updateProjectV2ItemFieldValue(input: {
    projectId: $projectId
    itemId: $itemId
    fieldId: $fieldId
    value: {
      text: $value
    }
  }) {
    projectV2Item {
      id
    }
  }
}
""";
        await QueryGraphQlAsync(
            mutation,
            ("projectId", projectId),
            ("itemId", itemId),
            ("fieldId", fieldId),
            ("value", value)).ConfigureAwait(false);
    }

    public async Task SetNumberFieldAsync(string projectId, string itemId, string fieldId, double value) {
        var mutation = """
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: Float!) {
  updateProjectV2ItemFieldValue(input: {
    projectId: $projectId
    itemId: $itemId
    fieldId: $fieldId
    value: {
      number: $value
    }
  }) {
    projectV2Item {
      id
    }
  }
}
""";
        await QueryGraphQlAsync(
            mutation,
            ("projectId", projectId),
            ("itemId", itemId),
            ("fieldId", fieldId),
            ("value", value.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
    }

    public async Task SetSingleSelectFieldAsync(string projectId, string itemId, string fieldId, string optionId) {
        var mutation = """
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $optionId: String!) {
  updateProjectV2ItemFieldValue(input: {
    projectId: $projectId
    itemId: $itemId
    fieldId: $fieldId
    value: {
      singleSelectOptionId: $optionId
    }
  }) {
    projectV2Item {
      id
    }
  }
}
""";
        try {
            await QueryGraphQlAsync(
                mutation,
                ("projectId", projectId),
                ("itemId", itemId),
                ("fieldId", fieldId),
                ("optionId", optionId)).ConfigureAwait(false);
        } catch (Exception ex) {
            throw new InvalidOperationException(
                $"Failed to set single-select field '{fieldId}' with option '{optionId}' for item '{itemId}'. {ex.Message}",
                ex);
        }
    }

    public async Task ClearFieldAsync(string projectId, string itemId, string fieldId) {
        var mutation = """
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!) {
  clearProjectV2ItemFieldValue(input: {
    projectId: $projectId
    itemId: $itemId
    fieldId: $fieldId
  }) {
    projectV2Item {
      id
    }
  }
}
""";
        await QueryGraphQlAsync(
            mutation,
            ("projectId", projectId),
            ("itemId", itemId),
            ("fieldId", fieldId)).ConfigureAwait(false);
    }

    private static ProjectRef? ParseProjectRef(JsonElement obj) {
        if (obj.ValueKind != JsonValueKind.Object) {
            return null;
        }
        var id = ReadString(obj, "id");
        var title = ReadString(obj, "title");
        var url = ReadString(obj, "url");
        if (!TryGetProperty(obj, "number", out var numberProp) || numberProp.ValueKind != JsonValueKind.Number || !numberProp.TryGetInt32(out var number)) {
            return null;
        }
        if (string.IsNullOrWhiteSpace(id)) {
            return null;
        }
        return new ProjectRef(id, number, title, url);
    }

    private static JsonElement? GetProjectConnection(JsonElement data, string connectionName) {
        if (TryGetNestedObject(data, "user", "projectV2", out var userProject) &&
            TryGetProperty(userProject, connectionName, out var userConnection) &&
            userConnection.ValueKind == JsonValueKind.Object) {
            return userConnection;
        }
        if (TryGetNestedObject(data, "organization", "projectV2", out var orgProject) &&
            TryGetProperty(orgProject, connectionName, out var orgConnection) &&
            orgConnection.ValueKind == JsonValueKind.Object) {
            return orgConnection;
        }
        return null;
    }

    private static bool TryGetNestedObject(JsonElement root, string first, string second, out JsonElement value) {
        value = default;
        if (!TryGetProperty(root, first, out var firstObj) || firstObj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        if (!TryGetProperty(firstObj, second, out var secondObj) || secondObj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        value = secondObj;
        return true;
    }

    private static async Task<JsonElement> QueryGraphQlAsync(string query, params (string Key, string? Value)[] variables) {
        return await GitHubGraphQlCli.QueryAsync(query, TimeSpan.FromSeconds(90), variables).ConfigureAwait(false);
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }
}
