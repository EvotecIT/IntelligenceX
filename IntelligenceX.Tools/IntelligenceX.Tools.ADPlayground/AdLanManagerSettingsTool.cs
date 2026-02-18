using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Domains;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns LAN Manager and LM hash posture across selected domain controllers (read-only).
/// </summary>
public sealed class AdLanManagerSettingsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_lan_manager_settings",
        "Check DC LAN Manager posture (NoLMHash and LmCompatibilityLevel) across one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional explicit DC list. When set, skips domain/forest discovery.")),
                ("allow_lm_hash_only", ToolSchema.Boolean("When true, return only rows where LM hash storage is still allowed.")),
                ("legacy_ntlm_only", ToolSchema.Boolean("When true, return only rows where LmCompatibilityLevel is below 5.")),
                ("max_domain_controllers", ToolSchema.Integer("Maximum discovered DCs to evaluate (capped). Default 200.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record LanManagerSettingsRow(
        string DomainName,
        string DomainController,
        bool NoLmHash,
        int? LmCompatibilityLevel,
        bool AllowsLmHash,
        bool LegacyNtlmAllowed);

    private sealed record LanManagerSettingsError(
        string Scope,
        string Message);

    private sealed record AdLanManagerSettingsResult(
        string? DomainName,
        string? ForestName,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        bool AllowLmHashOnly,
        bool LegacyNtlmOnly,
        IReadOnlyList<LanManagerSettingsError> Errors,
        IReadOnlyList<LanManagerSettingsRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLanManagerSettingsTool"/> class.
    /// </summary>
    public AdLanManagerSettingsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var explicitDomainControllers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("domain_controllers"));
        var allowLmHashOnly = ToolArgs.GetBoolean(arguments, "allow_lm_hash_only", defaultValue: false);
        var legacyNtlmOnly = ToolArgs.GetBoolean(arguments, "legacy_ntlm_only", defaultValue: false);
        var maxDomainControllers = ToolArgs.GetCappedInt32(arguments, "max_domain_controllers", 200, 1, 2000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var rows = new List<LanManagerSettingsRow>(maxDomainControllers);
        var errors = new List<LanManagerSettingsError>();

        if (explicitDomainControllers.Count > 0) {
            foreach (var dc in explicitDomainControllers.Take(maxDomainControllers)) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    var status = await LanManagerSettingsService.GetStatusForControllerAsync(dc, cancellationToken).ConfigureAwait(false);
                    if (status is null) {
                        errors.Add(new LanManagerSettingsError(dc, "Controller status was unavailable."));
                        continue;
                    }

                    rows.Add(new LanManagerSettingsRow(
                        DomainName: domainName ?? string.Empty,
                        DomainController: status.DomainController,
                        NoLmHash: status.NoLmHash,
                        LmCompatibilityLevel: status.LmCompatibilityLevel,
                        AllowsLmHash: !status.NoLmHash,
                        LegacyNtlmAllowed: status.LmCompatibilityLevel.GetValueOrDefault() < 5));
                } catch (Exception ex) {
                    errors.Add(new LanManagerSettingsError(dc, ex.Message));
                }
            }
        } else {
            var targetDomains = string.IsNullOrWhiteSpace(domainName)
                ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(static x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : new[] { domainName! };

            if (targetDomains.Length == 0) {
                return ToolResponse.Error(
                    "query_failed",
                    "No domains resolved for LAN Manager settings query. Provide domain_name/domain_controllers or ensure forest discovery is available.");
            }

            foreach (var domain in targetDomains) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    var snapshot = await LanManagerSettingsService.GetSnapshotAsync(domain, cancellationToken).ConfigureAwait(false);
                    foreach (var status in snapshot.DomainControllers) {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add(new LanManagerSettingsRow(
                            DomainName: domain,
                            DomainController: status.DomainController,
                            NoLmHash: status.NoLmHash,
                            LmCompatibilityLevel: status.LmCompatibilityLevel,
                            AllowsLmHash: !status.NoLmHash,
                            LegacyNtlmAllowed: status.LmCompatibilityLevel.GetValueOrDefault() < 5));
                        if (rows.Count >= maxDomainControllers) {
                            break;
                        }
                    }
                    if (rows.Count >= maxDomainControllers) {
                        break;
                    }
                } catch (Exception ex) {
                    errors.Add(new LanManagerSettingsError(domain, ex.Message));
                }
            }
        }

        var filtered = rows
            .Where(row => !allowLmHashOnly || row.AllowsLmHash)
            .Where(row => !legacyNtlmOnly || row.LegacyNtlmAllowed)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<LanManagerSettingsRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdLanManagerSettingsResult(
            DomainName: domainName,
            ForestName: forestName,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            AllowLmHashOnly: allowLmHashOnly,
            LegacyNtlmOnly: legacyNtlmOnly,
            Errors: errors,
            Rows: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: LAN Manager Settings (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("allow_lm_hash_only", allowLmHashOnly);
                meta.Add("legacy_ntlm_only", legacyNtlmOnly);
                meta.Add("max_domain_controllers", maxDomainControllers);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
                if (explicitDomainControllers.Count > 0) {
                    meta.Add("explicit_domain_controllers", explicitDomainControllers.Count);
                }
            });
        return response;
    }
}
