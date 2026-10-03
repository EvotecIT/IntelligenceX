namespace IntelligenceX.Tests;

internal static partial class Program {
    // This subprocess only speaks fixture JSON-RPC on stdin/stdout. It never invokes a provider.
    private static int RunAppServerFixture(string scenario) {
        var turnNumber = 0;
        string? pendingTurn = null;
        string? line;
        while ((line = Console.ReadLine()) is not null) {
            var request = JsonLite.Parse(line).AsObject()!;
            var method = request.GetString("method");
            var id = request.GetInt64("id");
            if (!id.HasValue) continue;
            var result = new JsonObject();
            switch (method) {
                case "thread/start":
                    result.Add("thread", new JsonObject().Add("id", "thread-fixture"));
                    break;
                case "turn/start":
                    if (scenario == "exit-before-receipt") return 0;
                    if (pendingTurn is not null) {
                        Console.WriteLine(JsonLite.Serialize(new JsonObject().Add("id", id.Value)
                            .Add("error", new JsonObject().Add("code", -32000).Add("message", "A turn is already active."))));
                        continue;
                    }
                    var turnId = "turn-" + ++turnNumber;
                    var started = FixtureTurn(turnId, "inProgress");
                    if (scenario.StartsWith("review-", StringComparison.Ordinal) && scenario != "review-deadline") {
                        FixtureNotification("item/agentMessage/delta", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turnId", turnId).Add("itemId", "answer").Add("delta", "Partial review"));
                        if (scenario == "review-completed") {
                            FixtureNotification("item/completed", new JsonObject().Add("threadId", "thread-fixture")
                                .Add("turnId", turnId).Add("item", new JsonObject().Add("id", "progress")
                                    .Add("type", "agentMessage").Add("phase", "commentary").Add("text", "Checking files")));
                            FixtureNotification("item/completed", new JsonObject().Add("threadId", "thread-fixture")
                                .Add("turnId", turnId).Add("item", new JsonObject().Add("id", "answer")
                                    .Add("type", "agentMessage").Add("phase", "final_answer").Add("text", "Final review")));
                        }
                        FixtureNotification("turn/completed", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turn", FixtureTurn(turnId, scenario == "review-interrupted" ? "interrupted" : "completed")));
                    } else if (scenario == "early-completion" || turnNumber > 1) {
                        FixtureItem(turnId, "Final answer");
                        FixtureNotification("turn/completed", new JsonObject()
                            .Add("threadId", "thread-fixture").Add("turn", FixtureTurn(turnId, "completed")));
                    } else {
                        pendingTurn = turnId;
                    }
                    FixtureResponse(id.Value, new JsonObject().Add("turn", started));
                    FixtureNotification("fixture/ready", new JsonObject().Add("turnId", turnId));
                    continue;
                case "turn/interrupt":
                    if (request.GetObject("params")?.GetString("turnId") != pendingTurn) return 2;
                    var interrupted = pendingTurn!;
                    pendingTurn = null;
                    FixtureNotification("turn/completed", new JsonObject().Add("threadId", "thread-fixture")
                        .Add("turn", FixtureTurn(interrupted, "interrupted")));
                    if (scenario == "unacknowledged-interrupt") continue;
                    break;
                case "config/read":
                    if (scenario == "exit-after-receipt") return 0;
                    if (scenario == "login" || scenario == "login-failed") {
                        FixtureNotification("account/login/completed", new JsonObject().Add("loginId", "login-fixture")
                            .Add("success", scenario == "login").Add("error", "Access denied"));
                    }
                    if (pendingTurn is not null) {
                        var turn = pendingTurn;
                        pendingTurn = null;
                        FixtureNotification("turn/completed", new JsonObject().Add("threadId", "other-thread")
                            .Add("turn", FixtureTurn(turn, "failed")));
                        FixtureNotification("turn/completed", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turn", FixtureTurn("other-turn", "failed")));
                        FixtureNotification("item/reasoning/textDelta", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turnId", turn).Add("itemId", "reasoning").Add("delta", "private reasoning"));
                        FixtureNotification("item/agentMessage/delta", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turnId", turn).Add("itemId", "answer").Add("delta", "Final "));
                        FixtureNotification("item/agentMessage/delta", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turnId", turn).Add("itemId", "answer").Add("delta", "answer"));
                        FixtureItem(turn, "Final answer");
                        FixtureNotification("thread/tokenUsage/updated", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turnId", turn).Add("tokenUsage", new JsonObject()
                                .Add("last", new JsonObject().Add("inputTokens", 12).Add("outputTokens", 7)
                                    .Add("totalTokens", 19).Add("cachedInputTokens", 3).Add("reasoningOutputTokens", 2))
                                .Add("total", new JsonObject().Add("totalTokens", 99))));
                        if (scenario == "failed") {
                            FixtureNotification("error", new JsonObject().Add("threadId", "thread-fixture")
                                .Add("turnId", turn).Add("willRetry", true)
                                .Add("error", new JsonObject().Add("message", "retrying")));
                        }
                        var status = scenario == "failed" ? "failed" : scenario == "interrupted" ? "interrupted" : "completed";
                        var completed = FixtureTurn(turn, status);
                        if (status == "failed") completed.Add("error", new JsonObject().Add("message", "permission denied"));
                        FixtureNotification("turn/completed", new JsonObject().Add("threadId", "thread-fixture")
                            .Add("turn", completed));
                    }
                    break;
            }
            FixtureResponse(id.Value, result);
        }
        return 0;
    }

    private static JsonObject FixtureTurn(string id, string status) =>
        new JsonObject().Add("id", id).Add("status", status).Add("items", new JsonArray());

    private static void FixtureItem(string turn, string text) => FixtureNotification("item/completed",
        new JsonObject().Add("threadId", "thread-fixture").Add("turnId", turn)
            .Add("item", new JsonObject().Add("id", "answer").Add("type", "agentMessage").Add("text", text)));

    private static void FixtureNotification(string method, JsonObject parameters) =>
        Console.WriteLine(JsonLite.Serialize(new JsonObject().Add("method", method).Add("params", parameters)));

    private static void FixtureResponse(long id, JsonObject result) =>
        Console.WriteLine(JsonLite.Serialize(new JsonObject().Add("id", id).Add("result", result)));
}
