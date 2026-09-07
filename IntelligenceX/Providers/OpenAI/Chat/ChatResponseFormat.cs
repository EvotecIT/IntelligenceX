using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>Explicit structured-output request for transports supporting JSON Schema generation.</summary>
public sealed class ChatResponseFormat {
    /// <summary>Creates a schema request. Provider schema support must be qualified separately.</summary>
    public ChatResponseFormat(string name, string jsonSchema, bool strict = true) {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64) throw new ArgumentException("A schema name of 1-64 characters is required.", nameof(name));
        foreach (char character in name) {
            if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                && !(character >= '0' && character <= '9') && character != '_' && character != '-')
                throw new ArgumentException("Schema names may contain only ASCII letters, digits, underscores and hyphens.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(jsonSchema) || jsonSchema.Length > 128_000)
            throw new ArgumentException("A bounded JSON schema is required.", nameof(jsonSchema));
        if (JsonLite.Parse(jsonSchema)?.AsObject() is null) throw new ArgumentException("The schema must be a JSON object.", nameof(jsonSchema));
        Name = name; JsonSchema = jsonSchema; Strict = strict;
    }

    /// <summary>Provider-safe schema identifier.</summary>
    public string Name { get; }
    /// <summary>Immutable schema JSON. Local result validation remains the caller's responsibility.</summary>
    public string JsonSchema { get; }
    /// <summary>Whether provider-enforced strict generation is requested.</summary>
    public bool Strict { get; }

    internal JsonObject ToJsonSchema() => new JsonObject().Add("name", Name)
        .Add("strict", Strict).Add("schema", JsonLite.Parse(JsonSchema)!.AsObject()!);
}
