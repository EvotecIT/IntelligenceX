using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ReviewTarget {
    private ReviewTarget(JsonObject payload) {
        Payload = payload;
    }

    internal JsonObject Payload { get; }

    public static ReviewTarget UncommittedChanges() {
        return new ReviewTarget(new JsonObject().Add("type", "uncommittedChanges"));
    }

    public static ReviewTarget BaseBranch(string branch) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "baseBranch")
            .Add("branch", branch));
    }

    public static ReviewTarget Commit(string sha) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "commit")
            .Add("sha", sha));
    }

    public static ReviewTarget Custom(string text) {
        return new ReviewTarget(new JsonObject()
            .Add("type", "custom")
            .Add("text", text));
    }
}
