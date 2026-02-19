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
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolEnumBinders.TryParseOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "profile"),
                ProfileByName,
                "profile",
                out FirewallProfileKind? profile,
                out var profileError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", profileError ?? "Invalid profile value."));
        }

        var enabled = arguments?.GetBoolean("enabled");
        var max = ResolveBoundedOptionLimit(arguments, "max_entries");

        if (!FirewallProfileListQueryExecutor.TryExecute(
                request: new FirewallProfileListQueryRequest {
                    Profile = profile,
                    Enabled = enabled,
                    MaxResults = max
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Firewall profile query failed."));
        }

        var result = queryResult ?? new FirewallProfileListQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Profiles,
            viewRowsPath: "profiles_view",
            title: "Firewall profiles (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                if (profile.HasValue) {
                    meta.Add("profile", ProfileToString(profile.Value));
                }
                if (enabled.HasValue) {
                    meta.Add("enabled", enabled.Value);
                }
            });
        return Task.FromResult(response);
    }

    private static string ProfileToString(FirewallProfileKind profile) {
        return ToolEnumBinders.ToName(profile, ProfileNames);
    }

}

