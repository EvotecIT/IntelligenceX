using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Describes the target for a review request.
/// </summary>
/// <example>
/// <code>
/// var target = ReviewTarget.BaseBranch("main");
/// var result = await client.StartReviewAsync(target);
/// </code>
/// </example>
public sealed class ReviewTarget {
    private ReviewTarget(JsonObject payload) {
        Payload = payload;
    }

    internal JsonObject Payload { get; }

    /// <summary>Reviews uncommitted changes.</summary>
    public static ReviewTarget UncommittedChanges() {
        return new ReviewTarget(new JsonObject().Add("type", "uncommittedChanges"));
    }

    /// <summary>Reviews changes against a base branch.</summary>
    public static ReviewTarget BaseBranch(string branch) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "baseBranch")
            .Add("branch", branch));
    }

    /// <summary>Reviews a specific commit.</summary>
    public static ReviewTarget Commit(string sha) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "commit")
            .Add("sha", sha));
    }

    /// <summary>Uses a custom review target text.</summary>
    public static ReviewTarget Custom(string text) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "custom")
            .Add("text", text));
    }
}
