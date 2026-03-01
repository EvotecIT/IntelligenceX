using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.DirectoryOps;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Finds potentially stale AD user/computer accounts using lastLogonTimestamp and pwdLastSet (read-only).
/// </summary>
public sealed class AdStaleAccountsTool : ActiveDirectoryToolBase, ITool {
    private sealed record StaleAccountsRequest(
        LdapToolObjectKind Kind,
        bool EnabledOnly,
        bool ExcludeCritical,
        int? DaysSinceLogon,
        int? DaysSincePasswordSet,
        CriteriaMatch Match);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_stale_accounts",
        "Find stale AD users/computers based on lastLogonTimestamp and/or pwdLastSet (read-only).",
        ToolSchema.Object(
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "computer")),
                ("enabled_only", ToolSchema.Boolean("When true, exclude disabled accounts. Default false.")),
                ("exclude_critical", ToolSchema.Boolean("When true, exclude critical system objects. Default true.")),
                ("days_since_logon", ToolSchema.Integer("If set (>0), match accounts with lastLogonTimestamp older than this many days (or missing).")),
                ("days_since_password_set", ToolSchema.Integer("If set (>0), match accounts with pwdLastSet older than this many days (or missing/0).")),
                ("match", ToolSchema.String("How to combine logon/password criteria when both are specified.").Enum("any", "all")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdStaleAccountsTool"/> class.
    /// </summary>
    public AdStaleAccountsTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<StaleAccountsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var daysSinceLogon = ToolArgs.ToPositiveInt32OrNull(reader.OptionalInt64("days_since_logon"));
            var daysSincePwd = ToolArgs.ToPositiveInt32OrNull(reader.OptionalInt64("days_since_password_set"));
            if (!daysSinceLogon.HasValue && !daysSincePwd.HasValue) {
                return ToolRequestBindingResult<StaleAccountsRequest>.Failure(
                    "At least one of days_since_logon or days_since_password_set must be provided (>0).");
            }

            return ToolRequestBindingResult<StaleAccountsRequest>.Success(new StaleAccountsRequest(
                Kind: LdapToolKinds.ParseObjectKind(reader.OptionalString("kind")),
                EnabledOnly: reader.Boolean("enabled_only"),
                ExcludeCritical: reader.Boolean("exclude_critical", defaultValue: true),
                DaysSinceLogon: daysSinceLogon,
                DaysSincePasswordSet: daysSincePwd,
                Match: ParseMatch(reader.OptionalString("match"))));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<StaleAccountsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var maxResults = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        var opts = new StaleAccountsQueryOptions {
            Kind = request.Kind switch {
                LdapToolObjectKind.User => DirectoryAccountKind.User,
                LdapToolObjectKind.Computer => DirectoryAccountKind.Computer,
                _ => DirectoryAccountKind.Any
            },
            EnabledOnly = request.EnabledOnly,
            ExcludeCritical = request.ExcludeCritical,
            DaysSinceLogon = request.DaysSinceLogon,
            DaysSincePasswordSet = request.DaysSincePasswordSet,
            Match = request.Match,
            ServerOrDomain = dc,
            BaseDn = baseDn!,
            ReferenceUtc = DateTime.UtcNow,
            MaxResults = maxResults
        };

        var res = StaleAccountsService.Query(opts);
        return Task.FromResult(ToolResultV2.OkModel(res));
    }

    private static CriteriaMatch ParseMatch(string? value) {
        return string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)
            ? CriteriaMatch.All
            : CriteriaMatch.Any;
    }
}
