using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

/// <summary>
/// Describes a schema validation issue for a reviewer configuration value.
/// </summary>
internal sealed record ReviewConfigValidationIssue(string Path, string Message);

/// <summary>
/// Represents the result of validating the current reviewer configuration file.
/// </summary>
internal sealed class ReviewConfigValidationResult {
    public ReviewConfigValidationResult(string configPath, string schemaHint,
        IReadOnlyList<ReviewConfigValidationIssue> errors,
        IReadOnlyList<ReviewConfigValidationIssue> warnings) {
        ConfigPath = configPath;
        SchemaHint = schemaHint;
        Errors = errors;
        Warnings = warnings;
    }

    public string ConfigPath { get; }
    public string SchemaHint { get; }
    public IReadOnlyList<ReviewConfigValidationIssue> Errors { get; }
    public IReadOnlyList<ReviewConfigValidationIssue> Warnings { get; }
    public bool HasErrors => Errors.Count > 0;
}

internal static class ReviewConfigValidator {
    private const double IntegralEpsilon = 0.000001d;
    private const string EmbeddedSchemaName = "IntelligenceX.Reviewer.Schemas.reviewer.schema.json";
    private static readonly HashSet<string> RootSections = new(StringComparer.Ordinal) {
        "review",
        "copilot",
        "cleanup",
        "codex",
        "appServer"
    };

    /// <summary>
    /// Validates the currently resolved reviewer configuration file, if present.
    /// </summary>
    /// <returns>The validation result, or <c>null</c> if no configuration file is found.</returns>
    public static ReviewConfigValidationResult? ValidateCurrent() {
        var configPath = ReviewConfigLoader.ResolveConfigPath();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) {
            return null;
        }

        if (!TryLoadSchema(out var schemaRoot, out var schemaHint, out var schemaWarning)) {
            var schemaWarnings = new List<ReviewConfigValidationIssue>();
            if (!string.IsNullOrWhiteSpace(schemaWarning)) {
                schemaWarnings.Add(new ReviewConfigValidationIssue("$schema", schemaWarning));
            }
            return new ReviewConfigValidationResult(configPath, schemaHint, Array.Empty<ReviewConfigValidationIssue>(), schemaWarnings);
        }

        JsonValue? configValue;
        try {
            configValue = JsonLite.Parse(File.ReadAllText(configPath));
        } catch (Exception ex) {
            return new ReviewConfigValidationResult(configPath, schemaHint,
                new[] { new ReviewConfigValidationIssue("$", $"Invalid JSON: {ex.Message}") },
                Array.Empty<ReviewConfigValidationIssue>());
        }

        var rootObj = configValue?.AsObject();
        if (rootObj is null) {
            return new ReviewConfigValidationResult(configPath, schemaHint,
                new[] { new ReviewConfigValidationIssue("$", "Configuration root must be a JSON object.") },
                Array.Empty<ReviewConfigValidationIssue>());
        }

        var errors = new List<ReviewConfigValidationIssue>();
        var warnings = new List<ReviewConfigValidationIssue>();

        schemaRoot.Properties.TryGetValue("review", out var reviewSchema);
        schemaRoot.Properties.TryGetValue("copilot", out var copilotSchema);
        schemaRoot.Properties.TryGetValue("cleanup", out var cleanupSchema);
        schemaRoot.Properties.TryGetValue("codex", out var codexSchema);

        if (rootObj.TryGetValue("review", out var reviewValue) && reviewValue?.AsObject() is not null) {
            if (reviewSchema is not null) {
                ValidateNode(reviewSchema, reviewValue, "$.review", errors, warnings);
            }
        } else if (reviewSchema is not null) {
            var reviewLike = new JsonObject();
            foreach (var entry in rootObj) {
                if (RootSections.Contains(entry.Key)) {
                    continue;
                }
                reviewLike.Add(entry.Key, entry.Value);
            }
            ValidateNode(reviewSchema, JsonValue.From(reviewLike), "$", errors, warnings);
        }

        if (copilotSchema is not null && rootObj.TryGetValue("copilot", out var copilotValue)) {
            ValidateNode(copilotSchema, copilotValue, "$.copilot", errors, warnings);
        }
        if (cleanupSchema is not null && rootObj.TryGetValue("cleanup", out var cleanupValue)) {
            ValidateNode(cleanupSchema, cleanupValue, "$.cleanup", errors, warnings);
        }
        if (codexSchema is not null) {
            if (rootObj.TryGetValue("codex", out var codexValue)) {
                ValidateNode(codexSchema, codexValue, "$.codex", errors, warnings);
            }
            if (rootObj.TryGetValue("appServer", out var appServerValue)) {
                ValidateNode(codexSchema, appServerValue, "$.appServer", errors, warnings);
                warnings.Add(new ReviewConfigValidationIssue("$.appServer", "appServer is deprecated; use codex instead."));
            }
        }

