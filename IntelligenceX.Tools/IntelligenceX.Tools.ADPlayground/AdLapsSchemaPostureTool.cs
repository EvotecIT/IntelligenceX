using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.DirectoryServices;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns LAPS schema posture for legacy and modern LAPS attributes (read-only).
/// </summary>
public sealed class AdLapsSchemaPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_laps_schema_posture",
        "Check legacy and Windows LAPS schema posture (ms-Mcs-AdmPwd searchFlags + msLAPS-* attribute presence) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("only_findings", ToolSchema.Boolean("When true, return only domains with at least one schema posture finding.")),
                ("include_details", ToolSchema.Boolean("When true, include schema DN and attribute DN detail rows.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record LapsSchemaPostureRow(
        string DomainName,
        bool LegacyAttributeFound,
        int? LegacySearchFlags,
        bool LegacyConfidentialBitSet,
        bool LegacyRisk,
        bool ModernEncryptedPasswordPresent,
        bool ModernPasswordExpirationTimePresent,
        bool ModernEncryptedDsrmPasswordPresent,
        bool ModernDsrmPasswordExpirationTimePresent,
        bool ModernSchemaComplete,
        bool AnyFinding);

    private sealed record LapsSchemaPostureDetail(
        string DomainName,
        string? LegacySchemaDn,
        string? LegacyAttributeDn,
        string? ModernSchemaDn);

    private sealed record LapsSchemaPostureError(
        string Domain,
        string Message);

    private sealed record AdLapsSchemaPostureResult(
        string? DomainName,
        string? ForestName,
        bool OnlyFindings,
        bool IncludeDetails,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<LapsSchemaPostureError> Errors,
        IReadOnlyList<LapsSchemaPostureRow> Rows,
        IReadOnlyList<LapsSchemaPostureDetail> Details);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLapsSchemaPostureTool"/> class.
    /// </summary>
    public AdLapsSchemaPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var onlyFindings = ToolArgs.GetBoolean(arguments, "only_findings", defaultValue: false);
        var includeDetails = ToolArgs.GetBoolean(arguments, "include_details", defaultValue: false);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "LAPS schema posture",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<LapsSchemaPostureRow>(targetDomains.Length);
        var details = new List<LapsSchemaPostureDetail>(targetDomains.Length);
        var errors = new List<LapsSchemaPostureError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var legacy = LapsLegacySearchFlagsService.Evaluate(domain);
                var modern = LapsModernSchemaService.Evaluate(domain);

                var legacyRisk = legacy.AttributeFound && !legacy.ConfidentialBitSet;
                var modernComplete =
                    modern.EncryptedPasswordPresent &&
                    modern.PasswordExpirationTimePresent &&
                    modern.EncryptedDsrmPasswordPresent &&
                    modern.DsrmPasswordExpirationTimePresent;
                var anyFinding = legacyRisk || !modernComplete;

                rows.Add(new LapsSchemaPostureRow(
                    DomainName: domain,
                    LegacyAttributeFound: legacy.AttributeFound,
                    LegacySearchFlags: legacy.SearchFlags,
                    LegacyConfidentialBitSet: legacy.ConfidentialBitSet,
                    LegacyRisk: legacyRisk,
                    ModernEncryptedPasswordPresent: modern.EncryptedPasswordPresent,
                    ModernPasswordExpirationTimePresent: modern.PasswordExpirationTimePresent,
                    ModernEncryptedDsrmPasswordPresent: modern.EncryptedDsrmPasswordPresent,
                    ModernDsrmPasswordExpirationTimePresent: modern.DsrmPasswordExpirationTimePresent,
                    ModernSchemaComplete: modernComplete,
                    AnyFinding: anyFinding));

                if (includeDetails) {
                    details.Add(new LapsSchemaPostureDetail(
                        DomainName: domain,
                        LegacySchemaDn: legacy.SchemaDn,
                        LegacyAttributeDn: legacy.AttributeDn,
                        ModernSchemaDn: modern.SchemaDn));
                }
            },
            errorFactory: (domain, ex) => new LapsSchemaPostureError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !onlyFindings || row.AnyFinding)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdLapsSchemaPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            OnlyFindings: onlyFindings,
            IncludeDetails: includeDetails,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            Details: projectedDetails);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: LAPS Schema Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("only_findings", onlyFindings);
                meta.Add("include_details", includeDetails);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}

