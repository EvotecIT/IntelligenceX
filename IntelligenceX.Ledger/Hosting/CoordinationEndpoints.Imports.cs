using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static readonly JsonSerializerOptions ImportJson = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };

    /// <summary>Takes requests a machine finished in its local store while the Ledger was unreachable. The result file is
    /// written first (content-addressed, so a rejected import leaves nothing that names it), then the archive entry.</summary>
    private static void MapImports(WebApplication app, CoordinationStore store, CoordinationArtifactStore artifacts) =>
        app.MapPost(Prefix + "/imports", async (CoordinationImport import, HttpContext http, CancellationToken ct) => {
            var caller = Caller(http);
            CoordinationValidation.Require(caller.Role == "host", "import-role-denied");
            CoordinationArchiveEntry? entry;
            try { entry = import.Entry.Deserialize<CoordinationArchiveEntry>(ImportJson); }
            catch (JsonException) { entry = null; }
            CoordinationValidation.Require(entry is not null && entry.Work is not null && entry.Work.Request?.Checkpoint is not null, "invalid-import");
            // Checked before the result file is written, so an import for another project leaves nothing behind.
            CoordinationValidation.Require(caller.Projects.Contains(entry!.Work.Request.Checkpoint.Project, StringComparer.Ordinal), "project-not-authorized");
            if (entry.Work.ResultRef is { } resultRef) {
                CoordinationValidation.Require(import.Content is { Length: > 0 } content && resultRef ==
                    "result-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), "import-result-mismatch");
                CoordinationValidation.Require(entry.Work.GrantId is not null, "invalid-import");
                artifacts.Import(entry.Work, import.Content!);
            }
            return Results.Ok(await store.ExecuteAsync(entry.RequestId, (engine, _, archived) => engine.Import(entry, caller, archived),
                DateTimeOffset.UtcNow, ct));
        });
}
