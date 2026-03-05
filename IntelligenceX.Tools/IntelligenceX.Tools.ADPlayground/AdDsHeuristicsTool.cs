using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.DirectoryServices;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns dsHeuristics posture for a forest (read-only).
/// </summary>
public sealed class AdDsHeuristicsTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxPositionRows = 64;
    private const int MaxPositionRowsCap = 2048;
    private const int MaxViewTop = 5000;

    internal readonly record struct DsHeuristicsBindingContract(
        string? ForestName,
        bool IncludePositions,
        bool NonDefaultOnly,
        int MaxPositionRows);

    private sealed record DsHeuristicsRequest(
        string? ForestName,
        bool IncludePositions,
        bool NonDefaultOnly,
        int MaxPositionRows);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ds_heuristics",
        "Read and decode dsHeuristics from CN=Directory Service in the Configuration partition (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest root DNS name. When omitted, uses current forest context.")),
                ("include_positions", ToolSchema.Boolean("When true, include per-position decoded characters.")),
                ("non_default_only", ToolSchema.Boolean("When true and include_positions=true, return only positions where value is not '0'.")),
                ("max_position_rows", ToolSchema.Integer("Maximum position rows when include_positions=true. Default 64.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DsHeuristicsSummaryRow(
        string ForestName,
        string ConfigurationNamingContext,
        string DistinguishedName,
        string Raw,
        int Length,
        bool AnonymousLdapEnabled,
        bool AllowAnonNspiEnabled,
        bool DoNotVerifyUniqueness,
        int NonDefaultPositionCount,
        bool AnyRiskFlag);

    private sealed record DsHeuristicsPositionRow(
        int Position,
        string Value,
        bool IsDefault);

    private sealed record AdDsHeuristicsResult(
        string? ForestName,
        bool IncludePositions,
        bool NonDefaultOnly,
        int MaxPositionRows,
        IReadOnlyList<DsHeuristicsSummaryRow> Rows,
        IReadOnlyList<DsHeuristicsPositionRow> PositionRows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDsHeuristicsTool"/> class.
    /// </summary>
    public AdDsHeuristicsTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<DsHeuristicsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader =>
            ToolRequestBindingResult<DsHeuristicsRequest>.Success(new DsHeuristicsRequest(
                ForestName: reader.OptionalString("forest_name"),
                IncludePositions: reader.Boolean("include_positions"),
                NonDefaultOnly: reader.Boolean("non_default_only"),
                MaxPositionRows: reader.CappedInt32("max_position_rows", DefaultMaxPositionRows, 1, MaxPositionRowsCap))));
    }

    internal static ToolRequestBindingResult<DsHeuristicsBindingContract> BindRequestContract(JsonObject? arguments) {
        var binding = BindRequest(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return ToolRequestBindingResult<DsHeuristicsBindingContract>.Failure(
                binding.Error,
                binding.ErrorCode,
                binding.Hints,
                binding.IsTransient);
        }

        var request = binding.Request;
        return ToolRequestBindingResult<DsHeuristicsBindingContract>.Success(new DsHeuristicsBindingContract(
            ForestName: request.ForestName,
            IncludePositions: request.IncludePositions,
            NonDefaultOnly: request.NonDefaultOnly,
            MaxPositionRows: request.MaxPositionRows));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DsHeuristicsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var forestName = request.ForestName;
        var includePositions = request.IncludePositions;
        var nonDefaultOnly = request.NonDefaultOnly;
        var maxPositionRows = request.MaxPositionRows;

        DsHeuristicsSnapshot snapshot;
        DsHeuristicsDetails details;
        try {
            snapshot = DsHeuristicsService.GetSnapshot(forestName);
            details = DsHeuristicsService.Decode(snapshot.Raw);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "dsHeuristics query failed."));
        }

        var summary = new DsHeuristicsSummaryRow(
            ForestName: snapshot.ForestName,
            ConfigurationNamingContext: snapshot.ConfigurationNamingContext,
            DistinguishedName: snapshot.DistinguishedName,
            Raw: snapshot.Raw ?? string.Empty,
            Length: details.Length,
            AnonymousLdapEnabled: details.AnonymousLdapEnabled,
            AllowAnonNspiEnabled: details.AllowAnonNspiEnabled,
            DoNotVerifyUniqueness: details.DoNotVerifyUniqueness,
            NonDefaultPositionCount: details.NonDefaultPositions.Count,
            AnyRiskFlag: details.AnonymousLdapEnabled || details.AllowAnonNspiEnabled || details.DoNotVerifyUniqueness);

        IReadOnlyList<DsHeuristicsPositionRow> positions = Array.Empty<DsHeuristicsPositionRow>();
        if (includePositions) {
            positions = details.Positions
                .OrderBy(static kv => kv.Key)
                .Where(kv => !nonDefaultOnly || kv.Value != '0')
                .Take(maxPositionRows)
                .Select(static kv => new DsHeuristicsPositionRow(
                    Position: kv.Key,
                    Value: kv.Value.ToString(),
                    IsDefault: kv.Value == '0'))
                .ToArray();
        }

        var result = new AdDsHeuristicsResult(
            ForestName: forestName,
            IncludePositions: includePositions,
            NonDefaultOnly: nonDefaultOnly,
            MaxPositionRows: maxPositionRows,
            Rows: new[] { summary },
            PositionRows: positions);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: dsHeuristics (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            scanned: result.Rows.Count,
            metaMutate: meta => {
                meta.Add("include_positions", includePositions);
                meta.Add("non_default_only", nonDefaultOnly);
                meta.Add("max_position_rows", maxPositionRows);
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            }));
    }
}
