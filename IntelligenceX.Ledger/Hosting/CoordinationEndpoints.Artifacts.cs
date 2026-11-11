using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static void MapArtifacts(WebApplication app, CoordinationStore store, CoordinationArtifactStore artifacts) {
        app.MapPost(Prefix + "/requests/{id}/result/content", async (string id, CoordinationArtifactCommand command,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((_, state) => {
                var caller = Caller(http);
                CoordinationValidation.Require(state.Work.TryGetValue(id, out var work), "request-not-found");
                CoordinationValidation.Require(caller.Role == "host" &&
                    work.Machine == caller.MachineId && work.Generation == command.Generation && work.GrantId == command.GrantId &&
                    work.Machine is not null && state.Hosts.TryGetValue(work.Machine, out var host) && host.BootId == command.BootId &&
                    (work.BootId == command.BootId || work.State is "reconcile" or "cancelled") &&
                    (state.Bindings.TryGetValue(id, out var binding) && binding.Generation == command.Generation ||
                     work.State == "reconcile" || work.State == "cancelled" && work.Assignee is null && work.ExecutionId is null) &&
                    (work.State is "submitted" or "started" or "reconcile" or "succeeded" or "failed" or "cancelled" ||
                     work.State == "admitted" && binding?.GrantId == command.GrantId), "result-content-owner-conflict");
                var artifact = artifacts.Put(id, command.Generation, command.GrantId, command.Content);
                CoordinationValidation.Require(work.ResultRef is null || work.ResultRef == artifact.ResultRef, "result-content-identity-conflict");
                return artifact;
            }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/requests/{id}/result/content", async (string id, HttpContext http, CancellationToken ct) =>
            await store.ExecuteAsync((_, state) => {
                if (!state.Work.TryGetValue(id, out var work) || work.ResultRef is null ||
                    !state.ResultOutbox.TryGetValue(id, out var result) || !ResultVisible(result, work, Caller(http)))
                    return Results.NotFound();
                var artifact = artifacts.ReadForAssignment(id, work.Generation, work.GrantId!, work.ResultRef);
                return artifact is null ? Results.NotFound() : Results.Ok(artifact);
            }, DateTimeOffset.UtcNow, ct));
    }

    private static void RequireResultContent(CoordinationWork work, CoordinationArtifactStore artifacts) {
        if (work.Request.ReturnTo is null) return;
        CoordinationValidation.Require(work.ResultRef is not null &&
            artifacts.ReadForAssignment(work.Request.Id, work.Generation, work.GrantId!, work.ResultRef) is not null,
            "result-content-required");
    }
}
