using System.ComponentModel;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol.Server;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Thin read tools. State evaluation and retrieval remain service-owned.</summary>
[McpServerToolType]
public sealed class LedgerTools(LedgerReadClient client) {
    [McpServerTool(Name = "repo_activity", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read current repository work grouped by checkout. The compact default includes full live identities, recent-session counts/titles, up to five shared filenames and total counts. Set details for all session rows, and recent24h for 24-hour history. Summary comes first.")]
    public Task<RepositoryActivity> RepoActivity(string repo, CancellationToken cancellationToken = default, bool recent24h = false, bool details = false) => client.RepoActivityAsync(repo, cancellationToken, recent24h, details);

    [McpServerTool(Name = "overlap", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Check duplicate work using branches, filenames or task text across live and last-two-hour sessions. Returns none/possible/conflict grouped by worktree, with live identities and recent counts/titles. Set details for full session rows and recent24h for 24-hour history. Text matching is lexical.")]
    public Task<RepositoryOverlap> Overlap(string repo, string? branch = null, string[]? files = null, string? text = null, CancellationToken cancellationToken = default, bool recent24h = false, bool details = false) =>
        client.OverlapAsync(repo, branch, files, text, cancellationToken, recent24h, details);

    [McpServerTool(Name = "who", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read live reservations for an exact key or a prefix. Total includes any rows omitted from this compact response.")]
    public Task<LeaseQuery> Who(string key, bool prefix = false, CancellationToken cancellationToken = default) => client.WhoAsync(key, prefix, cancellationToken);

    [McpServerTool(Name = "machine_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read one enrolled machine's reporter health, live-session count, observed session-limit count and reservations.")]
    public Task<MachineStatus> MachineStatus(string id, CancellationToken cancellationToken = default) => client.MachineStatusAsync(id, cancellationToken);

    [McpServerTool(Name = "budget_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read observed usage-limit groups across machines. Credits and reset times remain unknown. An unmapped account is session-scoped evidence.")]
    public Task<BudgetStatus> BudgetStatus(CancellationToken cancellationToken = default) => client.BudgetStatusAsync(cancellationToken);

    [McpServerTool(Name = "project_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read project responsibility/dependency evidence and optional maintained knowledge. Omit project for compact discovery. Shows up to three claims by default; request a larger maxResponsibilities when needed. Coverage identifies omitted facts.")]
    public async Task<ProjectDirectoryView> ProjectContext(
        [Description("Exact enrolled project identifier; omit to discover enrolled projects.")] string? project = null,
        [Description("Optional topic to retrieve from this project's maintained sources.")] string? query = null,
        [Description("Maximum observed claims to return, from 1 to 64; defaults to 3.")] int maxResponsibilities = 3,
        CancellationToken cancellationToken = default) {
        if (maxResponsibilities is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maxResponsibilities));
        if (project is null) {
            var directory = await client.ProjectsAsync(cancellationToken);
            return directory with { Projects = directory.Projects.Select(entry => entry with {
                Responsibilities = [], Dependencies = [], Knowledge = [],
                Coverage = entry.Coverage.Append("directory-discovery:request-exact-project-for-facts").ToArray()
            }).ToArray() };
        }
        var context = await client.ProjectAsync(project, query, cancellationToken);
        if (context.Responsibilities.Length > maxResponsibilities) context = context with {
            Coverage = context.Coverage.Append("responsibilities:showing-" + maxResponsibilities + "-of-" + context.Responsibilities.Length).ToArray(),
            Responsibilities = context.Responsibilities.Take(maxResponsibilities).ToArray()
        };
        return new(context.AsOfUtc, [context], context.Coverage);
    }

    [McpServerTool(Name = "knowledge_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Search explicitly enrolled maintained documents within one project. Excerpts include content revisions, source links and outcome metadata.")]
    public Task<KnowledgeSearchView> KnowledgeSearch(string project, string query, CancellationToken cancellationToken = default) =>
        client.SearchAsync(project, query, cancellationToken);

    [McpServerTool(Name = "work_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read current work counts, machine coverage and up to eight compact rows. Optionally filter verdict: working, waiting, needs-you, stuck, done. Use work_item for evidence.")]
    public async Task<WorkSummary> WorkStatus(string? verdict = null, CancellationToken cancellationToken = default) {
        var view = await client.WorkAsync(cancellationToken, verdict, 8);
        return new(view.AsOfUtc, view.Counts, view.Machines.Select(machine => new MachineSummary(machine.Id, machine.State)).ToArray(),
            view.Items.Select(row => new WorkSummaryRow(row.Item.Id, row.Label, row.Item.Provider, row.Item.MachineId,
                row.Item.Verdict, row.Item.ReasonCode, row.Item.SinceUtc, row.Item.NextActor, row.Item.LastKnown)).ToArray(),
            view.Coverage, view.Truncated);
    }

    [McpServerTool(Name = "work_item", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read the complete service verdict and evidence for an exact Ledger work item, including retained historical items.")]
    public Task<LedgerItemView> WorkItem(string itemId, CancellationToken cancellationToken = default) =>
        client.ItemAsync(itemId, cancellationToken);
}

/// <summary>Compact tool output; verdicts and counts are copied from the service.</summary>
public sealed record WorkSummary(DateTimeOffset AsOfUtc, Dictionary<string, int> Counts, MachineSummary[] Machines,
    WorkSummaryRow[] Items, string[] Coverage, bool Truncated);
/// <summary>Machine coverage copied without repeating per-item evidence.</summary>
public sealed record MachineSummary(string Id, string State);
/// <summary>Compact item identity and service verdict for optional evidence drill-down.</summary>
public sealed record WorkSummaryRow(string Id, string Label, string Provider, string MachineId, string Verdict,
    string ReasonCode, DateTimeOffset SinceUtc, string? NextActor, bool LastKnown);
