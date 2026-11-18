namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    /// <summary>Reads every catalogued project with open issues, PRs, observed worktrees and task verdict counts.</summary>
    public Task<ProjectStatusView> ProjectStatusAsync(string? repo = null, int limit = 40, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/projects/status?limit=" + limit + (repo is null ? "" : "&repo=" + Uri.EscapeDataString(repo)),
            LedgerReadJson.Default.ProjectStatusView, token);

    /// <summary>Reads task verdicts with evidence, missing facts and suggested (never applied) actions.</summary>
    public Task<WorkReconcileView> WorkReconcileAsync(string? verdict = null, string? repo = null, int limit = 50, CancellationToken token = default) =>
        GetAsync("api/ledger/v1/work/reconcile?limit=" + limit + (verdict is null ? "" : "&verdict=" + Uri.EscapeDataString(verdict)) +
            (repo is null ? "" : "&repo=" + Uri.EscapeDataString(repo)), LedgerReadJson.Default.WorkReconcileView, token);
}
