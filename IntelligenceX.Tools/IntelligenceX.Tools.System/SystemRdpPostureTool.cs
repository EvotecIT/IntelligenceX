using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Rdp;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns RDP runtime and policy posture details (read-only).
/// </summary>
public sealed class SystemRdpPostureTool : SystemToolBase, ITool {
    private sealed record RdpPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludePolicy);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_rdp_posture",
        "Return RDP runtime and policy posture (enabled/NLA/port/TLS/policy levels) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_policy", ToolSchema.Boolean("When true (default), include RDP policy snapshot.")))
            .NoAdditionalProperties());

    private sealed record SystemRdpPostureResult(
        string ComputerName,
        RdpConfigInfo Runtime,
        RdpPolicyState? Policy,
        bool? TlsRequired,
        bool? WeakSecurityLayer,
        bool? WeakEncryptionLevel,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRdpPostureTool"/> class.
    /// </summary>
    public SystemRdpPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<RdpPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<RdpPostureRequest>.Success(new RdpPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludePolicy: reader.Boolean("include_policy", defaultValue: true)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<RdpPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_rdp_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;

        try {
            var runtime = RdpQuery.Get(request.ComputerName);
            var policy = request.IncludePolicy ? RdpPolicyQuery.Get(request.ComputerName) : null;
            var warnings = new List<string>();

            var effectiveTlsRequired =
                runtime.SecurityLayer == (int)RdpSecurityLayer.SslTls
                || policy?.SecurityLayer == RdpSecurityLayer.SslTls;
            var weakSecurityLayer =
                runtime.SecurityLayer == (int)RdpSecurityLayer.Rdp
                || policy?.SecurityLayer == RdpSecurityLayer.Rdp;
            var weakEncryptionLevel = policy?.MinEncryptionLevel == RdpEncryptionLevel.Low;

            if (runtime.IsEnabled == true || policy?.AllowConnections == true) {
                if (runtime.NlaRequired == false || policy?.NlaRequired == false) {
                    warnings.Add("NLA is not required while RDP is enabled.");
                }
                if (weakSecurityLayer == true) {
                    warnings.Add("RDP security layer is set to legacy RDP.");
                }
                if (weakEncryptionLevel == true) {
                    warnings.Add("RDP minimum encryption level is Low.");
                }
            }
            if (runtime.ServiceRunning == false && (runtime.IsEnabled == true || policy?.AllowConnections == true)) {
                warnings.Add("RDP appears enabled, but TermService is not running.");
            }

            var model = new SystemRdpPostureResult(
                ComputerName: request.Target,
                Runtime: runtime,
                Policy: policy,
                TlsRequired: effectiveTlsRequired,
                WeakSecurityLayer: weakSecurityLayer,
                WeakEncryptionLevel: weakEncryptionLevel,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System RDP posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("RdpEnabled", (runtime.IsEnabled ?? policy?.AllowConnections)?.ToString() ?? string.Empty),
                    ("NlaRequired", (runtime.NlaRequired ?? policy?.NlaRequired)?.ToString() ?? string.Empty),
                    ("Port", runtime.Port?.ToString() ?? string.Empty),
                    ("ServiceRunning", runtime.ServiceRunning?.ToString() ?? string.Empty),
                    ("TlsRequired", effectiveTlsRequired.ToString()),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => meta.Add("include_policy", request.IncludePolicy)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "RDP posture query failed."));
        }
    }
}
