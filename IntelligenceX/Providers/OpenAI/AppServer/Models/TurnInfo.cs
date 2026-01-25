using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

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

    public string Id { get; }
    public string? Status { get; }
    public IReadOnlyList<TurnOutput> Outputs { get; }
    public IReadOnlyList<TurnOutput> ImageOutputs { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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
