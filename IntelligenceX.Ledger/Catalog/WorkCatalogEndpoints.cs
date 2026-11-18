using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;

namespace IntelligenceX.Ledger.Catalog;

/// <summary>Read-only project and task views. Without a configured catalog they answer with that coverage gap.</summary>
internal static class WorkCatalogEndpoints {
    internal static void Map(WebApplication app, WorkCatalogService? catalog, LedgerRuntime runtime) {
        const string prefix = "/api/ledger/v1";
        Task<LedgerItem[]> Items(DateTimeOffset now, CancellationToken ct) => runtime.ItemsAsync(now, ct);
        app.MapGet(prefix + "/projects/status", async (string? repo, int? limit, CancellationToken ct) => catalog is null
            ? Results.Ok(new ProjectStatusView(DateTimeOffset.UtcNow, null, 0, [], [], ["work-catalog-not-configured"], false))
            : Results.Ok(await catalog.ProjectsAsync(Items, Repo(repo), Math.Clamp(limit ?? 40, 1, 500), ct)));
        app.MapGet(prefix + "/work/reconcile", async (string? verdict, string? repo, int? limit, CancellationToken ct) => catalog is null
            ? Results.Ok(new WorkReconcileView(DateTimeOffset.UtcNow, null, [], [], ["work-catalog-not-configured"], false))
            : Results.Ok(await catalog.ReconcileAsync(Items, verdict, Repo(repo), Math.Clamp(limit ?? 50, 1, 500), ct)));
    }

    private static string? Repo(string? repo) {
        if (repo is null) return null;
        if (repo.Split('/') is not [var owner, var name] || !WorkCatalogSettings.ValidName(owner) || !WorkCatalogSettings.ValidName(name))
            throw new ArgumentException("Repository must be owner/name.");
        return repo;
    }
}
