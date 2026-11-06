using System.ComponentModel;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol.Server;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Thin read tools. State evaluation and retrieval remain service-owned.</summary>
[McpServerToolType]
public sealed class LedgerTools(LedgerReadClient client) {
    [McpServerTool(Name = "project_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read enrolled project responsibility, dependency evidence and optional relevant maintained knowledge. Returns freshness and source references.")]
    public async Task<ProjectDirectoryView> ProjectContext(
        [Description("Exact enrolled project identifier; omit to discover enrolled projects.")] string? project = null,
        [Description("Optional topic to retrieve from this project's maintained sources.")] string? query = null,
        CancellationToken cancellationToken = default) {
        if (project is null) return await client.ProjectsAsync(cancellationToken);
        var context = await client.ProjectAsync(project, query, cancellationToken);
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
public sealed record MachineSummary(string Id, string State);
public sealed record WorkSummaryRow(string Id, string Label, string Provider, string MachineId, string Verdict,
    string ReasonCode, DateTimeOffset SinceUtc, string? NextActor, bool LastKnown);
