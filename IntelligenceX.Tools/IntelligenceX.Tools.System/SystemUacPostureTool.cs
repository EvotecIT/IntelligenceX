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
/// Returns User Account Control posture for the local or remote Windows host.
/// </summary>
public sealed class SystemUacPostureTool : SystemToolBase, ITool {
    private sealed record UacPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record UacPostureResponse(
        string ComputerName,
        bool? EnableLua,
        bool? PromptOnSecureDesktop,
        int? ConsentPromptBehaviorAdmin,
        int? ConsentPromptBehaviorUser,
        bool? FilterAdministratorToken,
        bool? EnableInstallerDetection,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_uac_posture",
        "Return User Account Control elevation and consent-prompt posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:uac_posture", "intent:elevation_policy", "scope:host_uac_policy" });

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
            var posture = UacPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new UacPostureResponse(
                ComputerName: effectiveComputerName,
                EnableLua: posture.EnableLUA,
                PromptOnSecureDesktop: posture.PromptOnSecureDesktop,
                ConsentPromptBehaviorAdmin: posture.ConsentPromptBehaviorAdmin,
                ConsentPromptBehaviorUser: posture.ConsentPromptBehaviorUser,
                FilterAdministratorToken: posture.FilterAdministratorToken,
                EnableInstallerDetection: posture.EnableInstallerDetection,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_uac_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "UAC posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Enable LUA", FormatNullableBool(posture.EnableLUA)),
                    ("Prompt On Secure Desktop", FormatNullableBool(posture.PromptOnSecureDesktop)),
                    ("Consent Prompt Behavior Admin", posture.ConsentPromptBehaviorAdmin?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Consent Prompt Behavior User", posture.ConsentPromptBehaviorUser?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Filter Administrator Token", FormatNullableBool(posture.FilterAdministratorToken)),
                    ("Enable Installer Detection", FormatNullableBool(posture.EnableInstallerDetection)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "UAC posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(UacPolicyState posture) {
        var warnings = new List<string>();
        if (posture.EnableLUA == false) {
            warnings.Add("User Account Control is disabled.");
        }
        if (posture.EnableLUA == true && posture.PromptOnSecureDesktop == false) {
            warnings.Add("UAC consent prompts do not use the secure desktop.");
        }
        if (posture.FilterAdministratorToken == false) {
            warnings.Add("Built-in Administrator token filtering is disabled.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
