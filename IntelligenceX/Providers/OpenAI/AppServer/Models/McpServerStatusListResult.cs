using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a paged list of MCP server statuses.
/// </summary>
public sealed class McpServerStatusListResult {
    /// <summary>Creates a server status list result.</summary>
    /// <param name="servers">The servers returned by the API.</param>
    /// <param name="nextCursor">The cursor for the next page, if any.</param>
    /// <param name="raw">The raw JSON payload.</param>
    /// <param name="additional">Additional unmodeled JSON data.</param>
    public McpServerStatusListResult(IReadOnlyList<McpServerStatus> servers, string? nextCursor,
        JsonObject raw, JsonObject? additional) {
        Servers = servers;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Gets the servers returned by the API.</summary>
    public IReadOnlyList<McpServerStatus> Servers { get; }
    /// <summary>Gets the pagination cursor for the next page, if any.</summary>
    public string? NextCursor { get; }
    /// <summary>Gets the raw JSON payload.</summary>
    public JsonObject Raw { get; }
    /// <summary>Gets additional unmodeled JSON data.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a list result from JSON.</summary>
    /// <param name="obj">The JSON object to parse.</param>
    public static McpServerStatusListResult FromJson(JsonObject obj) {
        var servers = new List<McpServerStatus>();
        var data = obj.GetArray("data") ?? obj.GetArray("items");
        if (data is not null) {
            foreach (var item in data) {
                var serverObj = item.AsObject();
                if (serverObj is null) {
                    continue;
                }
                servers.Add(McpServerStatus.FromJson(serverObj));
            }
        }
        var nextCursor = obj.GetString("nextCursor") ?? obj.GetString("next_cursor");
        var additional = obj.ExtractAdditional("data", "items", "nextCursor", "next_cursor");
        return new McpServerStatusListResult(servers, nextCursor, obj, additional);
    }
}

/// <summary>
/// Represents the status of a single MCP server.
/// </summary>
public sealed class McpServerStatus {
    /// <summary>Creates a new MCP server status.</summary>
    /// <param name="name">The server name.</param>
    /// <param name="tools">The tool list keyed by name.</param>
    /// <param name="resources">The available resources.</param>
    /// <param name="resourceTemplates">The available resource templates.</param>
    /// <param name="authStatus">The authentication status.</param>
    /// <param name="raw">The raw JSON payload.</param>
    /// <param name="additional">Additional unmodeled JSON data.</param>
    public McpServerStatus(string name, IReadOnlyDictionary<string, McpToolInfo> tools, IReadOnlyList<McpResourceInfo> resources,
        IReadOnlyList<McpResourceTemplateInfo> resourceTemplates, McpAuthStatus authStatus, JsonObject raw, JsonObject? additional) {
        Name = name;
        Tools = tools;
        Resources = resources;
        ResourceTemplates = resourceTemplates;
        AuthStatus = authStatus;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Gets the server name.</summary>
    public string Name { get; }
    /// <summary>Gets the tools supported by the server.</summary>
    public IReadOnlyDictionary<string, McpToolInfo> Tools { get; }
    /// <summary>Gets the resources provided by the server.</summary>
    public IReadOnlyList<McpResourceInfo> Resources { get; }
    /// <summary>Gets the resource templates provided by the server.</summary>
    public IReadOnlyList<McpResourceTemplateInfo> ResourceTemplates { get; }
    /// <summary>Gets the authentication status.</summary>
    public McpAuthStatus AuthStatus { get; }
    /// <summary>Gets the raw JSON payload.</summary>
    public JsonObject Raw { get; }
    /// <summary>Gets additional unmodeled JSON data.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a server status from JSON.</summary>
    /// <param name="obj">The JSON object to parse.</param>
    public static McpServerStatus FromJson(JsonObject obj) {
        var name = obj.GetString("name") ?? string.Empty;

        var tools = new Dictionary<string, McpToolInfo>(StringComparer.Ordinal);
        var toolsObj = obj.GetObject("tools");
        if (toolsObj is not null) {
            foreach (var entry in toolsObj) {
                var toolObj = entry.Value?.AsObject();
                if (toolObj is null) {
                    continue;
                }
                tools[entry.Key] = McpToolInfo.FromJson(toolObj, entry.Key);
            }
        }

        var resources = new List<McpResourceInfo>();
        var resourcesArray = obj.GetArray("resources");
        if (resourcesArray is not null) {
            foreach (var item in resourcesArray) {
                var resourceObj = item.AsObject();
                if (resourceObj is null) {
                    continue;
                }
                resources.Add(McpResourceInfo.FromJson(resourceObj));
            }
        }

        var templates = new List<McpResourceTemplateInfo>();
        var templatesArray = obj.GetArray("resourceTemplates") ?? obj.GetArray("resource_templates");
        if (templatesArray is not null) {
            foreach (var item in templatesArray) {
                var templateObj = item.AsObject();
                if (templateObj is null) {
                    continue;
                }
                templates.Add(McpResourceTemplateInfo.FromJson(templateObj));
            }
        }

        var authStatusRaw = obj.GetString("authStatus") ?? obj.GetString("auth_status");
        var authStatus = McpAuthStatusExtensions.Parse(authStatusRaw);

        var additional = obj.ExtractAdditional(
            "name", "tools", "resources", "resourceTemplates", "resource_templates", "authStatus", "auth_status");
        return new McpServerStatus(name, tools, resources, templates, authStatus, obj, additional);
    }
}

/// <summary>
/// Represents a tool provided by an MCP server.
/// </summary>
public sealed class McpToolInfo {
    /// <summary>Creates a new MCP tool descriptor.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="title">The tool title.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="inputSchema">The input JSON schema.</param>
    /// <param name="outputSchema">The output JSON schema.</param>
    /// <param name="annotations">Additional annotations.</param>
    /// <param name="meta">The metadata object.</param>
    /// <param name="raw">The raw JSON payload.</param>
    /// <param name="additional">Additional unmodeled JSON data.</param>
    public McpToolInfo(string name, string? title, string? description, JsonObject? inputSchema,
        JsonObject? outputSchema, JsonObject? annotations, JsonObject? meta, JsonObject raw, JsonObject? additional) {
        Name = name;
        Title = title;
        Description = description;
        InputSchema = inputSchema;
        OutputSchema = outputSchema;
        Annotations = annotations;
        Meta = meta;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Gets the tool name.</summary>
    public string Name { get; }
    /// <summary>Gets the tool title.</summary>
    public string? Title { get; }
    /// <summary>Gets the tool description.</summary>
    public string? Description { get; }
    /// <summary>Gets the input schema.</summary>
    public JsonObject? InputSchema { get; }
    /// <summary>Gets the output schema.</summary>
    public JsonObject? OutputSchema { get; }
    /// <summary>Gets the annotations object.</summary>
    public JsonObject? Annotations { get; }
    /// <summary>Gets the metadata object.</summary>
    public JsonObject? Meta { get; }
    /// <summary>Gets the raw JSON payload.</summary>
    public JsonObject Raw { get; }
    /// <summary>Gets additional unmodeled JSON data.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a tool descriptor from JSON.</summary>
    /// <param name="obj">The JSON object to parse.</param>
    /// <param name="fallbackName">The fallback name to use when the JSON is missing a name.</param>
    public static McpToolInfo FromJson(JsonObject obj, string? fallbackName = null) {
        var name = obj.GetString("name") ?? fallbackName ?? string.Empty;
        var title = obj.GetString("title");
        var description = obj.GetString("description");
        var inputSchema = obj.GetObject("inputSchema") ?? obj.GetObject("input_schema");
        var outputSchema = obj.GetObject("outputSchema") ?? obj.GetObject("output_schema");
        var annotations = obj.GetObject("annotations");
        var meta = obj.GetObject("_meta");
        var additional = obj.ExtractAdditional(
            "name", "title", "description", "inputSchema", "input_schema", "outputSchema", "output_schema", "annotations", "_meta");
        return new McpToolInfo(name, title, description, inputSchema, outputSchema, annotations, meta, obj, additional);
    }
}

/// <summary>
/// Represents a resource provided by an MCP server.
/// </summary>
public sealed class McpResourceInfo {
    /// <summary>Creates a new resource descriptor.</summary>
    /// <param name="uri">The resource URI.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="title">The resource title.</param>
    /// <param name="description">The resource description.</param>
    /// <param name="mimeType">The resource MIME type.</param>
    /// <param name="size">The resource size in bytes.</param>
    /// <param name="annotations">Additional annotations.</param>
    /// <param name="meta">The metadata object.</param>
    /// <param name="raw">The raw JSON payload.</param>
    /// <param name="additional">Additional unmodeled JSON data.</param>
    public McpResourceInfo(string? uri, string? name, string? title, string? description, string? mimeType,
        long? size, JsonObject? annotations, JsonObject? meta, JsonObject raw, JsonObject? additional) {
        Uri = uri;
        Name = name;
        Title = title;
        Description = description;
        MimeType = mimeType;
        Size = size;
        Annotations = annotations;
        Meta = meta;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Gets the resource URI.</summary>
    public string? Uri { get; }
    /// <summary>Gets the resource name.</summary>
    public string? Name { get; }
    /// <summary>Gets the resource title.</summary>
    public string? Title { get; }
    /// <summary>Gets the resource description.</summary>
    public string? Description { get; }
    /// <summary>Gets the MIME type.</summary>
    public string? MimeType { get; }
    /// <summary>Gets the size in bytes.</summary>
    public long? Size { get; }
    /// <summary>Gets the annotations object.</summary>
    public JsonObject? Annotations { get; }
    /// <summary>Gets the metadata object.</summary>
    public JsonObject? Meta { get; }
    /// <summary>Gets the raw JSON payload.</summary>
    public JsonObject Raw { get; }
    /// <summary>Gets additional unmodeled JSON data.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a resource descriptor from JSON.</summary>
    /// <param name="obj">The JSON object to parse.</param>
    public static McpResourceInfo FromJson(JsonObject obj) {
        var uri = obj.GetString("uri");
        var name = obj.GetString("name");
        var title = obj.GetString("title");
        var description = obj.GetString("description");
        var mimeType = obj.GetString("mimeType") ?? obj.GetString("mime_type");
        var size = obj.GetInt64("size");
        var annotations = obj.GetObject("annotations");
        var meta = obj.GetObject("_meta");
        var additional = obj.ExtractAdditional(
            "uri", "name", "title", "description", "mimeType", "mime_type", "size", "annotations", "_meta");
        return new McpResourceInfo(uri, name, title, description, mimeType, size, annotations, meta, obj, additional);
    }
}

/// <summary>
/// Represents a resource template provided by an MCP server.
/// </summary>
public sealed class McpResourceTemplateInfo {
    /// <summary>Creates a new resource template descriptor.</summary>
    /// <param name="uriTemplate">The resource URI template.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="title">The resource title.</param>
    /// <param name="description">The resource description.</param>
    /// <param name="mimeType">The resource MIME type.</param>
    /// <param name="annotations">Additional annotations.</param>
    /// <param name="meta">The metadata object.</param>
    /// <param name="raw">The raw JSON payload.</param>
    /// <param name="additional">Additional unmodeled JSON data.</param>
    public McpResourceTemplateInfo(string? uriTemplate, string? name, string? title, string? description,
        string? mimeType, JsonObject? annotations, JsonObject? meta, JsonObject raw, JsonObject? additional) {
        UriTemplate = uriTemplate;
        Name = name;
        Title = title;
        Description = description;
        MimeType = mimeType;
        Annotations = annotations;
        Meta = meta;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Gets the resource URI template.</summary>
    public string? UriTemplate { get; }
    /// <summary>Gets the resource name.</summary>
    public string? Name { get; }
    /// <summary>Gets the resource title.</summary>
    public string? Title { get; }
    /// <summary>Gets the resource description.</summary>
    public string? Description { get; }
    /// <summary>Gets the MIME type.</summary>
    public string? MimeType { get; }
    /// <summary>Gets the annotations object.</summary>
    public JsonObject? Annotations { get; }
    /// <summary>Gets the metadata object.</summary>
    public JsonObject? Meta { get; }
    /// <summary>Gets the raw JSON payload.</summary>
    public JsonObject Raw { get; }
    /// <summary>Gets additional unmodeled JSON data.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a resource template descriptor from JSON.</summary>
    /// <param name="obj">The JSON object to parse.</param>
    public static McpResourceTemplateInfo FromJson(JsonObject obj) {
        var uriTemplate = obj.GetString("uriTemplate") ?? obj.GetString("uri_template");
        var name = obj.GetString("name");
        var title = obj.GetString("title");
        var description = obj.GetString("description");
        var mimeType = obj.GetString("mimeType") ?? obj.GetString("mime_type");
        var annotations = obj.GetObject("annotations");
        var meta = obj.GetObject("_meta");
        var additional = obj.ExtractAdditional(
            "uriTemplate", "uri_template", "name", "title", "description", "mimeType", "mime_type", "annotations", "_meta");
        return new McpResourceTemplateInfo(uriTemplate, name, title, description, mimeType, annotations, meta, obj, additional);
    }
}

/// <summary>
/// Describes the authentication status for an MCP server.
/// </summary>
public enum McpAuthStatus {
    /// <summary>The status is unknown.</summary>
    Unknown,
    /// <summary>The server does not support authentication.</summary>
    Unsupported,
    /// <summary>The server requires auth and no login is present.</summary>
    NotLoggedIn,
    /// <summary>The server is authenticated with a bearer token.</summary>
    BearerToken,
    /// <summary>The server is authenticated with OAuth.</summary>
    OAuth
}

internal static class McpAuthStatusExtensions {
    public static McpAuthStatus Parse(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return McpAuthStatus.Unknown;
        }
        var normalized = value!.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch {
            "unsupported" => McpAuthStatus.Unsupported,
            "not_logged_in" => McpAuthStatus.NotLoggedIn,
            "bearer_token" => McpAuthStatus.BearerToken,
            "oauth" => McpAuthStatus.OAuth,
            _ => McpAuthStatus.Unknown
        };
    }
}
