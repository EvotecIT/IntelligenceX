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
/// Returns interactive logon posture details from ComputerX registry-backed policy state (read-only).
/// </summary>
public sealed class SystemInteractiveLogonPostureTool : SystemToolBase, ITool {
    private sealed record InteractiveLogonPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeLegalNoticeText);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_interactive_logon_posture",
        "Return interactive logon posture (DisableCAD, inactivity timeout, optional legal notice fields) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_legal_notice_text", ToolSchema.Boolean("When true, include legal notice caption/text in the raw payload.")))
            .NoAdditionalProperties());

    private sealed record SystemInteractiveLogonPostureResult(
        string ComputerName,
        InteractiveLogonPolicyState Policy,
        bool HasLegalNotice,
        IReadOnlyList<string> Warnings);

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
                Target: ResolveTargetComputerName(computerName),
                IncludeLegalNoticeText: reader.Boolean("include_legal_notice_text", defaultValue: false)));
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
            var raw = InteractiveLogonPolicyQuery.Get(request.ComputerName);
            var hasLegalNotice = !string.IsNullOrWhiteSpace(raw.LegalNoticeCaption) || !string.IsNullOrWhiteSpace(raw.LegalNoticeText);
            var warnings = new List<string>();

            if (raw.DisableCAD == true) {
                warnings.Add("Ctrl+Alt+Del requirement is disabled.");
            }
            if (!raw.InactivityTimeoutSecs.HasValue || raw.InactivityTimeoutSecs.Value <= 0) {
                warnings.Add("No interactive logon inactivity timeout is configured.");
            }

            var policy = new InteractiveLogonPolicyState {
                DisableCAD = raw.DisableCAD,
                InactivityTimeoutSecs = raw.InactivityTimeoutSecs,
                LegalNoticeCaption = request.IncludeLegalNoticeText ? raw.LegalNoticeCaption : null,
                LegalNoticeText = request.IncludeLegalNoticeText ? raw.LegalNoticeText : null
            };

            var model = new SystemInteractiveLogonPostureResult(
                ComputerName: request.Target,
                Policy: policy,
                HasLegalNotice: hasLegalNotice,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System interactive logon posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("DisableCAD", raw.DisableCAD?.ToString() ?? string.Empty),
                    ("InactivityTimeoutSecs", raw.InactivityTimeoutSecs?.ToString() ?? string.Empty),
                    ("LegalNoticeConfigured", hasLegalNotice.ToString()),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => meta.Add("include_legal_notice_text", request.IncludeLegalNoticeText)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Interactive logon posture query failed."));
        }
    }
}
