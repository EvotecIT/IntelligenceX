using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Treatment;

/// <summary>
/// Describes an AI treatment job over supplied artifacts.
/// </summary>
public sealed class TreatmentRequest {
    /// <summary>
    /// Optional caller supplied request id.
    /// </summary>
    public string? Id { get; set; }
    /// <summary>
    /// Optional human readable treatment name.
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// System or persona instructions for the treatment run.
    /// </summary>
    public string? Instructions { get; set; }
    /// <summary>
    /// User-facing brief or task prompt.
    /// </summary>
    public string? Prompt { get; set; }
    /// <summary>
    /// Optional model override.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// Optional reasoning effort hint.
    /// </summary>
    public ReasoningEffort? ReasoningEffort { get; set; }
    /// <summary>
    /// Optional text verbosity hint.
    /// </summary>
    public TextVerbosity? TextVerbosity { get; set; }
    /// <summary>
    /// Optional sampling temperature.
    /// </summary>
    public double? Temperature { get; set; }
    /// <summary>
    /// Working directory for provider-side file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Workspace path for provider-side file access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Whether the treatment provider may use network access.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Whether to force a fresh provider conversation for this treatment run.
    /// </summary>
    public bool NewThread { get; set; } = true;
    /// <summary>
    /// Input artifacts available to the model.
    /// </summary>
    public IReadOnlyList<TreatmentInputArtifact> Inputs { get; set; } = Array.Empty<TreatmentInputArtifact>();
    /// <summary>
    /// Expected outputs for the model to produce.
    /// </summary>
    public IReadOnlyList<TreatmentOutputSpec> Outputs { get; set; } = Array.Empty<TreatmentOutputSpec>();
    /// <summary>
    /// Optional structured output contract.
    /// </summary>
    public TreatmentOutputSchema? OutputSchema { get; set; }
    /// <summary>
    /// Optional image generation settings.
    /// </summary>
    public TreatmentImageOptions? ImageGeneration { get; set; }
    /// <summary>
    /// Caller metadata that should travel with the run.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = EmptyDictionary;
    /// <summary>
    /// Whether local text-like input files should be inlined into provider prompts.
    /// </summary>
    public bool InlineLocalInputFiles { get; set; } = true;
    /// <summary>
    /// Maximum characters to inline per local input file.
    /// </summary>
    public int? MaxInlineFileCharacters { get; set; } = 120000;

    internal static readonly IReadOnlyDictionary<string, string> EmptyDictionary = new Dictionary<string, string>(0);
}

/// <summary>
/// Describes a single artifact available to a treatment run.
/// </summary>
public sealed class TreatmentInputArtifact {
    /// <summary>
    /// Stable artifact id.
    /// </summary>
    public string? Id { get; set; }
    /// <summary>
    /// Caller-defined role, such as source, evidence, brief, image, or context.
    /// </summary>
    public string? Role { get; set; }
    /// <summary>
    /// Human readable name.
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// Media type, if known.
    /// </summary>
    public string? MediaType { get; set; }
    /// <summary>
    /// Inline text content.
    /// </summary>
    public string? Text { get; set; }
    /// <summary>
    /// Inline JSON content.
    /// </summary>
    public JsonObject? Json { get; set; }
    /// <summary>
    /// Local artifact path.
    /// </summary>
    public string? Path { get; set; }
    /// <summary>
    /// External artifact URI.
    /// </summary>
    public Uri? Uri { get; set; }
    /// <summary>
    /// Whether this artifact is private source material rather than publishable output.
    /// </summary>
    public bool IsPrivate { get; set; } = true;
    /// <summary>
    /// Optional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = TreatmentRequest.EmptyDictionary;
}

/// <summary>
/// Describes an expected treatment output.
/// </summary>
public sealed class TreatmentOutputSpec {
    /// <summary>
    /// Stable output id.
    /// </summary>
    public string? Id { get; set; }
    /// <summary>
    /// Output modality.
    /// </summary>
    public TreatmentOutputModality Modality { get; set; } = TreatmentOutputModality.Text;
    /// <summary>
    /// Human-readable output description.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Target media type.
    /// </summary>
    public string? MediaType { get; set; }
    /// <summary>
    /// Preferred output path.
    /// </summary>
    public string? Path { get; set; }
    /// <summary>
    /// Whether this output is required.
    /// </summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// Output modalities supported by the generic treatment contract.
/// </summary>
public enum TreatmentOutputModality {
    /// <summary>
    /// Plain text output.
    /// </summary>
    Text,
    /// <summary>
    /// Markdown output.
    /// </summary>
    Markdown,
    /// <summary>
    /// JSON output.
    /// </summary>
    Json,
    /// <summary>
    /// Image or other visual output.
    /// </summary>
    Image,
    /// <summary>
    /// Manifest describing generated or selected assets.
    /// </summary>
    AssetManifest
}

/// <summary>
/// Describes a structured output contract expected from the provider.
/// </summary>
public sealed class TreatmentOutputSchema {
    /// <summary>
    /// Contract name or version.
    /// </summary>
    public string? Contract { get; set; }
    /// <summary>
    /// JSON schema for the expected output.
    /// </summary>
    public JsonObject? JsonSchema { get; set; }
    /// <summary>
    /// Example JSON for providers that do not support strict schema mode.
    /// </summary>
    public JsonObject? ExampleJson { get; set; }
    /// <summary>
    /// Whether the provider should treat the schema as strict when possible.
    /// </summary>
    public bool Strict { get; set; }
}

/// <summary>
/// Generic image generation options for treatment providers.
/// </summary>
public sealed class TreatmentImageOptions {
    /// <summary>
    /// Enables provider-side image generation.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Directory where generated image files should be written when supported.
    /// </summary>
    public string? OutputDirectory { get; set; }
    /// <summary>
    /// Provider image quality hint.
    /// </summary>
    public string? Quality { get; set; }
    /// <summary>
    /// Provider image size hint.
    /// </summary>
    public string? Size { get; set; }
    /// <summary>
    /// Desired image output format.
    /// </summary>
    public string? OutputFormat { get; set; }
    /// <summary>
    /// Desired background handling.
    /// </summary>
    public string? Background { get; set; }
    /// <summary>
    /// Output compression for lossy formats.
    /// </summary>
    public int? OutputCompression { get; set; }
    /// <summary>
    /// Number of partial images requested from streaming-capable providers.
    /// </summary>
    public int? PartialImages { get; set; }
}
