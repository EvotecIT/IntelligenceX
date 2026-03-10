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
/// Returns interactive logon policy posture for the local or remote Windows host.
/// </summary>
public sealed class SystemInteractiveLogonPostureTool : SystemToolBase, ITool {
    private sealed record InteractiveLogonPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record InteractiveLogonPostureResponse(
        string ComputerName,
        bool LegalNoticeConfigured,
        int? InactivityTimeoutSecs,
        bool? DisableCad,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_interactive_logon_posture",
        "Return interactive logon notice, inactivity timeout, and Ctrl+Alt+Del posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:interactive_logon_posture", "intent:console_logon_policy", "scope:host_logon_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInteractiveLogonPostureTool"/> class.
    /// </summary>
    public SystemInteractiveLogonPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<InteractiveLogonPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<InteractiveLogonPostureRequest>.Success(new InteractiveLogonPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<InteractiveLogonPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_interactive_logon_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = InteractiveLogonPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var legalNoticeConfigured = !string.IsNullOrWhiteSpace(posture.LegalNoticeCaption)
                || !string.IsNullOrWhiteSpace(posture.LegalNoticeText);
            var model = new InteractiveLogonPostureResponse(
                ComputerName: effectiveComputerName,
                LegalNoticeConfigured: legalNoticeConfigured,
                InactivityTimeoutSecs: posture.InactivityTimeoutSecs,
                DisableCad: posture.DisableCAD,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_interactive_logon_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Interactive logon posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Legal Notice Configured", legalNoticeConfigured ? "true" : "false"),
                    ("Inactivity Timeout Seconds", posture.InactivityTimeoutSecs?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Disable CAD", FormatNullableBool(posture.DisableCAD)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Interactive logon posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(InteractiveLogonPolicyState posture) {
        var warnings = new List<string>();
        if (posture.DisableCAD == true) {
            warnings.Add("Ctrl+Alt+Del requirement is disabled.");
        }
        if (!string.IsNullOrWhiteSpace(posture.LegalNoticeCaption) ^ !string.IsNullOrWhiteSpace(posture.LegalNoticeText)) {
            warnings.Add("Legal notice is only partially configured.");
        }
        if (!posture.InactivityTimeoutSecs.HasValue || posture.InactivityTimeoutSecs.Value <= 0) {
            warnings.Add("Interactive logon inactivity timeout is not configured.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
