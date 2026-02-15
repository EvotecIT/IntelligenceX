using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal sealed class ProjectV2Client {
    internal sealed record OwnerRef(string Login, string NodeId, string OwnerType);
    internal sealed record ProjectRef(string Id, int Number, string Title, string Url);
    internal sealed record ProjectField(string Id, string Name, string DataType, IReadOnlyDictionary<string, string> OptionsByName);
    internal sealed record ProjectView(string Id, string Name, string Layout, string Url);
    internal sealed record ProjectItem(string Id, string Url, string ContentId, string ContentType);
    internal sealed record ContentRef(string Id, string Url, string ContentType);

    public async Task<OwnerRef> GetOwnerAsync(string owner) {
        var query = """
query($owner: String!) {
  user(login: $owner) {
    id
    login
  }
  organization(login: $owner) {
    id
    login
  }
}
""";
        var root = await QueryGraphQlAsync(
            query,
            ("owner", owner)
        ).ConfigureAwait(false);

        if (!TryGetProperty(root, "data", out var data)) {
            throw new InvalidOperationException("GitHub GraphQL response missing data for owner query.");
        }

        if (TryGetProperty(data, "user", out var user) && user.ValueKind == JsonValueKind.Object) {
            var id = ReadString(user, "id");
            var login = ReadString(user, "login");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(login)) {
                return new OwnerRef(login, id, "user");
            }
        }

        if (TryGetProperty(data, "organization", out var org) && org.ValueKind == JsonValueKind.Object) {
            var id = ReadString(org, "id");
            var login = ReadString(org, "login");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(login)) {
                return new OwnerRef(login, id, "organization");
            }
        }

        throw new InvalidOperationException($"Owner '{owner}' was not found as a user or organization.");
    }

    public async Task<ProjectRef?> TryGetProjectAsync(string owner, int number) {
        var query = """
query($owner: String!, $number: Int!) {
  user(login: $owner) {
    projectV2(number: $number) {
      id
      number
      title
      url
    }
  }
  organization(login: $owner) {
    projectV2(number: $number) {
      id
      number
      title
      url
    }
  }
}
""";

        var root = await QueryGraphQlAsync(
            query,
            ("owner", owner),
            ("number", number.ToString(CultureInfo.InvariantCulture))
        ).ConfigureAwait(false);

        if (!TryGetProperty(root, "data", out var data)) {
            return null;
        }

        if (TryGetNestedObject(data, "user", "projectV2", out var userProject)) {
            var parsed = ParseProjectRef(userProject);
            if (parsed is not null) {
                return parsed;
            }
        }
        if (TryGetNestedObject(data, "organization", "projectV2", out var orgProject)) {
            var parsed = ParseProjectRef(orgProject);
            if (parsed is not null) {
                return parsed;
            }
        }
        return null;
    }

    public async Task<ProjectRef> CreateProjectAsync(string ownerNodeId, string title) {
        var mutation = """
mutation($ownerId: ID!, $title: String!) {
  createProjectV2(input: {
    ownerId: $ownerId
    title: $title
  }) {
    projectV2 {
      id
      number
      title
      url
    }
  }
}
""";

        var root = await QueryGraphQlAsync(
            mutation,
            ("ownerId", ownerNodeId),
            ("title", title)
        ).ConfigureAwait(false);

        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "createProjectV2", out var createPayload) ||
            !TryGetProperty(createPayload, "projectV2", out var projectObj) ||
            projectObj.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("GitHub GraphQL response missing project creation payload.");
        }

        return ParseProjectRef(projectObj) ??
               throw new InvalidOperationException("Unable to parse created project details.");
    }

    public async Task<ProjectRef> CopyProjectAsync(string sourceProjectId, string ownerNodeId, string title, bool includeDraftIssues) {
        var mutation = """
mutation($projectId: ID!, $ownerId: ID!, $title: String!, $includeDraftIssues: Boolean!) {
  copyProjectV2(input: {
    projectId: $projectId
    ownerId: $ownerId
    title: $title
    includeDraftIssues: $includeDraftIssues
  }) {
    projectV2 {
      id
      number
      title
      url
    }
  }
}
""";

        var root = await QueryGraphQlAsync(
            mutation,
            ("projectId", sourceProjectId),
            ("ownerId", ownerNodeId),
            ("title", title),
            ("includeDraftIssues", includeDraftIssues ? "true" : "false")
        ).ConfigureAwait(false);

        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "copyProjectV2", out var payload) ||
            !TryGetProperty(payload, "projectV2", out var projectObj) ||
            projectObj.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("GitHub GraphQL response missing project copy payload.");
        }

        return ParseProjectRef(projectObj) ??
               throw new InvalidOperationException("Unable to parse copied project details.");
    }

    public async Task UpdateProjectAsync(string projectId, string? description, bool? isPublic) {
        if (string.IsNullOrWhiteSpace(projectId)) {
            throw new InvalidOperationException("Project id is required.");
        }
        if (description is null && !isPublic.HasValue) {
            return;
        }

        var mutation = """
mutation($projectId: ID!, $shortDescription: String, $public: Boolean) {
  updateProjectV2(input: {
    projectId: $projectId
    shortDescription: $shortDescription
    public: $public
  }) {
    projectV2 {
      id
    }
  }
}
""";

        await QueryGraphQlAsync(
            mutation,
            ("projectId", projectId),
            ("shortDescription", description),
            ("public", isPublic.HasValue ? (isPublic.Value ? "true" : "false") : null)
        ).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, ProjectField>> GetProjectFieldsByNameAsync(string owner, int number) {
        var fields = new Dictionary<string, ProjectField>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        var query = """
query($owner: String!, $number: Int!, $cursor: String) {
  user(login: $owner) {
    projectV2(number: $number) {
      fields(first: 100, after: $cursor) {
        nodes {
          __typename
          ... on ProjectV2Field {
            id
            name
            dataType
          }
          ... on ProjectV2SingleSelectField {
            id
            name
            dataType
            options {
              id
              name
            }
          }
          ... on ProjectV2IterationField {
            id
            name
            dataType
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
      fields(first: 100, after: $cursor) {
        nodes {
          __typename
          ... on ProjectV2Field {
            id
            name
            dataType
          }
          ... on ProjectV2SingleSelectField {
            id
            name
            dataType
            options {
              id
              name
            }
          }
          ... on ProjectV2IterationField {
            id
            name
            dataType
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

        while (true) {
            var root = await QueryGraphQlAsync(
                query,
                ("owner", owner),
                ("number", number.ToString(CultureInfo.InvariantCulture)),
                ("cursor", cursor)
            ).ConfigureAwait(false);

            if (!TryGetProperty(root, "data", out var data)) {
                break;
            }

            var connection = GetProjectConnection(data, "fields");
            if (connection is null) {
                break;
            }
            if (!TryGetProperty(connection.Value, "nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) {
                break;
            }

            foreach (var fieldNode in nodes.EnumerateArray()) {
                var id = ReadString(fieldNode, "id");
                var name = ReadString(fieldNode, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) {
                    continue;
                }
                var dataType = ReadString(fieldNode, "dataType");
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (TryGetProperty(fieldNode, "options", out var optionsArray) &&
                    optionsArray.ValueKind == JsonValueKind.Array) {
                    foreach (var option in optionsArray.EnumerateArray()) {
                        var optionId = ReadString(option, "id");
                        var optionName = ReadString(option, "name");
                        if (!string.IsNullOrWhiteSpace(optionId) && !string.IsNullOrWhiteSpace(optionName)) {
                            options[optionName] = optionId;
                        }
                    }
                }
                fields[name] = new ProjectField(id, name, dataType, options);
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

        return fields;
    }

    public async Task<IReadOnlyDictionary<string, ProjectView>> GetProjectViewsByNameAsync(string owner, int number) {
        var views = new Dictionary<string, ProjectView>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        var query = """
query($owner: String!, $number: Int!, $cursor: String) {
  user(login: $owner) {
    projectV2(number: $number) {
      views(first: 100, after: $cursor) {
        nodes {
          id
          name
          layout
          url
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
      views(first: 100, after: $cursor) {
        nodes {
          id
          name
          layout
          url
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

        while (true) {
            var root = await QueryGraphQlAsync(
                query,
                ("owner", owner),
                ("number", number.ToString(CultureInfo.InvariantCulture)),
                ("cursor", cursor)
            ).ConfigureAwait(false);

            if (!TryGetProperty(root, "data", out var data)) {
                break;
            }

            var connection = GetProjectConnection(data, "views");
            if (connection is null ||
                !TryGetProperty(connection.Value, "nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array) {
                break;
            }

            foreach (var viewNode in nodes.EnumerateArray()) {
                var id = ReadString(viewNode, "id");
                var name = ReadString(viewNode, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) {
                    continue;
                }

                var layout = ReadString(viewNode, "layout");
                var url = ReadString(viewNode, "url");
                views[name] = new ProjectView(id, name, layout, url);
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

        return views;
    }

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
        var args = new List<string> {
            "api",
            "graphql",
            "-f",
            $"query={query}"
        };
        var variableTypes = ParseVariableTypes(query);
        foreach (var (key, value) in variables) {
            if (value is null) {
                continue;
            }
            args.Add(UseTypedFormValue(variableTypes, key) ? "-F" : "-f");
            args.Add($"{key}={value}");
        }

        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90), args.ToArray()).ConfigureAwait(false);
        JsonElement root;
        try {
            using var doc = JsonDocument.Parse(stdout);
            root = doc.RootElement.Clone();
        } catch (Exception) {
            if (code != 0) {
                throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
            }
            throw;
        }

        if (code != 0) {
            if (root.TryGetProperty("errors", out var nonZeroErrors) &&
                nonZeroErrors.ValueKind == JsonValueKind.Array &&
                nonZeroErrors.GetArrayLength() > 0 &&
                ShouldIgnoreErrors(root, nonZeroErrors)) {
                return root;
            }

            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
        }

        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0) {
            if (ShouldIgnoreErrors(root, errors)) {
                return root;
            }
            var first = errors[0];
            var message = first.TryGetProperty("message", out var msg)
                ? (msg.GetString() ?? "GraphQL error")
                : "GraphQL error";
            throw new InvalidOperationException($"GitHub GraphQL returned errors: {message}");
        }
        return root;
    }

    private static bool ShouldIgnoreErrors(JsonElement root, JsonElement errors) {
        if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Object) {
            return false;
        }

        foreach (var error in errors.EnumerateArray()) {
            var message = ReadString(error, "message");
            if (string.IsNullOrWhiteSpace(message)) {
                return false;
            }

            if (message.Contains("Could not resolve to a User with the login of", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (message.Contains("Could not resolve to an Organization with the login of", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string> ParseVariableTypes(string query) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(query, @"\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z0-9_!\[\]]+)")) {
            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type)) {
                map[name] = type;
            }
        }
        return map;
    }

    private static bool UseTypedFormValue(IReadOnlyDictionary<string, string> variableTypes, string key) {
        if (!variableTypes.TryGetValue(key, out var graphType) || string.IsNullOrWhiteSpace(graphType)) {
            return true;
        }

        var normalized = graphType.Replace("!", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        return normalized.Equals("Int", StringComparison.Ordinal) ||
               normalized.Equals("Float", StringComparison.Ordinal) ||
               normalized.Equals("Boolean", StringComparison.Ordinal);
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
