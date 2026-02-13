using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.DirectoryOps;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Lists Active Directory user accounts whose accountExpires timestamp is in the past (read-only).
/// </summary>
public sealed class AdUsersExpiredTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_users_expired",
        "List Active Directory user accounts that are currently expired (accountExpires in the past) (read-only).",
        ToolSchema.Object(
                ("reference_time_utc", ToolSchema.String("Optional ISO-8601 UTC time used as 'now' for comparison (default: current time).")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdUsersExpiredTool"/> class.
    /// </summary>
    public AdUsersExpiredTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolTime.TryParseUtcOptional(ToolArgs.GetOptionalTrimmed(arguments, "reference_time_utc"), out var referenceUtcOpt, out var refErr)) {
            return Task.FromResult(Error("invalid_argument", $"reference_time_utc: {refErr}"));
        }
        var referenceUtc = referenceUtcOpt ?? DateTime.UtcNow;

        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);
        if (string.IsNullOrWhiteSpace(baseDn)) {
            return Task.FromResult(Error(
                errorCode: "not_configured",
                error: "search_base_dn could not be resolved (RootDSE defaultNamingContext missing).",
                hints: new[] {
                    "Call ad_environment_discover first to resolve effective domain_controller and search_base_dn.",
                    "If discovery fails, pass domain_controller and search_base_dn explicitly."
                },
                isTransient: false));
        }

        var res = ExpiredUsersService.Query(baseDn, dc, referenceUtc, maxResults);

        return Task.FromResult(ToolResponse.OkModel(res));
    }
}
