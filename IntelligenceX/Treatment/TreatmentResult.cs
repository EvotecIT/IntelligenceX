using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Treatment;

/// <summary>
/// Result returned by a treatment provider.
/// </summary>
public sealed class TreatmentResult {
    /// <summary>
    /// Initializes a new treatment result.
    /// </summary>
    public TreatmentResult(string id, string? status, string? text, JsonObject? json, IReadOnlyList<TreatmentAsset>? assets,
        object? raw, IReadOnlyDictionary<string, string>? metadata = null) {
        Id = id;
        Status = status;
        Text = text;
        Json = json;
        Assets = assets ?? Array.Empty<TreatmentAsset>();
        Raw = raw;
        Metadata = metadata ?? TreatmentRequest.EmptyDictionary;
    }

    /// <summary>
    /// Treatment result id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Provider status.
    /// </summary>
    public string? Status { get; }
    /// <summary>
    /// Aggregated text output.
    /// </summary>
    public string? Text { get; }
    /// <summary>
    /// Parsed JSON output when available.
    /// </summary>
    public JsonObject? Json { get; }
    /// <summary>
    /// Assets generated or selected during treatment.
    /// </summary>
    public IReadOnlyList<TreatmentAsset> Assets { get; }
    /// <summary>
    /// Raw provider result.
    /// </summary>
    public object? Raw { get; }
    /// <summary>
    /// Metadata attached by the caller or provider.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Describes an output asset produced by a treatment run.
/// </summary>
public sealed class TreatmentAsset {
    /// <summary>
    /// Initializes a new treatment asset.
    /// </summary>
    public TreatmentAsset(string? id, TreatmentOutputModality modality, string? purpose, string? path, Uri? uri, string? mediaType,
        string? altText, string? caption, string? credit, string? model, string? prompt, string? revisedPrompt,
        IReadOnlyList<string>? sourceInputIds, object? raw = null) {
        Id = id;
        Modality = modality;
        Purpose = purpose;
        Path = path;
        Uri = uri;
        MediaType = mediaType;
        AltText = altText;
        Caption = caption;
        Credit = credit;
        Model = model;
        Prompt = prompt;
        RevisedPrompt = revisedPrompt;
        SourceInputIds = sourceInputIds ?? Array.Empty<string>();
        Raw = raw;
    }

    /// <summary>
    /// Asset id.
    /// </summary>
    public string? Id { get; }
    /// <summary>
    /// Asset modality.
    /// </summary>
    public TreatmentOutputModality Modality { get; }
    /// <summary>
    /// Asset purpose.
    /// </summary>
    public string? Purpose { get; }
    /// <summary>
    /// Local path to the asset.
    /// </summary>
    public string? Path { get; }
    /// <summary>
    /// External URI to the asset.
    /// </summary>
    public Uri? Uri { get; }
    /// <summary>
    /// Asset media type.
    /// </summary>
    public string? MediaType { get; }
    /// <summary>
    /// Suggested alt text.
    /// </summary>
    public string? AltText { get; }
    /// <summary>
    /// Suggested caption.
    /// </summary>
    public string? Caption { get; }
    /// <summary>
    /// Suggested credit or attribution.
    /// </summary>
    public string? Credit { get; }
    /// <summary>
    /// Provider model that produced the asset.
    /// </summary>
    public string? Model { get; }
    /// <summary>
    /// Prompt used to produce the asset.
    /// </summary>
    public string? Prompt { get; }
    /// <summary>
    /// Provider revised prompt when available.
    /// </summary>
    public string? RevisedPrompt { get; }
    /// <summary>
    /// Source input ids that influenced the asset.
    /// </summary>
    public IReadOnlyList<string> SourceInputIds { get; }
    /// <summary>
    /// Raw provider output for this asset.
    /// </summary>
    public object? Raw { get; }
}
