using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns duplicate/conflict (CNF) GPO objects for one domain (read-only).
/// </summary>
public sealed class AdGpoDuplicatesTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_duplicates",
        "Find duplicate/conflict (CNF) Group Policy Container objects under CN=Policies for one domain (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoDuplicatesResult(
        string DomainName,
        int Scanned,
        bool Truncated,
        IReadOnlyList<GpoDuplicateObject> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoDuplicatesTool"/> class.
    /// </summary>
    public AdGpoDuplicatesTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecuteDomainRowsViewTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: GPO Duplicates (preview)",
            defaultErrorMessage: "GPO duplicate query failed.",
            maxTop: MaxViewTop,
            query: static domainName => GpoDuplicateService.Get(domainName),
            collectionSucceededSelector: static view => view.CollectionSucceeded,
            collectionErrorSelector: static view => view.CollectionError,
            allRowsSelector: static view => view.Items,
            resultFactory: static (domainName, _, _, rows, scanned, truncated) => new AdGpoDuplicatesResult(
                DomainName: domainName,
                Scanned: scanned,
                Truncated: truncated,
                Rows: rows));
    }
}

