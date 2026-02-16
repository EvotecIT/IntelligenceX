using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Groups;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Summarizes well-known privileged groups and (optionally) their memberships (read-only).
/// </summary>
public sealed class AdPrivilegedGroupsSummaryTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMembersSampleSize = 20;
    private const int MaxMembersSampleSize = 200;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_privileged_groups_summary",
        "Summarize well-known privileged AD groups with optional member counts/samples (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. If omitted, uses current domain.")),
                ("include_member_count", ToolSchema.Boolean("When true, include member_count (effective nested membership). Default true.")),
                ("include_member_sample", ToolSchema.Boolean("When true, include a sample of members (DN if available, else name/SID). Default false.")),
                ("member_sample_size", ToolSchema.Integer("Maximum number of member labels to include when include_member_sample=true (capped). Default 20.")),
                ("search_base_dn", ToolSchema.String("Optional base DN hint used to derive domain_name (engine-first; RootDSE avoided when possible).")),
                ("domain_controller", ToolSchema.String("Optional domain controller hint (engine chooses best DC).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPrivilegedGroupsSummaryTool"/> class.
    /// </summary>
    public AdPrivilegedGroupsSummaryTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var includeMemberCount = arguments?.GetBoolean("include_member_count") ?? true;
        var includeMemberSample = arguments?.GetBoolean("include_member_sample") ?? false;

        var requestedSampleSize = arguments?.GetInt64("member_sample_size");
        var memberSampleSize = requestedSampleSize.HasValue && requestedSampleSize.Value > 0
            ? (int)Math.Min(requestedSampleSize.Value, MaxMembersSampleSize)
            : DefaultMembersSampleSize;

        var summary = await PrivilegedGroupsSummaryService
            .QueryAsync(
                new PrivilegedGroupsSummaryQueryOptions {
                    DomainControllerHint = arguments?.GetString("domain_controller") ?? Options.DomainController,
                    SearchBaseDnHint = arguments?.GetString("search_base_dn") ?? Options.DefaultSearchBaseDn,
                    DomainName = arguments?.GetString("domain_name"),
                    IncludeMemberCount = includeMemberCount,
                    IncludeMemberSample = includeMemberSample,
                    MemberSampleSize = memberSampleSize
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ToolResponse.OkModel(summary);
    }
}
