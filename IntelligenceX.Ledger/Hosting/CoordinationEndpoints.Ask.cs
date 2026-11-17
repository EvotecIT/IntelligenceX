using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static void MapAsk(WebApplication app, CoordinationStore store) {
        app.MapPost(Prefix + "/ask", async (CoordinationAsk ask, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.Ask(ask, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/questions", async (long? afterOrder, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.QuestionsToDeliver(Caller(http), Math.Max(0, afterOrder ?? 0)),
                DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/questions", async (string id, CoordinationQuestionCommand command,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.RaiseQuestion(id, command, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/questions/{number:int}/answer", async (string id, int number,
            CoordinationAnswerCommand command, HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.Answer(id, number, command, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/questions/{number:int}/delivery", async (string id, int number,
            CoordinationQuestionDeliveryCommand command, HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.RecordQuestionDelivery(id, number, command, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/usage", async (string id, CoordinationUsage usage, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.RecordUsage(id, usage, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/requests/{id}/usage", async (string id, HttpContext http, CancellationToken ct) =>
            await store.ExecuteAsync((_, state) => state.Work.TryGetValue(id, out var work) && UsageVisible(work, Caller(http))
                ? Results.Ok(new CoordinationUsageView(id, state.Usage.TryGetValue(id, out var items) ? items.ToArray() : []))
                : Results.NotFound(), DateTimeOffset.UtcNow, ct));
        app.MapGet(Prefix + "/usage", async (string date, HttpContext http, CancellationToken ct) =>
            DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)
                ? Results.Ok(await store.ExecuteAsync((engine, _) => engine.UsageReport(day, work => UsageVisible(work, Caller(http))),
                    DateTimeOffset.UtcNow, ct))
                : Results.BadRequest(new { error = "invalid-date" }));
    }

    private static bool UsageVisible(CoordinationWork work, CoordinationPrincipal caller) =>
        Visible(work, caller) || caller.Role == "host" && work.Request.ReturnTo?.MachineId == caller.MachineId;
}
