using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal sealed partial class ProjectV2Client {
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

    public async Task<bool> SupportsProjectViewCreationAsync() {
        var query = """
query {
  __type(name: "Mutation") {
    fields {
      name
    }
  }
}
""";

        var root = await QueryGraphQlAsync(query).ConfigureAwait(false);
        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "__type", out var mutationType) ||
            mutationType.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(mutationType, "fields", out var fields) ||
            fields.ValueKind != JsonValueKind.Array) {
            return false;
        }

        foreach (var field in fields.EnumerateArray()) {
            var name = ReadString(field, "name");
            if (name.Equals("createProjectV2View", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

}
