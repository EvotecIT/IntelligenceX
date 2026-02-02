using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Describes what content should be reviewed.
/// </summary>
public sealed class ReviewTarget {
    private ReviewTarget(JsonObject payload) {
        Payload = payload;
    }

    internal JsonObject Payload { get; }

    /// <summary>
    /// Targets uncommitted working tree changes.
    /// </summary>
    public static ReviewTarget UncommittedChanges() {
        return new ReviewTarget(new JsonObject().Add("type", "uncommittedChanges"));
    }

    /// <summary>
    /// Targets a base branch.
    /// </summary>
    /// <param name="branch">Branch name.</param>
    public static ReviewTarget BaseBranch(string branch) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "baseBranch")
            .Add("branch", branch));
    }

    /// <summary>
    /// Targets a specific commit by SHA.
    /// </summary>
    /// <param name="sha">Commit SHA.</param>
    public static ReviewTarget Commit(string sha) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "commit")
            .Add("sha", sha));
    }

    /// <summary>
    /// Targets a custom text payload.
    /// </summary>
    /// <param name="text">Custom text to review.</param>
    public static ReviewTarget Custom(string text) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "custom")
            .Add("text", text));
    }
}
