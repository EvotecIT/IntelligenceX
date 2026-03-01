using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Firewall;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists Windows Firewall profile settings (read-only, capped).
/// </summary>
public sealed class SystemFirewallProfilesTool : SystemToolBase, ITool {
    private sealed record FirewallProfilesRequest(
        FirewallProfileKind? Profile,
        bool? Enabled,
        int MaxEntries);

    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, FirewallProfileKind> ProfileByName =
        new Dictionary<string, FirewallProfileKind>(StringComparer.OrdinalIgnoreCase) {
            ["domain"] = FirewallProfileKind.Domain,
            ["private"] = FirewallProfileKind.Private,
            ["public"] = FirewallProfileKind.Public
        };

    private static readonly IReadOnlyDictionary<FirewallProfileKind, string> ProfileNames =
        new Dictionary<FirewallProfileKind, string> {
            [FirewallProfileKind.Domain] = "domain",
            [FirewallProfileKind.Private] = "private",
            [FirewallProfileKind.Public] = "public",
            [FirewallProfileKind.All] = "all"
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_firewall_profiles",
        "List Windows Firewall profile settings (read-only, capped).",
        ToolSchema.Object(
                ("profile", ToolSchema.String("Optional profile filter.").Enum("any", "domain", "private", "public")),
                ("enabled", ToolSchema.Boolean("Optional enabled-state filter.")),
                ("max_entries", ToolSchema.Integer("Optional maximum profiles to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemFirewallProfilesTool"/> class.
    /// </summary>
    public SystemFirewallProfilesTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<FirewallProfilesRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("profile"),
                    ProfileByName,
                    "profile",
                    out FirewallProfileKind? profile,
                    out var profileError)) {
                return ToolRequestBindingResult<FirewallProfilesRequest>.Failure(profileError ?? "Invalid profile value.");
            }

            // Keep compatibility with prior behavior: null when arguments object is absent,
            // otherwise missing key resolves to false via JsonObject.GetBoolean default.
            bool? enabled = arguments is null ? null : reader.Boolean("enabled");
            return ToolRequestBindingResult<FirewallProfilesRequest>.Success(new FirewallProfilesRequest(
                Profile: profile,
                Enabled: enabled,
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<FirewallProfilesRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!FirewallProfileListQueryExecutor.TryExecute(
                request: new FirewallProfileListQueryRequest {
                    Profile = request.Profile,
                    Enabled = request.Enabled,
                    MaxResults = request.MaxEntries
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Firewall profile query failed."));
        }

        var result = queryResult ?? new FirewallProfileListQueryResult();
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Profiles,
            viewRowsPath: "profiles_view",
            title: "Firewall profiles (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                if (request.Profile.HasValue) {
                    meta.Add("profile", ProfileToString(request.Profile.Value));
                }
                if (request.Enabled.HasValue) {
                    meta.Add("enabled", request.Enabled.Value);
                }
            });
        return Task.FromResult(response);
    }

    private static string ProfileToString(FirewallProfileKind profile) {
        return ToolEnumBinders.ToName(profile, ProfileNames);
    }

}
