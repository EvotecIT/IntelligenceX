using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Trusts;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Explores and summarizes domain/forest trust posture (read-only).
/// </summary>
public sealed class AdTrustTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_trust",
        "Explore domain/forest trusts with posture filters and optional grouped summaries (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest root DNS name. Defaults to current forest.")),
                ("recursive", ToolSchema.Boolean("When true, recursively explores remote forests linked by forest trusts.")),
                ("skip_validation", ToolSchema.Boolean("When true, skips lightweight trust validation checks. Default true.")),
                ("status", ToolSchema.String("Activity filter by modification age.").Enum("any", "active", "inactive")),
                ("inactive_days", ToolSchema.Integer("Threshold in days used by status=active/inactive. Default 365.")),
                ("old_protocol", ToolSchema.Boolean("When true, keep trusts with old/weak protocol indicators.")),
                ("impermeability", ToolSchema.Boolean("When true, keep trusts lacking selective auth and/or SID filtering.")),
                ("trust_type", ToolSchema.String("Trust type filter.").Enum("any", "forest", "external", "parent_child", "tree_root")),
                ("direction", ToolSchema.String("Trust direction filter.").Enum("any", "inbound", "outbound", "bidirectional")),
                ("summary", ToolSchema.Boolean("When true, emit grouped summary rows instead of raw trust rows.")),
                ("summary_by", ToolSchema.String("Summary grouping key (used when summary=true).").Enum("direction", "type")),
                ("summary_matrix", ToolSchema.Boolean("When true, emit a type x direction matrix summary.")),
                ("include_communication_issues", ToolSchema.Boolean("When true, emit trust communication issues from logs/events instead of trust posture rows.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdTrustResult(
        string Mode,
        string? ForestName,
        bool Recursive,
        bool SkipValidation,
        string Status,
        int InactiveDays,
        bool OldProtocol,
        bool Impermeability,
        string TrustType,
        string Direction,
        string SummaryBy,
        int Scanned,
        bool Truncated,
        int TotalFiltered,
        IReadOnlyList<TrustExplorer.Assessment> Items,
        IReadOnlyList<TrustSummaryRow> SummaryRows,
        IReadOnlyList<TrustSummaryMatrixRow> MatrixRows,
        IReadOnlyList<TrustCommunicationIssue> CommunicationIssues);

    private sealed record TrustRequestContext(
        string? ForestName,
        bool Recursive,
        bool SkipValidation,
        string Status,
        int InactiveDays,
        bool OldProtocol,
        bool Impermeability,
        string TrustType,
        string Direction,
        int MaxResults);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdTrustTool"/> class.
    /// </summary>
    public AdTrustTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var recursive = ToolArgs.GetBoolean(arguments, "recursive", defaultValue: false);
        var skipValidation = ToolArgs.GetBoolean(arguments, "skip_validation", defaultValue: true);
        var includeCommunicationIssues = ToolArgs.GetBoolean(arguments, "include_communication_issues", defaultValue: false);
        var summary = ToolArgs.GetBoolean(arguments, "summary", defaultValue: false);
        var summaryMatrix = ToolArgs.GetBoolean(arguments, "summary_matrix", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var status = NormalizeEnumValue(ToolArgs.GetOptionalTrimmed(arguments, "status"), "any");
        var trustType = NormalizeEnumValue(ToolArgs.GetOptionalTrimmed(arguments, "trust_type"), "any");
        var direction = NormalizeEnumValue(ToolArgs.GetOptionalTrimmed(arguments, "direction"), "any");
        var summaryBy = NormalizeEnumValue(ToolArgs.GetOptionalTrimmed(arguments, "summary_by"), "direction");
        var inactiveDays = ToolArgs.GetCappedInt32(arguments, "inactive_days", 365, 1, 36500);

        if (!IsOneOf(status, "any", "active", "inactive")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "status must be one of: any, active, inactive."));
        }
        if (!IsOneOf(trustType, "any", "forest", "external", "parent_child", "tree_root")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "trust_type must be one of: any, forest, external, parent_child, tree_root."));
        }
        if (!IsOneOf(direction, "any", "inbound", "outbound", "bidirectional")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "direction must be one of: any, inbound, outbound, bidirectional."));
        }
        if (!IsOneOf(summaryBy, "direction", "type")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "summary_by must be one of: direction, type."));
        }

        var oldProtocol = ToolArgs.GetBoolean(arguments, "old_protocol", defaultValue: false);
        var impermeability = ToolArgs.GetBoolean(arguments, "impermeability", defaultValue: false);
        var context = new TrustRequestContext(
            ForestName: forestName,
            Recursive: recursive,
            SkipValidation: skipValidation,
            Status: status,
            InactiveDays: inactiveDays,
            OldProtocol: oldProtocol,
            Impermeability: impermeability,
            TrustType: trustType,
            Direction: direction,
            MaxResults: maxResults);

        if (includeCommunicationIssues) {
            return Task.FromResult(RunCommunicationIssuesMode(
                arguments: arguments,
                context: context));
        }

        IReadOnlyList<TrustExplorer.Assessment> filtered;
        try {
            filtered = TrustExplorerFilter.Get(new TrustExplorerFilter.Options {
                Forest = context.ForestName,
                Recursive = context.Recursive,
                SkipValidation = context.SkipValidation,
                Status = ToFilterStatus(context.Status),
                InactiveDays = context.InactiveDays,
                OldProtocol = context.OldProtocol,
                Impermeability = context.Impermeability,
                Type = ToFilterType(context.TrustType),
                Direction = ToFilterDirection(context.Direction)
            });
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "Trust query failed.",
                invalidOperationErrorCode: "query_failed"));
        }

        if (summaryMatrix) {
            return Task.FromResult(BuildMatrixResponse(
                arguments: arguments,
                context: context,
                filtered: filtered));
        }

        if (summary) {
            return Task.FromResult(BuildSummaryResponse(
                arguments: arguments,
                summaryBy: summaryBy,
                context: context,
                filtered: filtered));
        }

        return Task.FromResult(BuildRawResponse(
            arguments: arguments,
            context: context,
            filtered: filtered));
    }

    private static string RunCommunicationIssuesMode(JsonObject? arguments, TrustRequestContext context) {
        IReadOnlyList<TrustCommunicationIssue> allIssues;
        try {
            var analyzer = new TrustCommunicationAnalyzer();
            if (string.IsNullOrWhiteSpace(context.ForestName)) {
                allIssues = analyzer.AnalyzeForest().ToArray();
            } else {
                var buffer = new List<TrustCommunicationIssue>();
                foreach (var domain in DomainHelper.EnumerateForestDomainNames(context.ForestName)) {
                    buffer.AddRange(analyzer.AnalyzeDomain(domain));
                }
                allIssues = buffer;
            }
        } catch (Exception ex) {
            return ErrorFromException(
                ex,
                defaultMessage: "Trust communication diagnostics failed.",
                invalidOperationErrorCode: "query_failed");
        }

        var scanned = allIssues.Count;
        var rows = scanned > context.MaxResults ? allIssues.Take(context.MaxResults).ToArray() : allIssues;
        var truncated = scanned > rows.Count;

        var result = new AdTrustResult(
            Mode: "communication_issues",
            ForestName: context.ForestName,
            Recursive: false,
            SkipValidation: true,
            Status: "any",
            InactiveDays: 365,
            OldProtocol: false,
            Impermeability: false,
            TrustType: "any",
            Direction: "any",
            SummaryBy: "direction",
            Scanned: scanned,
            Truncated: truncated,
            TotalFiltered: scanned,
            Items: Array.Empty<TrustExplorer.Assessment>(),
            SummaryRows: Array.Empty<TrustSummaryRow>(),
            MatrixRows: Array.Empty<TrustSummaryMatrixRow>(),
            CommunicationIssues: rows);

        return BuildViewResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "issues_view",
            title: "Active Directory: Trust Communication Issues (preview)",
            truncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("mode", "communication_issues");
                meta.Add("max_results", context.MaxResults);
                if (!string.IsNullOrWhiteSpace(context.ForestName)) {
                    meta.Add("forest_name", context.ForestName);
                }
            });
    }

    private static string BuildRawResponse(
        JsonObject? arguments,
        TrustRequestContext context,
        IReadOnlyList<TrustExplorer.Assessment> filtered) {
        var scanned = filtered.Count;
        var rows = scanned > context.MaxResults ? filtered.Take(context.MaxResults).ToArray() : filtered;
        var truncated = scanned > rows.Count;

        var result = new AdTrustResult(
            Mode: "raw",
            ForestName: context.ForestName,
            Recursive: context.Recursive,
            SkipValidation: context.SkipValidation,
            Status: context.Status,
            InactiveDays: context.InactiveDays,
            OldProtocol: context.OldProtocol,
            Impermeability: context.Impermeability,
            TrustType: context.TrustType,
            Direction: context.Direction,
            SummaryBy: "direction",
            Scanned: scanned,
            Truncated: truncated,
            TotalFiltered: scanned,
            Items: rows,
            SummaryRows: Array.Empty<TrustSummaryRow>(),
            MatrixRows: Array.Empty<TrustSummaryMatrixRow>(),
            CommunicationIssues: Array.Empty<TrustCommunicationIssue>());

        return BuildViewResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "trusts_view",
            title: "Active Directory: Trusts (preview)",
            truncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                meta.Add("max_results", context.MaxResults);
            });
    }

    private static string BuildSummaryResponse(
        JsonObject? arguments,
        TrustRequestContext context,
        string summaryBy,
        IReadOnlyList<TrustExplorer.Assessment> filtered) {
        var allRows = TrustExplorerFilter.GetSummaryBy(filtered, summaryBy.Equals("type", StringComparison.OrdinalIgnoreCase) ? "Type" : "Direction");
        var scanned = allRows.Count;
        var rows = scanned > context.MaxResults ? allRows.Take(context.MaxResults).ToArray() : allRows;
        var truncated = scanned > rows.Count;

        var result = new AdTrustResult(
            Mode: "summary",
            ForestName: context.ForestName,
            Recursive: context.Recursive,
            SkipValidation: context.SkipValidation,
            Status: context.Status,
            InactiveDays: context.InactiveDays,
            OldProtocol: context.OldProtocol,
            Impermeability: context.Impermeability,
            TrustType: context.TrustType,
            Direction: context.Direction,
            SummaryBy: summaryBy,
            Scanned: scanned,
            Truncated: truncated,
            TotalFiltered: filtered.Count,
            Items: Array.Empty<TrustExplorer.Assessment>(),
            SummaryRows: rows,
            MatrixRows: Array.Empty<TrustSummaryMatrixRow>(),
            CommunicationIssues: Array.Empty<TrustCommunicationIssue>());

        return BuildViewResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "summary_view",
            title: "Active Directory: Trust Summary (preview)",
            truncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("mode", "summary");
                meta.Add("summary_by", summaryBy);
                meta.Add("total_filtered", filtered.Count);
                meta.Add("max_results", context.MaxResults);
            });
    }

    private static string BuildMatrixResponse(
        JsonObject? arguments,
        TrustRequestContext context,
        IReadOnlyList<TrustExplorer.Assessment> filtered) {
        var allRows = TrustExplorerFilter.GetSummaryMatrix(filtered);
        var scanned = allRows.Count;
        var rows = scanned > context.MaxResults ? allRows.Take(context.MaxResults).ToArray() : allRows;
        var truncated = scanned > rows.Count;

        var result = new AdTrustResult(
            Mode: "summary_matrix",
            ForestName: context.ForestName,
            Recursive: context.Recursive,
            SkipValidation: context.SkipValidation,
            Status: context.Status,
            InactiveDays: context.InactiveDays,
            OldProtocol: context.OldProtocol,
            Impermeability: context.Impermeability,
            TrustType: context.TrustType,
            Direction: context.Direction,
            SummaryBy: "direction",
            Scanned: scanned,
            Truncated: truncated,
            TotalFiltered: filtered.Count,
            Items: Array.Empty<TrustExplorer.Assessment>(),
            SummaryRows: Array.Empty<TrustSummaryRow>(),
            MatrixRows: rows,
            CommunicationIssues: Array.Empty<TrustCommunicationIssue>());

        return BuildViewResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "summary_matrix_view",
            title: "Active Directory: Trust Summary Matrix (preview)",
            truncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("mode", "summary_matrix");
                meta.Add("total_filtered", filtered.Count);
                meta.Add("max_results", context.MaxResults);
            });
    }

    private static string BuildViewResponse<TRow>(
        JsonObject? arguments,
        AdTrustResult model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool truncated,
        int scanned,
        Action<JsonObject> metaMutate) {
        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: metaMutate);
        return response;
    }

    private static bool IsOneOf(string value, params string[] allowed) {
        for (var i = 0; i < allowed.Length; i++) {
            if (string.Equals(value, allowed[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeEnumValue(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }

        return value.Trim().ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }

    private static string ToFilterStatus(string normalizedStatus) {
        return normalizedStatus switch {
            "active" => "Active",
            "inactive" => "Inactive",
            _ => "Any"
        };
    }

    private static string ToFilterType(string normalizedType) {
        return normalizedType switch {
            "forest" => "Forest",
            "external" => "External",
            "parent_child" => "ParentChild",
            "tree_root" => "TreeRoot",
            _ => "Any"
        };
    }

    private static string ToFilterDirection(string normalizedDirection) {
        return normalizedDirection switch {
            "inbound" => "Inbound",
            "outbound" => "Outbound",
            "bidirectional" => "Bidirectional",
            _ => "Any"
        };
    }
}
