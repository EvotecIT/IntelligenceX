using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static void MapAsk(WebApplication app, CoordinationStore store, CoordinationArtifactStore artifacts, CoordinationImageStore images) {
        // Archived requests keep their ids: a retry gets the archived work back, a different ask with the id is rejected.
        app.MapPost(Prefix + "/ask", async (CoordinationAsk ask, HttpContext http, CancellationToken ct) => {
            CoordinationValidation.Require(ask?.Images is null || ask.Images.Length <= CoordinationImage.MaximumPerMessage &&
                ask.Images.All(CoordinationImage.ValidId), "invalid-ask-images");
            // Every image has to be here already, so the helper never starts with a picture it can't fetch.
            CoordinationValidation.Require(ask!.Images is null || ask.Images.All(images.Exists), "ask-image-unknown");
            return Results.Ok(await store.AskAsync(ask, Caller(http), DateTimeOffset.UtcNow, ct,
                ask.Continues is { } earlier ? await PreviousResultAsync(store, artifacts, earlier, Caller(http), ct) : null));
        });
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
                : null, DateTimeOffset.UtcNow, ct) ?? (await store.ArchivedAsync(id, ct) is { } entry && UsageVisible(entry.Work, Caller(http))
                    ? Results.Ok(new CoordinationUsageView(id, entry.Usage)) : Results.NotFound()));
        app.MapGet(Prefix + "/usage", async (string date, HttpContext http, CancellationToken ct) => {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)) return Results.BadRequest(new { error = "invalid-date" });
            // Usage happens before a request finishes, so archived requests that finished on or after the day cover it.
            var archived = await store.ArchivedSinceAsync(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), ct);
            return Results.Ok(await store.ExecuteAsync((engine, _) => engine.UsageReport(day, work => UsageVisible(work, Caller(http)), archived),
                DateTimeOffset.UtcNow, ct));
        });
    }

    /// <summary>The earlier ask's result as its requester may read it, for a follow-up whose helper can't carry on the same
    /// conversation. Null when there is none to read; the engine decides whether the follow-up is allowed.</summary>
    private static async Task<string?> PreviousResultAsync(CoordinationStore store, CoordinationArtifactStore artifacts, string id,
        CoordinationPrincipal caller, CancellationToken ct) {
        var live = await store.ExecuteAsync((_, state) => state.Work.TryGetValue(id, out var work) &&
            state.ResultOutbox.TryGetValue(id, out var result) ? (work, result) : ((CoordinationWork, CoordinationResultOutbox)?)null,
            DateTimeOffset.UtcNow, ct);
        if (live is null && await store.ArchivedAsync(id, ct) is { Result: { } archivedResult } entry) live = (entry.Work, archivedResult);
        if (live is not ({ ResultRef: not null } work, var outbox) || !ResultVisible(outbox, work, caller)) return null;
        return artifacts.ReadForAssignment(id, work.Generation, work.GrantId!, work.ResultRef)?.Content;
    }

    private static bool UsageVisible(CoordinationWork work, CoordinationPrincipal caller) =>
        Visible(work, caller) || caller.Role == "host" && work.Request.ReturnTo?.MachineId == caller.MachineId;
}
