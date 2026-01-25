using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ReviewStartResult {
    public ReviewStartResult(TurnInfo turn, string? reviewThreadId, JsonObject raw, JsonObject? additional) {
        Turn = turn;
        ReviewThreadId = reviewThreadId;
        Raw = raw;
        Additional = additional;
    }

    public TurnInfo Turn { get; }
    public string? ReviewThreadId { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ReviewStartResult FromJson(JsonObject obj) {
        var turnObj = obj.GetObject("turn");
        if (turnObj is null) {
            throw new InvalidOperationException("Unexpected review response.");
        }
        var reviewThreadId = obj.GetString("reviewThreadId") ?? obj.GetString("review_thread_id");
        var additional = obj.ExtractAdditional("turn", "reviewThreadId", "review_thread_id");
        return new ReviewStartResult(TurnInfo.FromJson(turnObj), reviewThreadId, obj, additional);
    }
}
