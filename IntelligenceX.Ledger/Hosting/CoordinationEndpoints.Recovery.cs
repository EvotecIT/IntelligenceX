using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static void MapRecovery(WebApplication app, CoordinationStore store, CoordinationArtifactStore artifacts) {
        app.MapPost(Prefix + "/requests/{id}/reconcile", async (string id, CoordinationReconciliation command,
            HttpContext http, CancellationToken ct) => {
            CoordinationValidation.Require(command.RequestId == id, "reconciliation-request-conflict");
            return Results.Ok(await store.ExecuteAsync((engine, _) => {
                var work = engine.Reconcile(command, Caller(http));
                RequireResultContent(work, artifacts);
                return work;
            }, DateTimeOffset.UtcNow, ct));
        });
        app.MapPost(Prefix + "/requests/{id}/reauthorize", async (string id, CoordinationReauthorization command,
            HttpContext http, CancellationToken ct) => {
            CoordinationValidation.Require(command.RequestId == id, "reauthorization-request-conflict");
            return Results.Ok(await store.ExecuteAsync((engine, _) => engine.Reauthorize(command, Caller(http)), DateTimeOffset.UtcNow, ct));
        });
    }
}
