using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static bool ResultVisible(CoordinationResultOutbox result, CoordinationWork work, CoordinationPrincipal caller) =>
        Visible(work, caller) || caller.Role == "host" && result.ReturnTo.MachineId == caller.MachineId;

    private static void MapLifecycle(WebApplication app, CoordinationStore store) {
        app.MapPost(Prefix + "/requests/{id}/cancel", async (string id, CoordinationCancellation command,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.Cancel(id, command, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/binding", async (string id, CoordinationSessionBinding binding,
            HttpContext http, CancellationToken ct) => {
            CoordinationValidation.Require(id == binding.RequestId, "binding-request-conflict");
            return Results.Ok(await store.ExecuteAsync((engine, _) => engine.BindSession(binding, Caller(http)), DateTimeOffset.UtcNow, ct));
        });
        app.MapGet(Prefix + "/results", async (long? afterOrder, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((_, state) => {
                var page = state.ResultOutbox.Values.Where(result =>
                state.Work.TryGetValue(result.RequestId, out var work) && ResultVisible(result, work, Caller(http)) &&
                work.Order > Math.Max(0, afterOrder ?? 0) && result.ConsumedAtUtc is null)
                    .OrderBy(result => state.Work[result.RequestId].Order).Take(17).ToArray();
                return new CoordinationResultPage(page.Take(16).ToArray(), page.Length > 16 ? state.Work[page[15].RequestId].Order : null);
            }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/requests/{id}/result", async (string id, HttpContext http, CancellationToken ct) =>
            await store.ExecuteAsync((_, state) => state.ResultOutbox.TryGetValue(id, out var result) &&
                state.Work.TryGetValue(id, out var work) && ResultVisible(result, work, Caller(http))
                ? Results.Ok(result) : Results.NotFound(), DateTimeOffset.UtcNow, ct));
        app.MapPost(Prefix + "/requests/{id}/result/begin", async (string id, CoordinationDeliveryIntent command,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.BeginResultDelivery(id, command.Generation, command.AttemptId, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/result/receipts", async (string id, CoordinationDeliveryCommand command,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.RecordResultDelivery(id, command.Generation, command.Receipt, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/result/consume", async (string id, CoordinationConsumption command,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) =>
                engine.ConsumeResult(id, command.Generation, command.ResultRef, command.Candidate, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/retirements", async (long? afterOrder, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, state) => {
                var page = state.Retirements.Values.Where(job => job.CompletedAtUtc is null &&
                    state.Work.TryGetValue(job.Binding.RequestId, out var work) && work.Order > Math.Max(0, afterOrder ?? 0) && Visible(work, Caller(http)))
                    .OrderBy(job => state.Work[job.Binding.RequestId].Order).Take(17).ToArray();
                var items = new List<CoordinationRetirement>();
                var holds = new List<CoordinationRetirementHold>();
                foreach (var job in page.Take(16)) {
                    try {
                        engine.RequireTemporaryWorker(job.Binding);
                        items.Add(job);
                    } catch (CoordinationConflictException error) when (error.Message == "binding-worker-not-temporary") {
                        holds.Add(new(job.Binding.RequestId, error.Message));
                    }
                }
                return new CoordinationRetirementPage(items.ToArray(), page.Length > 16 ? state.Work[page[15].Binding.RequestId].Order : null) {
                    Holds = holds.ToArray()
                };
            }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/requests/{id}/retirement", async (string id, HttpContext http, CancellationToken ct) =>
            await store.ExecuteAsync((engine, state) => {
                if (!state.Retirements.TryGetValue(id, out var job) ||
                    !state.Work.TryGetValue(id, out var work) || !Visible(work, Caller(http))) return Results.NotFound();
                engine.RequireTemporaryWorker(job.Binding);
                return Results.Ok(job);
            }, DateTimeOffset.UtcNow, ct));
        app.MapPost(Prefix + "/requests/{id}/retirement", async (string id, CoordinationRetirementReceipt receipt,
            HttpContext http, CancellationToken ct) => {
            CoordinationValidation.Require(id == receipt.Binding.RequestId, "retirement-request-conflict");
            return Results.Ok(await store.ExecuteAsync((engine, _) => engine.Retire(receipt, Caller(http)), DateTimeOffset.UtcNow, ct));
        });
    }
}
