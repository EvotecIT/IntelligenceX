using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns built-in Administrator and Guest account posture for the local or remote Windows host.
/// </summary>
public sealed class SystemBuiltinAccountsTool : SystemToolBase, ITool {
    private sealed record BuiltinAccountsRequest(
        string? ComputerName,
        string Target);

    private sealed record BuiltinAccountsResponse(
        string ComputerName,
        string? AdministratorName,
        bool? AdministratorEnabled,
        string? GuestName,
        bool? GuestEnabled,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_builtin_accounts",
        "Return built-in Administrator and Guest account posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:builtin_accounts", "intent:local_admin_guest_posture", "scope:host_local_accounts" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBuiltinAccountsTool"/> class.
    /// </summary>
    public SystemBuiltinAccountsTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<BuiltinAccountsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<BuiltinAccountsRequest>.Success(new BuiltinAccountsRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<BuiltinAccountsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_builtin_accounts");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = BuiltinAccountsQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new BuiltinAccountsResponse(
                ComputerName: effectiveComputerName,
                AdministratorName: posture.AdministratorName,
                AdministratorEnabled: posture.AdministratorEnabled,
                GuestName: posture.GuestName,
                GuestEnabled: posture.GuestEnabled,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_builtin_accounts",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Built-in accounts posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Administrator Name", posture.AdministratorName ?? "unknown"),
                    ("Administrator Enabled", FormatNullableBool(posture.AdministratorEnabled)),
                    ("Guest Name", posture.GuestName ?? "unknown"),
                    ("Guest Enabled", FormatNullableBool(posture.GuestEnabled)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Built-in accounts query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(BuiltinAccountsState posture) {
        var warnings = new List<string>();
        if (posture.AdministratorEnabled == true) {
            warnings.Add("The built-in Administrator account is enabled.");
        }
        if (posture.GuestEnabled == true) {
            warnings.Add("The built-in Guest account is enabled.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
