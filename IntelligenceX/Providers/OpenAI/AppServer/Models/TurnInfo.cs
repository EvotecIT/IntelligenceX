using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a single turn response from the app-server.
/// </summary>
/// <example>
/// <code>
/// var turn = await thread.SendAsync("Summarize the diff.");
/// foreach (var output in turn.Outputs) {
///     if (output.IsText) {
///         Console.WriteLine(output.Text);
///     }
/// }
/// </code>
/// </example>
public sealed class TurnInfo {
    public TurnInfo(string id, string? status, IReadOnlyList<TurnOutput>? outputs, IReadOnlyList<TurnOutput>? imageOutputs,
        JsonObject raw, JsonObject? additional) {
        Id = id;
        Status = status;
        Outputs = outputs ?? Array.Empty<TurnOutput>();
        ImageOutputs = imageOutputs ?? Array.Empty<TurnOutput>();
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Turn identifier.</summary>
    public string Id { get; }
    /// <summary>Turn status (if available).</summary>
    public string? Status { get; }
    /// <summary>All outputs returned for the turn.</summary>
    public IReadOnlyList<TurnOutput> Outputs { get; }
    /// <summary>Image outputs returned for the turn.</summary>
    public IReadOnlyList<TurnOutput> ImageOutputs { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a turn from JSON.</summary>
    public static TurnInfo FromJson(JsonObject turnObj) {
        var id = turnObj.GetString("id") ?? string.Empty;
        var status = turnObj.GetString("status");
        var outputs = TurnOutput.FromTurn(turnObj);
        var images = TurnOutput.FilterImages(outputs);
        var additional = turnObj.ExtractAdditional(
            "id", "status", "output", "outputs", "response", "result");
        return new TurnInfo(id, status, outputs, images, turnObj, additional);
    }
}
