using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the response from starting a review.
/// </summary>
/// <example>
/// <code>
/// var result = await client.StartReviewAsync(ReviewTarget.BaseBranch("main"));
/// Console.WriteLine(result.ReviewThreadId);
/// </code>
/// </example>
public sealed class ReviewStartResult {
    public ReviewStartResult(TurnInfo turn, string? reviewThreadId, JsonObject raw, JsonObject? additional) {
        Turn = turn;
        ReviewThreadId = reviewThreadId;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>The initial review turn.</summary>
    public TurnInfo Turn { get; }
    /// <summary>Review thread id for inline comments (if provided).</summary>
    public string? ReviewThreadId { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a review response from JSON.</summary>
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
