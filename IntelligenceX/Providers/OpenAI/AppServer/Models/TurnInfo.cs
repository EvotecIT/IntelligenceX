using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class TurnInfo {
    public TurnInfo(string id, string? status, IReadOnlyList<TurnOutput>? outputs = null, IReadOnlyList<TurnOutput>? imageOutputs = null) {
        Id = id;
        Status = status;
        Outputs = outputs ?? Array.Empty<TurnOutput>();
        ImageOutputs = imageOutputs ?? Array.Empty<TurnOutput>();
    }

    public string Id { get; }
    public string? Status { get; }
    public IReadOnlyList<TurnOutput> Outputs { get; }
    public IReadOnlyList<TurnOutput> ImageOutputs { get; }

    public static TurnInfo FromJson(JsonObject turnObj) {
        var id = turnObj.GetString("id") ?? string.Empty;
        var status = turnObj.GetString("status");
        var outputs = TurnOutput.FromTurn(turnObj);
        var images = TurnOutput.FilterImages(outputs);
        return new TurnInfo(id, status, outputs, images);
    }
}
