using IntelligenceX.Ledger.Context;

namespace IntelligenceX.Ledger.Hosting;

internal static class ProjectContextEndpoints {
    internal static void Map(WebApplication app, ProjectContextService context, LedgerRuntime runtime) {
        app.MapGet("/api/ledger/v1/work", async (string? verdict, int? limit, CancellationToken token) => {
            if (verdict is not (null or "working" or "waiting" or "needs-you" or "stuck" or "done"))
                return Results.BadRequest(new { error = "invalid-verdict" });
            return Results.Ok(await runtime.WorkAsync(context, DateTimeOffset.UtcNow, token, verdict, Math.Clamp(limit ?? 200, 1, 200)));
        });
        app.MapGet("/api/coordination/v1/projects", async (CancellationToken token) =>
            Results.Ok(await context.ProjectsAsync(token)));
        app.MapGet("/api/coordination/v1/projects/{id}", async (string id, string? query, CancellationToken token) => {
            try {
                var project = await context.ProjectAsync(id, query, token);
                return project is null ? Results.NotFound() : Results.Ok(project);
            } catch (ProjectDirectoryUnavailableException) { return Results.Json(new { error = "project-directory-unavailable" }, statusCode: 503); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "invalid-query" }); }
        });
        app.MapGet("/api/coordination/v1/knowledge", async (string project, string query, CancellationToken token) => {
            try {
                var found = await context.SearchAsync(project, query, token);
                return found is null ? Results.NotFound() : Results.Ok(found);
            } catch (ProjectDirectoryUnavailableException) { return Results.Json(new { error = "project-directory-unavailable" }, statusCode: 503); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "invalid-query" }); }
        });
    }
}
