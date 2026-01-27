using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class McpServerStatusListResult {
    public McpServerStatusListResult(IReadOnlyList<McpServerStatus> servers, string? nextCursor,
        JsonObject raw, JsonObject? additional) {
        Servers = servers;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<McpServerStatus> Servers { get; }
    public string? NextCursor { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public sealed class McpServerStatus {
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

    public string Name { get; }
    public IReadOnlyDictionary<string, McpToolInfo> Tools { get; }
    public IReadOnlyList<McpResourceInfo> Resources { get; }
    public IReadOnlyList<McpResourceTemplateInfo> ResourceTemplates { get; }
    public McpAuthStatus AuthStatus { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public sealed class McpToolInfo {
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

    public string Name { get; }
    public string? Title { get; }
    public string? Description { get; }
    public JsonObject? InputSchema { get; }
    public JsonObject? OutputSchema { get; }
    public JsonObject? Annotations { get; }
    public JsonObject? Meta { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public sealed class McpResourceInfo {
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

    public string? Uri { get; }
    public string? Name { get; }
    public string? Title { get; }
    public string? Description { get; }
    public string? MimeType { get; }
    public long? Size { get; }
    public JsonObject? Annotations { get; }
    public JsonObject? Meta { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public sealed class McpResourceTemplateInfo {
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

    public string? UriTemplate { get; }
    public string? Name { get; }
    public string? Title { get; }
    public string? Description { get; }
    public string? MimeType { get; }
    public JsonObject? Annotations { get; }
    public JsonObject? Meta { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public enum McpAuthStatus {
    Unknown,
    Unsupported,
    NotLoggedIn,
    BearerToken,
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
