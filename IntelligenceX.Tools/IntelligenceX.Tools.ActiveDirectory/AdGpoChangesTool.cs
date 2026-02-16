using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

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

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        if (!ToolTime.TryParseUtcOptional(ToolArgs.GetOptionalTrimmed(arguments, "since_utc"), out var sinceUtc, out var sinceError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", $"since_utc: {sinceError}"));
        }

        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);
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
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", ex.Message));
        } catch (InvalidOperationException ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", ex.Message));
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", $"GPO change query failed: {ex.Message}"));
        }

        var result = new AdGpoChangesResult(
            DomainName: domainName,
            SinceUtc: sinceUtc,
            Scanned: scanned,
            Truncated: truncated,
            Items: items);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: items,
            viewRowsPath: "items_view",
            title: "Active Directory: GPO changes (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("max_results", maxResults);
                if (sinceUtc.HasValue) {
                    meta.Add("since_utc", ToolTime.FormatUtc(sinceUtc));
                }
            });
        return Task.FromResult(response);
    }
}
