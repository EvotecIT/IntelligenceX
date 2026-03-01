using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Firewall;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists active Windows Firewall rules (read-only, capped).
/// </summary>
public sealed class SystemFirewallRulesTool : SystemToolBase, ITool {
    private sealed record FirewallRulesRequest(
        string? NameContains,
        string? ApplicationContains,
        FirewallDirection? Direction,
        FirewallAction? Action,
        FirewallProtocol? Protocol,
        FirewallProfileKind? Profile,
        int MaxEntries);

    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, FirewallDirection> DirectionByName =
        new Dictionary<string, FirewallDirection>(StringComparer.OrdinalIgnoreCase) {
            ["inbound"] = FirewallDirection.Inbound,
            ["outbound"] = FirewallDirection.Outbound
        };

    private static readonly IReadOnlyDictionary<FirewallDirection, string> DirectionNames =
        new Dictionary<FirewallDirection, string> {
            [FirewallDirection.Inbound] = "inbound",
            [FirewallDirection.Outbound] = "outbound"
        };

    private static readonly IReadOnlyDictionary<string, FirewallAction> ActionByName =
        new Dictionary<string, FirewallAction>(StringComparer.OrdinalIgnoreCase) {
            ["allow"] = FirewallAction.Allow,
            ["block"] = FirewallAction.Block
        };

    private static readonly IReadOnlyDictionary<FirewallAction, string> ActionNames =
        new Dictionary<FirewallAction, string> {
            [FirewallAction.Allow] = "allow",
            [FirewallAction.Block] = "block"
        };

    private static readonly IReadOnlyDictionary<string, FirewallProtocol> ProtocolByName =
        new Dictionary<string, FirewallProtocol>(StringComparer.OrdinalIgnoreCase) {
            ["tcp"] = FirewallProtocol.Tcp,
            ["udp"] = FirewallProtocol.Udp
        };

    private static readonly IReadOnlyDictionary<FirewallProtocol, string> ProtocolNames =
        new Dictionary<FirewallProtocol, string> {
            [FirewallProtocol.Any] = "any",
            [FirewallProtocol.Tcp] = "tcp",
            [FirewallProtocol.Udp] = "udp"
        };

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
        "system_firewall_rules",
        "List active Windows Firewall rules (read-only, capped).",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive substring match against rule name.")),
                ("application_contains", ToolSchema.String("Optional case-insensitive substring match against application path.")),
                ("direction", ToolSchema.String("Optional direction filter.").Enum("any", "inbound", "outbound")),
                ("action", ToolSchema.String("Optional action filter.").Enum("any", "allow", "block")),
                ("protocol", ToolSchema.String("Optional protocol filter.").Enum("any", "tcp", "udp")),
                ("profile", ToolSchema.String("Optional profile filter.").Enum("any", "domain", "private", "public")),
                ("max_entries", ToolSchema.Integer("Optional maximum rules to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemFirewallRulesTool"/> class.
    /// </summary>
    public SystemFirewallRulesTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<FirewallRulesRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("direction"),
                    DirectionByName,
                    "direction",
                    out FirewallDirection? direction,
                    out var directionError)) {
                return ToolRequestBindingResult<FirewallRulesRequest>.Failure(directionError ?? "Invalid direction value.");
            }
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("action"),
                    ActionByName,
                    "action",
                    out FirewallAction? action,
                    out var actionError)) {
                return ToolRequestBindingResult<FirewallRulesRequest>.Failure(actionError ?? "Invalid action value.");
            }
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("protocol"),
                    ProtocolByName,
                    "protocol",
                    out FirewallProtocol? protocol,
                    out var protocolError)) {
                return ToolRequestBindingResult<FirewallRulesRequest>.Failure(protocolError ?? "Invalid protocol value.");
            }
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("profile"),
                    ProfileByName,
                    "profile",
                    out FirewallProfileKind? profile,
                    out var profileError)) {
                return ToolRequestBindingResult<FirewallRulesRequest>.Failure(profileError ?? "Invalid profile value.");
            }

            return ToolRequestBindingResult<FirewallRulesRequest>.Success(new FirewallRulesRequest(
                NameContains: reader.OptionalString("name_contains"),
                ApplicationContains: reader.OptionalString("application_contains"),
                Direction: direction,
                Action: action,
                Protocol: protocol,
                Profile: profile,
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<FirewallRulesRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!FirewallRuleListQueryExecutor.TryExecute(
                request: new FirewallRuleListQueryRequest {
                    NameContains = request.NameContains,
                    ApplicationContains = request.ApplicationContains,
                    Direction = request.Direction,
                    Action = request.Action,
                    Protocol = request.Protocol,
                    Profile = request.Profile,
                    MaxResults = request.MaxEntries
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Firewall rule query failed."));
        }

        var result = queryResult ?? new FirewallRuleListQueryResult();
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Rules,
            viewRowsPath: "rules_view",
            title: "Firewall rules (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                if (!string.IsNullOrWhiteSpace(request.ApplicationContains)) {
                    meta.Add("application_contains", request.ApplicationContains);
                }
                if (request.Direction.HasValue) {
                    meta.Add("direction", ToolEnumBinders.ToName(request.Direction.Value, DirectionNames));
                }
                if (request.Action.HasValue) {
                    meta.Add("action", ToolEnumBinders.ToName(request.Action.Value, ActionNames));
                }
                if (request.Protocol.HasValue) {
                    meta.Add("protocol", ToolEnumBinders.ToName(request.Protocol.Value, ProtocolNames));
                }
                if (request.Profile.HasValue) {
                    meta.Add("profile", ProfileToString(request.Profile.Value));
                }
            });
        return Task.FromResult(response);
    }

    private static string ProfileToString(FirewallProfileKind profile) {
        if (ProfileNames.TryGetValue(profile, out var direct)) {
            return direct;
        }

        var parts = new List<string>(3);
        if ((profile & FirewallProfileKind.Domain) != 0) {
            parts.Add("domain");
        }
        if ((profile & FirewallProfileKind.Private) != 0) {
            parts.Add("private");
        }
        if ((profile & FirewallProfileKind.Public) != 0) {
            parts.Add("public");
        }

        return parts.Count == 0
            ? ((int)profile).ToString(CultureInfo.InvariantCulture)
            : string.Join("|", parts);
    }
}
