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
/// Returns effective account and lockout policy posture for the local or remote Windows host.
/// </summary>
public sealed class SystemAccountPolicyPostureTool : SystemToolBase, ITool {
    private sealed record AccountPolicyPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record AccountPolicyPostureResponse(
        string ComputerName,
        int? MinPasswordLength,
        int? MinPasswordAgeDays,
        int? MaxPasswordAgeDays,
        int? PasswordHistorySize,
        int? LockoutThreshold,
        int? LockoutDurationMinutes,
        int? LockoutResetMinutes,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_account_policy_posture",
        "Return effective password and lockout policy posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:account_policy_posture", "intent:password_lockout_policy", "scope:host_account_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAccountPolicyPostureTool"/> class.
    /// </summary>
    public SystemAccountPolicyPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<AccountPolicyPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<AccountPolicyPostureRequest>.Success(new AccountPolicyPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<AccountPolicyPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_account_policy_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = AccountPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new AccountPolicyPostureResponse(
                ComputerName: effectiveComputerName,
                MinPasswordLength: posture.MinPasswordLength,
                MinPasswordAgeDays: posture.MinPasswordAgeDays,
                MaxPasswordAgeDays: posture.MaxPasswordAgeDays,
                PasswordHistorySize: posture.PasswordHistorySize,
                LockoutThreshold: posture.LockoutThreshold,
                LockoutDurationMinutes: posture.LockoutDurationMinutes,
                LockoutResetMinutes: posture.LockoutResetMinutes,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_account_policy_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Account policy posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Minimum Password Length", posture.MinPasswordLength?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Minimum Password Age Days", posture.MinPasswordAgeDays?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Maximum Password Age Days", posture.MaxPasswordAgeDays?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Password History Size", posture.PasswordHistorySize?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Lockout Threshold", posture.LockoutThreshold?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Lockout Duration Minutes", posture.LockoutDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Account policy posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(AccountPolicyState posture) {
        var warnings = new List<string>();
        if (posture.MinPasswordLength.HasValue && posture.MinPasswordLength.Value < 12) {
            warnings.Add("Minimum password length is below 12 characters.");
        }
        if (posture.PasswordHistorySize.HasValue && posture.PasswordHistorySize.Value < 12) {
            warnings.Add("Password history size is below 12 remembered passwords.");
        }
        if (posture.LockoutThreshold.HasValue && posture.LockoutThreshold.Value == 0) {
            warnings.Add("Account lockout threshold is disabled.");
        }
        if (posture.LockoutThreshold.HasValue
            && posture.LockoutThreshold.Value > 0
            && posture.LockoutThreshold.Value > 10) {
            warnings.Add("Account lockout threshold is permissive.");
        }

        return warnings;
    }
}
