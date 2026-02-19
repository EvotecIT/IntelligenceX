using System;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Small helpers for building tool input JSON schemas without repeating low-level JSON wiring.
/// </summary>
/// <remarks>
/// This intentionally supports only the subset of JSON Schema we use for tool calling.
/// </remarks>
public static class ToolSchema {
    /// <summary>
    /// Creates an object schema with a <c>properties</c> map.
    /// </summary>
    /// <param name="properties">Property definitions.</param>
    public static JsonObject Object(params (string Name, JsonObject Schema)[] properties) {
        var props = new JsonObject(StringComparer.Ordinal);
        if (properties is not null) {
            foreach (var p in properties) {
                if (string.IsNullOrWhiteSpace(p.Name) || p.Schema is null) {
                    continue;
                }
                props.Add(p.Name, p.Schema);
            }
        }

        return new JsonObject()
            .Add("type", "object")
            .Add("properties", props);
    }

    /// <summary>
    /// Creates a string schema.
    /// </summary>
    /// <param name="description">Optional description.</param>
    public static JsonObject String(string? description = null) {
        var s = new JsonObject().Add("type", "string");
        return string.IsNullOrWhiteSpace(description) ? s : s.Add("description", description);
    }

    /// <summary>
    /// Creates an integer schema.
    /// </summary>
    /// <param name="description">Optional description.</param>
    public static JsonObject Integer(string? description = null) {
        var s = new JsonObject().Add("type", "integer");
        return string.IsNullOrWhiteSpace(description) ? s : s.Add("description", description);
    }

    /// <summary>
    /// Creates a boolean schema.
    /// </summary>
    /// <param name="description">Optional description.</param>
    public static JsonObject Boolean(string? description = null) {
        var s = new JsonObject().Add("type", "boolean");
        return string.IsNullOrWhiteSpace(description) ? s : s.Add("description", description);
    }

    /// <summary>
    /// Creates an array schema with <c>items</c>.
    /// </summary>
    /// <param name="items">Item schema.</param>
    /// <param name="description">Optional description.</param>
    public static JsonObject Array(JsonObject items, string? description = null) {
        var s = new JsonObject()
            .Add("type", "array")
            .Add("items", items ?? new JsonObject());
        return string.IsNullOrWhiteSpace(description) ? s : s.Add("description", description);
    }
}

/// <summary>
/// Helper extension methods for tool input JSON schemas.
/// </summary>
public static class ToolSchemaExtensions {
    /// <summary>
    /// Adds a <c>required</c> array to an object schema.
    /// </summary>
    /// <param name="schema">Schema to modify.</param>
    /// <param name="required">Required property names.</param>
    public static JsonObject Required(this JsonObject schema, params string[] required) {
        if (schema is null) throw new ArgumentNullException(nameof(schema));

        var arr = new JsonArray();
        if (required is not null) {
            foreach (var r in required) {
                if (!string.IsNullOrWhiteSpace(r)) {
                    arr.Add(r);
                }
            }
        }
        if (arr.Count > 0) {
            schema.Add("required", arr);
        }
        return schema;
    }

    /// <summary>
    /// Sets <c>additionalProperties=false</c> on an object schema.
    /// </summary>
    /// <param name="schema">Schema to modify.</param>
    public static JsonObject NoAdditionalProperties(this JsonObject schema) {
        if (schema is null) throw new ArgumentNullException(nameof(schema));
        return schema.Add("additionalProperties", false);
    }

    /// <summary>
    /// Adds an <c>enum</c> array to a schema.
    /// </summary>
    /// <param name="schema">Schema to modify.</param>
    /// <param name="values">Allowed values.</param>
    public static JsonObject Enum(this JsonObject schema, params string[] values) {
        if (schema is null) throw new ArgumentNullException(nameof(schema));

        var arr = new JsonArray();
        if (values is not null) {
            foreach (var v in values) {
                if (!string.IsNullOrWhiteSpace(v)) {
                    arr.Add(v);
                }
            }
        }
        if (arr.Count > 0) {
            schema.Add("enum", arr);
        }
        return schema;
    }

    /// <summary>
    /// Adds standard tabular view options (<c>columns/sort_by/sort_direction/top</c>) to an object schema.
    /// </summary>
    /// <param name="schema">Schema to mutate.</param>
    /// <param name="columnsDescription">Optional custom description for <c>columns</c>.</param>
    /// <param name="sortByDescription">Optional custom description for <c>sort_by</c>.</param>
    /// <param name="topDescription">Optional custom description for <c>top</c>.</param>
    public static JsonObject WithTableViewOptions(
        this JsonObject schema,
        string? columnsDescription = null,
        string? sortByDescription = null,
        string? topDescription = null) {
        if (schema is null) {
            throw new ArgumentNullException(nameof(schema));
        }

        var properties = schema.GetObject("properties");
        if (properties is null) {
            return schema;
        }

        if (!properties.TryGetValue("columns", out _)) {
            properties.Add("columns", ToolSchema.Array(
                ToolSchema.String(),
                columnsDescription ?? "Optional output columns (projection)."));
        }

        if (!properties.TryGetValue("sort_by", out _)) {
            properties.Add("sort_by", ToolSchema.String(
                sortByDescription ?? "Optional sort column."));
        }

        if (!properties.TryGetValue("sort_direction", out _)) {
            properties.Add("sort_direction", ToolSchema.String("Optional sort direction.").Enum("asc", "desc"));
        }

        if (!properties.TryGetValue("top", out _)) {
            properties.Add("top", ToolSchema.Integer(
                topDescription ?? "Optional output top-N after sorting/projection (capped)."));
        }

        return schema;
    }

    /// <summary>
    /// Adds canonical write-governance metadata arguments to an object schema.
    /// </summary>
    /// <remarks>
    /// All fields are optional by schema because runtime policy decides whether they are required.
    /// </remarks>
    /// <param name="schema">Schema to mutate.</param>
    public static JsonObject WithWriteGovernanceMetadata(this JsonObject schema) {
        if (schema is null) {
            throw new ArgumentNullException(nameof(schema));
        }

        var properties = schema.GetObject("properties");
        if (properties is null) {
            return schema;
        }

        for (var i = 0; i < ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments.Count; i++) {
            string argumentName = ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments[i];
            AddStringPropertyIfMissing(
                properties,
                argumentName,
                GetWriteGovernanceDescription(argumentName));
        }

        return schema;
    }

    private static string GetWriteGovernanceDescription(string argumentName) {
        return argumentName switch {
            ToolWriteGovernanceArgumentNames.ExecutionId => "Write execution identifier for audit correlation.",
            ToolWriteGovernanceArgumentNames.ActorId => "Actor identifier responsible for the write intent.",
            ToolWriteGovernanceArgumentNames.ChangeReason => "Change reason, ticket, or approval reference.",
            ToolWriteGovernanceArgumentNames.RollbackPlanId => "Rollback plan identifier for safe revert operations.",
            ToolWriteGovernanceArgumentNames.RollbackProviderId => "Optional rollback provider identifier.",
            ToolWriteGovernanceArgumentNames.AuditCorrelationId => "Optional immutable audit correlation identifier.",
            _ => "Write governance metadata value."
        };
    }

    private static void AddStringPropertyIfMissing(JsonObject properties, string name, string description) {
        if (properties.TryGetValue(name, out _)) {
            return;
        }

        properties.Add(name, ToolSchema.String(description));
    }
}
