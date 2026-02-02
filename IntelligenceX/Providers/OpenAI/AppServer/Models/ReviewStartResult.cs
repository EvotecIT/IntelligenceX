using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the response after starting a review.
/// </summary>
public sealed class ReviewStartResult {
    /// <summary>
    /// Initializes a new review start result.
    /// </summary>
    public ReviewStartResult(TurnInfo turn, string? reviewThreadId, JsonObject raw, JsonObject? additional) {
        Turn = turn;
        ReviewThreadId = reviewThreadId;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the review turn info.
    /// </summary>
    public TurnInfo Turn { get; }
    /// <summary>
    /// Gets the review thread id when available.
    /// </summary>
    public string? ReviewThreadId { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a review start result from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed review start result.</returns>
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
