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
/// Returns Defender ASR and cloud policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdDefenderAsrPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private static readonly IReadOnlyList<string> AdditionalUnconfiguredAttributionValues = new[] { "Off" };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_defender_asr_policy",
        "Assess Defender ASR and cloud policy posture (including key hardening toggles) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdDefenderAsrPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        object Asr,
        object Cloud,
        IReadOnlyList<object> AsrEntriesFriendly,
        object CloudFriendly,
        string? AttributionTopWriters,
        string? NetworkProtectionMode,
        bool? NetworkProtectionNotOff,
        string? PuaProtectionMode,
        bool? PuaNotDisabled,
        bool? RtpRealtimeEnabled,
        bool? RtpBehaviorEnabled,
        bool? RtpIoavEnabled,
        bool? RtpScriptEnabled,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDefenderAsrPolicyTool"/> class.
    /// </summary>
    public AdDefenderAsrPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var includeAttribution = ToolArgs.GetBoolean(arguments, "include_attribution", defaultValue: true);
        var configuredAttributionOnly = ToolArgs.GetBoolean(arguments, "configured_attribution_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (!TryExecute(
                action: () => DefenderAsrPolicyService.Get(domainName),
                result: out DefenderAsrPolicyService.View view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Defender ASR policy query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var rows = PreparePolicyAttributionRows(
            attribution: view.Attribution,
            includeAttribution: includeAttribution,
            configuredAttributionOnly: configuredAttributionOnly,
            maxResults: maxResults,
            additionalUnconfiguredValues: AdditionalUnconfiguredAttributionValues,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdDefenderAsrPolicyResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            Asr: view.Asr,
            Cloud: view.Cloud,
            AsrEntriesFriendly: view.AsrEntriesFriendly,
            CloudFriendly: view.CloudFriendly,
            AttributionTopWriters: view.AttributionTopWriters,
            NetworkProtectionMode: view.NetworkProtectionMode,
            NetworkProtectionNotOff: view.NetworkProtectionNotOff,
            PuaProtectionMode: view.PuaProtectionMode,
            PuaNotDisabled: view.PuaNotDisabled,
            RtpRealtimeEnabled: view.RtpRealtimeEnabled,
            RtpBehaviorEnabled: view.RtpBehaviorEnabled,
            RtpIoavEnabled: view.RtpIoavEnabled,
            RtpScriptEnabled: view.RtpScriptEnabled,
            Attribution: rows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Defender ASR Policy (preview)",
            baseTruncated: truncated,
            scanned: scanned,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                AddStandardPolicyAttributionMeta(meta, domainName, includeAttribution, configuredAttributionOnly, maxResults);
            });
        return Task.FromResult(response);
    }
}

