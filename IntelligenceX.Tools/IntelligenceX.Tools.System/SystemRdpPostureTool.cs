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
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_rdp_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var includePolicy = ToolArgs.GetBoolean(arguments, "include_policy", defaultValue: true);
        var target = ResolveTargetComputerName(computerName);

        try {
            var runtime = RdpQuery.Get(computerName);
            var policy = includePolicy ? RdpPolicyQuery.Get(computerName) : null;
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
                ComputerName: target,
                Runtime: runtime,
                Policy: policy,
                TlsRequired: effectiveTlsRequired,
                WeakSecurityLayer: weakSecurityLayer,
                WeakEncryptionLevel: weakEncryptionLevel,
                Warnings: warnings);

            return Task.FromResult(ToolResponse.OkFactsModelWithRenderValue(
                model: model,
                title: "System RDP posture",
                facts: new[] {
                    ("Computer", target),
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
                    target: target,
                    mutate: meta => meta.Add("include_policy", includePolicy)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "RDP posture query failed."));
        }
    }
}
