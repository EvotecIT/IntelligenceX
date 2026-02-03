using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a single chat turn and its outputs.
/// </summary>
public sealed class TurnInfo {
    /// <summary>
    /// Initializes a new turn info model.
    /// </summary>
    public TurnInfo(string id, string? responseId, string? status, IReadOnlyList<TurnOutput>? outputs, IReadOnlyList<TurnOutput>? imageOutputs,
        JsonObject raw, JsonObject? additional) {
        Id = id;
        ResponseId = responseId;
        Status = status;
        Outputs = outputs ?? Array.Empty<TurnOutput>();
        ImageOutputs = imageOutputs ?? Array.Empty<TurnOutput>();
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Initializes a new turn info model.
    /// </summary>
    public TurnInfo(string id, string? status, IReadOnlyList<TurnOutput>? outputs, IReadOnlyList<TurnOutput>? imageOutputs,
        JsonObject raw, JsonObject? additional)
        : this(id, null, status, outputs, imageOutputs, raw, additional) { }

    /// <summary>
    /// Gets the turn id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Gets the response id when available.
    /// </summary>
    public string? ResponseId { get; }
    /// <summary>
    /// Gets the turn status.
    /// </summary>
    public string? Status { get; }
    /// <summary>
    /// Gets all outputs.
    /// </summary>
    public IReadOnlyList<TurnOutput> Outputs { get; }
    /// <summary>
    /// Gets image outputs.
    /// </summary>
    public IReadOnlyList<TurnOutput> ImageOutputs { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a turn info model from JSON.
    /// </summary>
    /// <param name="turnObj">Source JSON object.</param>
    /// <returns>The parsed turn info.</returns>
    public static TurnInfo FromJson(JsonObject turnObj) {
        var id = turnObj.GetString("id") ?? string.Empty;
        var status = turnObj.GetString("status");
        var responseId = turnObj.GetString("responseId")
                         ?? turnObj.GetObject("response")?.GetString("id");
        var outputs = TurnOutput.FromTurn(turnObj);
        var images = TurnOutput.FilterImages(outputs);
        var additional = turnObj.ExtractAdditional(
            "id", "status", "responseId", "output", "outputs", "response", "result");
        return new TurnInfo(id, responseId, status, outputs, images, turnObj, additional);
    }
}
