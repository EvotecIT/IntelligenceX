using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>Thin authenticated commands; all lifecycle decisions belong to the shared engine.</summary>
internal static partial class CoordinationEndpoints {
    private const string Prefix = "/api/coordination/v1";
    internal static CoordinationConfiguration Load(string path, LedgerSettings settings) {
        if (new FileInfo(path).Length > 262144) throw new InvalidDataException("Coordination configuration exceeds its bound.");
        var config = JsonSerializer.Deserialize<CoordinationConfiguration>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Missing coordination configuration.");
        CoordinationValidation.Policy(config.InitialPolicy);
        if (config.Principals is not { Length: > 0 and <= 256 } || config.Principals.Any(p =>
            !CoordinationValidation.Id(p.Id) || p.Role is not ("client" or "host" or "operator") || p.Token is null || p.Token.Length < 32 ||
            p.Token.Any(char.IsWhiteSpace) || p.Token == settings.ReadToken || p.Token == settings.WriteToken ||
            p.Projects is null || !p.Projects.All(CoordinationValidation.Id) ||
            p.AllowRequesterEnrollment && (p.Role != "host" || p.Projects.Length == 0) ||
            p.Role == "host" && !config.InitialPolicy.Machines.Any(m => m.Id == p.MachineId)) ||
            config.Principals.Select(p => p.Id).Distinct().Count() != config.Principals.Length ||
            config.Principals.Select(p => p.Token).Distinct().Count() != config.Principals.Length)
            throw new InvalidDataException("Invalid scoped coordination principals.");
        return config;
    }

    private static CoordinationPrincipal Caller(HttpContext context) => (CoordinationPrincipal)context.Items[typeof(CoordinationPrincipal)]!;
    internal static bool OwnsRoute(PathString path) => new[] { "/goals", "/requests", "/hosts", "/workers", "/requesters", "/policy", "/summary", "/changes", "/results", "/retirements" }
        .Any(suffix => path.StartsWithSegments(Prefix + suffix));

    internal static async Task<bool> AuthenticateAsync(HttpContext context, string bearer, CoordinationConfiguration config,
        CoordinationStore store) {
        var principal = config.Principals.FirstOrDefault(p => LedgerRuntime.TokenEquals(bearer, p.Token)) ??
            await store.ExecuteAsync((engine, _) => engine.AuthenticateRequester(bearer), DateTimeOffset.UtcNow, context.RequestAborted);
        if (principal is null) return false;
        context.Items[typeof(CoordinationPrincipal)] = principal;
        return true;
    }

    private static bool Visible(CoordinationWork work, CoordinationPrincipal p) => p.Role switch {
        "operator" => true,
        "host" => work.Machine == p.MachineId,
        _ => work.Requester == p.Id && (p.RequesterId is not null || p.Projects.Contains(work.Request.Checkpoint.Project))
    };

