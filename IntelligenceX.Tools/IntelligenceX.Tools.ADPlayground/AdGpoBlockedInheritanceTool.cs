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
/// Returns OU blocked-inheritance posture for one domain (read-only).
/// </summary>
public sealed class AdGpoBlockedInheritanceTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxRows = 200000;
    private const int MaxRowsCap = 500000;
    private const int MaxViewTop = 5000;

    private sealed record GpoBlockedInheritanceRequest(
        bool OnlyBlocked,
        int MaxRows);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_blocked_inheritance",
        "Enumerate OUs and report whether Group Policy inheritance is blocked (gPOptions bit 1; read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("only_blocked", ToolSchema.Boolean("When true (default), return only OUs with blocked inheritance.")),
                ("max_rows", ToolSchema.Integer("Maximum OUs to scan before projection (capped). Default 200000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoBlockedInheritanceResult(
        string DomainName,
        bool OnlyBlocked,
        int MaxRows,
        int Scanned,
        bool Truncated,
        int BlockedCount,
        IReadOnlyList<GpoBlockedInheritanceRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoBlockedInheritanceTool"/> class.
    /// </summary>
    public AdGpoBlockedInheritanceTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<GpoBlockedInheritanceRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader =>
            ToolRequestBindingResult<GpoBlockedInheritanceRequest>.Success(new GpoBlockedInheritanceRequest(
                OnlyBlocked: reader.Boolean("only_blocked", defaultValue: true),
                MaxRows: reader.CappedInt32("max_rows", DefaultMaxRows, 1, MaxRowsCap))));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoBlockedInheritanceRequest> context, CancellationToken cancellationToken) {
        var request = context.Request;
        return ExecuteDomainRowsViewTool(
            arguments: context.Arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: GPO Blocked Inheritance (preview)",
            defaultErrorMessage: "GPO blocked-inheritance query failed.",
            maxTop: MaxViewTop,
            query: domainName => GpoBlockedInheritanceService.Get(domainName, onlyBlocked: request.OnlyBlocked, maxRows: request.MaxRows),
            collectionSucceededSelector: static view => view.CollectionSucceeded,
            collectionErrorSelector: static view => view.CollectionError,
            allRowsSelector: static view => view.Items,
            resultFactory: (domainName, view, _, rows, scanned, truncated) => new AdGpoBlockedInheritanceResult(
                DomainName: domainName,
                OnlyBlocked: request.OnlyBlocked,
                MaxRows: request.MaxRows,
                Scanned: scanned,
                Truncated: truncated,
                BlockedCount: view.Items.Count(static row => row.BlockedInheritance),
                Rows: rows),
            additionalMetaMutate: (meta, _, _, _) => {
                meta.Add("only_blocked", request.OnlyBlocked);
                meta.Add("max_rows", request.MaxRows);
            });
    }
}