        if (reviewSchema is not null && rootObj.TryGetValue("review", out var reviewObject) && reviewObject?.AsObject() is not null) {
            foreach (var entry in rootObj) {
                if (RootSections.Contains(entry.Key)) {
                    continue;
                }
                if (reviewSchema.Properties.ContainsKey(entry.Key)) {
                    warnings.Add(new ReviewConfigValidationIssue($"$.{entry.Key}", "Ignored because review object is present."));
                } else {
                    warnings.Add(new ReviewConfigValidationIssue($"$.{entry.Key}", "Unknown root property."));
                }
            }
        } else {
            foreach (var entry in rootObj) {
                if (RootSections.Contains(entry.Key)) {
                    continue;
                }
                if (reviewSchema is not null && !reviewSchema.Properties.ContainsKey(entry.Key)) {
                    warnings.Add(new ReviewConfigValidationIssue($"$.{entry.Key}", "Unknown review setting."));
                }
            }
        }

        return new ReviewConfigValidationResult(configPath, schemaHint, errors, warnings);
    }

    private static bool TryLoadSchema(out SchemaNode schema, out string schemaHint, out string? warning) {
        schema = new SchemaNode();
        warning = null;
        schemaHint = "Schemas/reviewer.schema.json";

        var schemaText = TryLoadSchemaText(out var hint, out var searchSummary);
        if (string.IsNullOrWhiteSpace(schemaText)) {
            warning = $"Schema not found; skipping validation. Tried: {searchSummary}";
            schemaHint = hint ?? schemaHint;
            return false;
        }

        JsonValue? schemaValue;
        try {
            schemaValue = JsonLite.Parse(schemaText);
        } catch (Exception ex) {
            warning = $"Schema parse failed ({hint ?? schemaHint}): {ex.Message}";
            schemaHint = hint ?? schemaHint;
            return false;
        }

        var schemaObj = schemaValue?.AsObject();
        if (schemaObj is null) {
            warning = $"Schema root must be an object ({hint ?? schemaHint}).";
            schemaHint = hint ?? schemaHint;
            return false;
        }

        schema = ParseSchema(schemaObj);
        schemaHint = hint ?? schemaHint;
        return true;
    }

    private static string? TryLoadSchemaText(out string? hint, out string searchSummary) {
        hint = null;
        var cwd = Environment.CurrentDirectory;
        var cwdSchema = Path.Combine(cwd, "Schemas", "reviewer.schema.json");
        var baseDirSchema = Path.Combine(AppContext.BaseDirectory, "Schemas", "reviewer.schema.json");
        searchSummary = $"{cwdSchema}; {baseDirSchema}; embedded:{EmbeddedSchemaName}";
        if (File.Exists(cwdSchema)) {
            hint = Path.GetRelativePath(cwd, cwdSchema);
            return File.ReadAllText(cwdSchema);
        }

        if (File.Exists(baseDirSchema)) {
            hint = baseDirSchema;
            return File.ReadAllText(baseDirSchema);
        }

        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedSchemaName);
        if (stream is null) {
            return null;
        }

        using var reader = new StreamReader(stream);
        hint = "embedded schema";
        return reader.ReadToEnd();
    }

    private static SchemaNode ParseSchema(JsonObject obj) {
        var node = new SchemaNode {
            Type = obj.GetString("type"),
            Minimum = obj.GetDouble("minimum"),
            Maximum = obj.GetDouble("maximum")
        };

        var enumValues = obj.GetArray("enum");
        if (enumValues is not null && enumValues.Count > 0) {
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in enumValues) {
                var text = item.AsString();
                if (!string.IsNullOrWhiteSpace(text)) {
                    values.Add(text!);
                }
            }
            if (values.Count > 0) {
                node.Enum = values;
            }
        }

        var properties = obj.GetObject("properties");
        if (properties is not null) {
            foreach (var entry in properties) {
                var childObj = entry.Value?.AsObject();
                if (childObj is null) {
                    continue;
                }
                node.Properties[entry.Key] = ParseSchema(childObj);
            }
        }

        if (obj.TryGetValue("items", out var itemsValue)) {
            var itemsObj = itemsValue?.AsObject();
            if (itemsObj is not null) {
                node.Items = ParseSchema(itemsObj);
            }
        }

        if (obj.TryGetValue("additionalProperties", out var additionalValue)) {
            var additionalObj = additionalValue?.AsObject();
            if (additionalObj is not null) {
                node.AdditionalProperties = ParseSchema(additionalObj);
            } else if (additionalValue is not null && additionalValue.Kind == JsonValueKind.Boolean) {
                if (additionalValue.AsBoolean()) {
                    node.AdditionalProperties = new SchemaNode();
                }
            }
        }

        return node;
    }

    private static void ValidateNode(SchemaNode schema, JsonValue? value, string path,
        List<ReviewConfigValidationIssue> errors, List<ReviewConfigValidationIssue> warnings) {
        if (value is null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(schema.Type) && !IsTypeMatch(schema.Type!, value)) {
            errors.Add(new ReviewConfigValidationIssue(path, $"Expected {schema.Type} but found {value.Kind}."));
            return;
        }

        if (schema.Enum is not null) {
            var text = value.AsString();
            if (string.IsNullOrWhiteSpace(text) || !schema.Enum.Contains(text!)) {
                var allowed = string.Join(", ", schema.Enum.OrderBy(v => v));
                errors.Add(new ReviewConfigValidationIssue(path, $"Value '{text}' is not allowed. Expected one of: {allowed}."));
                return;
            }
        }

        if (schema.Minimum.HasValue || schema.Maximum.HasValue) {
            var numeric = value.AsDouble();
            if (!numeric.HasValue) {
                errors.Add(new ReviewConfigValidationIssue(path, "Expected a numeric value."));
            } else {
                if (schema.Minimum.HasValue && numeric.Value < schema.Minimum.Value) {
                    errors.Add(new ReviewConfigValidationIssue(path, $"Value {numeric.Value} is below minimum {schema.Minimum.Value}."));
                }
                if (schema.Maximum.HasValue && numeric.Value > schema.Maximum.Value) {
                    errors.Add(new ReviewConfigValidationIssue(path, $"Value {numeric.Value} exceeds maximum {schema.Maximum.Value}."));
                }
            }
        }

        if (string.Equals(schema.Type, "object", StringComparison.OrdinalIgnoreCase)) {
            var obj = value.AsObject();
            if (obj is null) {
                return;
            }
            foreach (var entry in obj) {
                if (schema.Properties.TryGetValue(entry.Key, out var propertySchema)) {
                    ValidateNode(propertySchema, entry.Value, $"{path}.{entry.Key}", errors, warnings);
                    continue;
                }

                if (schema.AdditionalProperties is not null) {
                    ValidateNode(schema.AdditionalProperties, entry.Value, $"{path}.{entry.Key}", errors, warnings);
                }

                if (schema.Properties.Count > 0 && schema.AdditionalProperties is null) {
                    warnings.Add(new ReviewConfigValidationIssue($"{path}.{entry.Key}", "Unknown property."));
                }
            }
        }

        if (string.Equals(schema.Type, "array", StringComparison.OrdinalIgnoreCase)) {
            var array = value.AsArray();
            if (array is null || schema.Items is null) {
                return;
            }
            var index = 0;
            foreach (var item in array) {
                ValidateNode(schema.Items, item, $"{path}[{index}]", errors, warnings);
                index++;
            }
        }
    }

    private static bool IsTypeMatch(string type, JsonValue value) {
        return type switch {
            "string" => value.Kind == JsonValueKind.String,
            "boolean" => value.Kind == JsonValueKind.Boolean,
            "number" => value.Kind == JsonValueKind.Number,
            "integer" => value.Kind == JsonValueKind.Number && IsIntegral(value.AsDouble()),
            "object" => value.Kind == JsonValueKind.Object,
            "array" => value.Kind == JsonValueKind.Array,
            _ => true
        };
    }

    private static bool IsIntegral(double? value) {
        if (!value.HasValue) {
            return false;
        }
        var rounded = Math.Round(value.Value);
        // Allow for minor floating-point drift when checking integer compatibility.
        return Math.Abs(value.Value - rounded) < IntegralEpsilon;
    }

    private sealed class SchemaNode {
        public string? Type { get; set; }
        public HashSet<string>? Enum { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public SchemaNode? Items { get; set; }
        public SchemaNode? AdditionalProperties { get; set; }
        public Dictionary<string, SchemaNode> Properties { get; } = new(StringComparer.Ordinal);
    }
}