    internal static void Map(WebApplication app, CoordinationStore store, string dataDirectory) {
        var artifacts = new CoordinationArtifactStore(Path.Combine(dataDirectory, "coordination-results"));
        MapArtifacts(app, store, artifacts);
        MapRecovery(app, store, artifacts);
        MapLifecycle(app, store);
        app.MapGet(Prefix + "/workers/{provider}/{sessionId}", async (string provider, string sessionId,
            HttpContext http, CancellationToken ct) => Results.Ok(await store.ExecuteAsync((e, _) =>
                e.WorkerStatus(provider, sessionId, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requesters", async (RequesterEnrollment enrollment, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => e.EnrollRequester(enrollment, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/goals", async (CoordinationGoalCommand command, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => e.SetGoal(command.Goal, Caller(http), command.ExpectedRevision), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/goals/{id}", async (string id, HttpContext http, CancellationToken ct) =>
            await store.ExecuteAsync((_, s) => s.Goals.TryGetValue(id, out var goal) &&
                (goal.Owner == Caller(http).Id || Caller(http).Role == "operator") ? Results.Ok(goal) : Results.NotFound(), DateTimeOffset.UtcNow, ct));
        app.MapPost(Prefix + "/requests", async (CoordinationRequest request, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => e.Submit(request, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/preview", async (CoordinationRequest request, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((_, s) => {
                var copy = JsonSerializer.Deserialize<CoordinationState>(JsonSerializer.Serialize(s))!;
                return new CoordinationEngine(copy, DateTimeOffset.UtcNow).Submit(request, Caller(http));
            }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/requests", async (long? afterOrder, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, s) => {
                e.Schedule();
                var p = Caller(http);
                var page = s.Work.Values.Where(w => w.Order > Math.Max(0, afterOrder ?? 0) && Visible(w, p) &&
                    (p.Role != "host" || w.State is "admitted" or "submitted" or "started" or "reconcile"))
                    .OrderBy(w => w.Order).Take(17).ToArray();
                return new CoordinationWorkPage(page.Take(16).ToArray(), page.Length > 16 ? page[15].Order : null);
            }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/requests/{id}", async (string id, HttpContext http, CancellationToken ct) =>
            await store.ExecuteAsync((e, s) => {
                e.Schedule();
                if (!s.Work.TryGetValue(id, out var work) || !Visible(work, Caller(http))) return Results.NotFound();
                if (s.Retirements.TryGetValue(id, out var retirement)) e.RequireTemporaryWorker(retirement.Binding);
                return Results.Ok(new CoordinationRequestView(work, s.Bindings.GetValueOrDefault(id),
                    s.ResultOutbox.GetValueOrDefault(id), retirement));
            }, DateTimeOffset.UtcNow, ct));
        app.MapPost(Prefix + "/requests/{id}/receipts/{stage}", async (string id, string stage, ExecutionReceipt receipt, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => {
                var work = e.Receipt(id, stage, receipt, Caller(http));
                if (stage == "terminal") RequireResultContent(work, artifacts);
                return work;
            }, DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/requests/{id}/accept", async (string id, CoordinationAcceptance acceptance, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => e.Accept(id, acceptance.ResultRef, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/hosts", async (CoordinationHost host, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, state) => {
                e.ObserveHost(host, Caller(http));
                return new CoordinationAcknowledgement(true, state.Policy.GrantSeconds);
            }, DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/workers", async (CoordinationWorker worker, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => { e.ObserveWorker(worker, Caller(http)); return new { acknowledged = true }; }, DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/policy", async (CoordinationPolicyCommand command, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, _) => { e.ReplacePolicy(command.Policy, Caller(http), command.ExpectedRevision); return command.Policy; }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/policy", async (HttpContext http, CancellationToken ct) => {
            CoordinationValidation.Require(Caller(http).Role == "operator", "policy-role-denied");
            return Results.Ok(await store.ExecuteAsync((_, s) => s.Policy, DateTimeOffset.UtcNow, ct));
        });
        app.MapGet(Prefix + "/summary", async (string? project, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((e, s) => {
                var p = Caller(http);
                CoordinationValidation.Require(p.Role == "operator" || project is not null &&
                    (p.Projects.Contains(project) || p.RequesterId is not null &&
                        s.Work.Values.Any(w => w.Requester == p.Id && w.Request.Checkpoint.Project == project)), "project-required-or-denied");
                e.Schedule(); return e.Summary(project, include: work => Visible(work, p));
            }, DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/changes", async (long? after, HttpContext http, CancellationToken ct) => {
            var changes = await store.ChangesAsync(Math.Max(after ?? 0, 0), 100, ct);
            var p = Caller(http);
            var visible = await store.ExecuteAsync((_, s) => changes.Where(c => {
                if (p.Role == "operator") return true;
                if (c.Stage == "policy-updated") return false;
                if (c.Stage is "goal-updated" or "goal-paused" or "goal-stopped")
                    return s.Goals.TryGetValue(c.RequestId, out var goal) && goal.Owner == p.Id;
                return s.Work.TryGetValue(c.RequestId, out var w) &&
                    (Visible(w, p) || s.ResultOutbox.TryGetValue(c.RequestId, out var result) && ResultVisible(result, w, p));
            }).ToArray(), DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { nextCursor = changes.LastOrDefault()?.Sequence ?? after ?? 0, changes = visible });
        });
    }

}
