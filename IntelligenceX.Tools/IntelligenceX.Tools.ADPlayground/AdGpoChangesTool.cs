using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists recently changed Group Policy Objects for a domain (read-only, capped).
/// </summary>
public sealed class AdGpoChangesTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_changes",
        "List recently changed Group Policy Objects for a domain (read-only, capped).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Domain DNS name to query.")),
                ("since_utc", ToolSchema.String("Optional ISO-8601 UTC lower bound for last modification timestamp.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoChangesResult(
        string DomainName,
        DateTime? SinceUtc,
        int Scanned,
        bool Truncated,
        IReadOnlyList<GpoListItem> Items);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoChangesTool"/> class.
    /// </summary>
    public AdGpoChangesTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainName(arguments, out var domainName, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        if (!ToolTime.TryParseUtcOptional(ToolArgs.GetOptionalTrimmed(arguments, "since_utc"), out var sinceUtc, out var sinceError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", $"since_utc: {sinceError}"));
        }

        var maxResults = ResolveBoundedMaxResults(arguments);
        var items = new List<GpoListItem>(Math.Min(maxResults, 512));
        var scanned = 0;
        var truncated = false;

        try {
            foreach (var item in GpoChangeHistoryService.GetChanges(domainName, sinceUtc)) {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                if (items.Count >= maxResults) {
                    truncated = true;
                    break;
                }

                items.Add(item);
            }
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "GPO change query failed.",
                invalidOperationErrorCode: "query_failed"));
        }

        var result = new AdGpoChangesResult(
            DomainName: domainName,
            SinceUtc: sinceUtc,
            Scanned: scanned,
            Truncated: truncated,
            Items: items);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: items,
            viewRowsPath: "items_view",
            title: "Active Directory: GPO changes (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                if (sinceUtc.HasValue) {
                    meta.Add("since_utc", ToolTime.FormatUtc(sinceUtc));
                }
            }));
    }
}

