using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns WinRM authentication and listener posture for the local or remote Windows host.
/// </summary>
public sealed class SystemWinRmPostureTool : SystemToolBase, ITool {
    private sealed record WinRmPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record WinRmPostureResponse(
        string ComputerName,
        bool? ServiceAllowUnencrypted,
        bool? ClientAllowUnencrypted,
        bool? ServiceBasicEnabled,
        bool? ClientBasicEnabled,
        int ListenerCount,
        int HttpsListenerCount,
        int HttpListenerCount,
        int CertificateBackedListenerCount,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_winrm_posture",
        "Return WinRM service/client authentication posture and listener configuration for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:winrm_posture", "intent:remote_management_policy", "scope:host_remote_management" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemWinRmPostureTool"/> class.
    /// </summary>
    public SystemWinRmPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<WinRmPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<WinRmPostureRequest>.Success(new WinRmPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<WinRmPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_winrm_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = WinRmPolicyQuery.Get(request.ComputerName);
            var warnings = WinRmPolicyRiskEvaluator.Evaluate(posture);
            var httpsListeners = posture.Listeners.Count(static listener =>
                string.Equals(listener.Transport, "HTTPS", StringComparison.OrdinalIgnoreCase));
            var httpListeners = posture.Listeners.Count(static listener =>
                string.Equals(listener.Transport, "HTTP", StringComparison.OrdinalIgnoreCase));
            var certificateBackedListeners = posture.Listeners.Count(static listener =>
                !string.IsNullOrWhiteSpace(listener.CertificateThumbprint));
            var effectiveComputerName = request.Target;
            var model = new WinRmPostureResponse(
                ComputerName: effectiveComputerName,
                ServiceAllowUnencrypted: posture.ServiceAllowUnencrypted,
                ClientAllowUnencrypted: posture.ClientAllowUnencrypted,
                ServiceBasicEnabled: posture.ServiceAuth.Basic,
                ClientBasicEnabled: posture.ClientAuth.Basic,
                ListenerCount: posture.Listeners.Count,
                HttpsListenerCount: httpsListeners,
                HttpListenerCount: httpListeners,
                CertificateBackedListenerCount: certificateBackedListeners,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_winrm_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(posture.Listeners.Count, 1),
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "WinRM posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Service Allow Unencrypted", FormatNullableBool(posture.ServiceAllowUnencrypted)),
                    ("Client Allow Unencrypted", FormatNullableBool(posture.ClientAllowUnencrypted)),
                    ("Service Basic Enabled", FormatNullableBool(posture.ServiceAuth.Basic)),
                    ("Client Basic Enabled", FormatNullableBool(posture.ClientAuth.Basic)),
                    ("Listeners", posture.Listeners.Count.ToString(CultureInfo.InvariantCulture)),
                    ("HTTPS Listeners", httpsListeners.ToString(CultureInfo.InvariantCulture)),
                    ("HTTP Listeners", httpListeners.ToString(CultureInfo.InvariantCulture)),
                    ("Certificate-backed Listeners", certificateBackedListeners.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "WinRM posture query failed."));
        }
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
