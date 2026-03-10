using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns User Account Control posture details from ComputerX registry-backed policy state (read-only).
/// </summary>
public sealed class SystemUacPostureTool : SystemToolBase, ITool {
    private sealed record UacPostureRequest(
        string? ComputerName,
        string Target);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_uac_posture",
        "Return UAC posture (EnableLUA, secure desktop prompts, installer detection, admin token filtering) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    private sealed record SystemUacPostureResult(
        string ComputerName,
        UacPolicyState Policy,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemUacPostureTool"/> class.
    /// </summary>
    public SystemUacPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<UacPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<UacPostureRequest>.Success(new UacPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<UacPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_uac_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;

        try {
            var policy = UacPolicyQuery.Get(request.ComputerName);
            var warnings = new List<string>();

            if (policy.EnableLUA == false) {
                warnings.Add("User Account Control is disabled.");
            }
            if (policy.PromptOnSecureDesktop == false) {
                warnings.Add("UAC prompts do not use the secure desktop.");
            }
            if (policy.FilterAdministratorToken == false) {
                warnings.Add("Built-in Administrator token filtering is not enabled.");
            }
            if (policy.EnableInstallerDetection == false) {
                warnings.Add("Installer detection is disabled.");
            }

            var model = new SystemUacPostureResult(
                ComputerName: request.Target,
                Policy: policy,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System UAC posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("EnableLUA", policy.EnableLUA?.ToString() ?? string.Empty),
                    ("PromptOnSecureDesktop", policy.PromptOnSecureDesktop?.ToString() ?? string.Empty),
                    ("FilterAdministratorToken", policy.FilterAdministratorToken?.ToString() ?? string.Empty),
                    ("EnableInstallerDetection", policy.EnableInstallerDetection?.ToString() ?? string.Empty),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "UAC posture query failed."));
        }
    }
}
