using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.DirectoryOps;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists Active Directory user accounts whose accountExpires timestamp is in the past (read-only).
/// </summary>
public sealed class AdUsersExpiredTool : ActiveDirectoryToolBase, ITool {
    private sealed record UsersExpiredRequest(DateTime ReferenceUtc);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<UsersExpiredRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!ToolTime.TryParseUtcOptional(reader.OptionalString("reference_time_utc"), out var referenceUtcOpt, out var refErr)) {
                return ToolRequestBindingResult<UsersExpiredRequest>.Failure($"reference_time_utc: {refErr}");
            }

            return ToolRequestBindingResult<UsersExpiredRequest>.Success(new UsersExpiredRequest(
                ReferenceUtc: referenceUtcOpt ?? DateTime.UtcNow));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<UsersExpiredRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var maxResults = ResolveMaxResults(context.Arguments);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);
        if (string.IsNullOrWhiteSpace(baseDn)) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "not_configured",
                error: "search_base_dn could not be resolved (RootDSE defaultNamingContext missing).",
                hints: new[] {
                    "Call ad_environment_discover first to resolve effective domain_controller and search_base_dn.",
                    "If discovery fails, pass domain_controller and search_base_dn explicitly."
                },
                isTransient: false));
        }

        var res = ExpiredUsersService.Query(baseDn, dc, context.Request.ReferenceUtc, maxResults);

        return Task.FromResult(ToolResultV2.OkModel(res));
    }
}
