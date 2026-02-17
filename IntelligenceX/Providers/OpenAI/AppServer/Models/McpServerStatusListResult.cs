using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents one page of MCP server status results.
/// </summary>
public sealed class McpServerStatusListResult {
    /// <summary>
    /// Initializes a new MCP server status list result.
    /// </summary>
    public McpServerStatusListResult(IReadOnlyList<McpServerStatus> servers, string? nextCursor,
        JsonObject raw, JsonObject? additional) {
        Servers = servers;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets MCP server entries for the current page.
    /// </summary>
    public IReadOnlyList<McpServerStatus> Servers { get; }
    /// <summary>
    /// Gets the pagination cursor for the next page, or <see langword="null"/> when no further pages exist.
    /// </summary>
    public string? NextCursor { get; }
    /// <summary>
    /// Gets the original raw JSON payload returned by app-server.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses an MCP server status list from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed list result.</returns>
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
/// Represents the status and capabilities of a single MCP server.
/// </summary>
public sealed class McpServerStatus {
    /// <summary>
    /// Initializes a new MCP server status.
    /// </summary>
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

    /// <summary>
    /// Gets the MCP server name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets available tools keyed by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, McpToolInfo> Tools { get; }
    /// <summary>
    /// Gets currently exposed resources.
    /// </summary>
    public IReadOnlyList<McpResourceInfo> Resources { get; }
    /// <summary>
    /// Gets available resource templates.
    /// </summary>
    public IReadOnlyList<McpResourceTemplateInfo> ResourceTemplates { get; }
    /// <summary>
    /// Gets server authentication status.
    /// </summary>
    public McpAuthStatus AuthStatus { get; }
    /// <summary>
    /// Gets the original raw JSON payload for this server.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a server status from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed server status.</returns>
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
/// Describes an MCP tool.
/// </summary>
public sealed class McpToolInfo {
    /// <summary>
    /// Initializes a new MCP tool info.
    /// </summary>
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

    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the optional tool title.
    /// </summary>
    public string? Title { get; }
    /// <summary>
    /// Gets the optional tool description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Gets the input schema (when provided by the server).
    /// </summary>
    public JsonObject? InputSchema { get; }
    /// <summary>
    /// Gets the output schema (when provided by the server).
    /// </summary>
    public JsonObject? OutputSchema { get; }
    /// <summary>
    /// Gets tool annotations.
    /// </summary>
    public JsonObject? Annotations { get; }
    /// <summary>
    /// Gets tool metadata.
    /// </summary>
    public JsonObject? Meta { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses tool info from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <param name="fallbackName">Fallback tool name.</param>
    /// <returns>The parsed tool info.</returns>
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
/// Describes an MCP resource.
/// </summary>
public sealed class McpResourceInfo {
    /// <summary>
    /// Initializes a new MCP resource info.
    /// </summary>
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

    /// <summary>
    /// Gets the resource URI.
    /// </summary>
    public string? Uri { get; }
    /// <summary>
    /// Gets the resource name.
    /// </summary>
    public string? Name { get; }
    /// <summary>
    /// Gets the resource title.
    /// </summary>
    public string? Title { get; }
    /// <summary>
    /// Gets the resource description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Gets the mime type.
    /// </summary>
    public string? MimeType { get; }
    /// <summary>
    /// Gets the resource size in bytes when the server provides it.
    /// </summary>
    public long? Size { get; }
    /// <summary>
    /// Gets resource annotations.
    /// </summary>
    public JsonObject? Annotations { get; }
    /// <summary>
    /// Gets resource metadata.
    /// </summary>
    public JsonObject? Meta { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses resource info from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed resource info.</returns>
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
/// Describes an MCP resource template.
/// </summary>
public sealed class McpResourceTemplateInfo {
    /// <summary>
    /// Initializes a new MCP resource template info.
    /// </summary>
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

    /// <summary>
    /// Gets the URI template (for example <c>repo://{owner}/{name}</c>).
    /// </summary>
    public string? UriTemplate { get; }
    /// <summary>
    /// Gets the template name.
    /// </summary>
    public string? Name { get; }
    /// <summary>
    /// Gets the template title.
    /// </summary>
    public string? Title { get; }
    /// <summary>
    /// Gets the template description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Gets the mime type.
    /// </summary>
    public string? MimeType { get; }
    /// <summary>
    /// Gets template annotations.
    /// </summary>
    public JsonObject? Annotations { get; }
    /// <summary>
    /// Gets template metadata.
    /// </summary>
    public JsonObject? Meta { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a resource template from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed resource template.</returns>
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
/// Authentication status for an MCP server.
/// </summary>
public enum McpAuthStatus {
    /// <summary>
    /// Unknown auth status.
    /// </summary>
    Unknown,
    /// <summary>
    /// Authentication is not supported.
    /// </summary>
    Unsupported,
    /// <summary>
    /// Not logged in.
    /// </summary>
    NotLoggedIn,
    /// <summary>
    /// Bearer token authentication.
    /// </summary>
    BearerToken,
    /// <summary>
    /// OAuth authentication is available.
    /// </summary>
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
