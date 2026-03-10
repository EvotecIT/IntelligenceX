using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns WinRM security posture details from ComputerX registry-backed policy state (read-only).
/// </summary>
public sealed class SystemWinRmPostureTool : SystemToolBase, ITool {
    private sealed record WinRmPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeListeners,
        bool IncludeServiceRootSddl);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_winrm_posture",
        "Return WinRM security posture (auth modes, encryption flags, listeners, optional service ACL) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_listeners", ToolSchema.Boolean("When true (default), include listener inventory.")),
                ("include_service_root_sddl", ToolSchema.Boolean("When true, include WinRM service RootSDDL in the raw payload.")))
            .NoAdditionalProperties());

    private sealed record SystemWinRmPostureResult(
        string ComputerName,
        WinRmPolicyState Policy,
        int HttpListenerCount,
        int HttpsListenerCount,
        IReadOnlyList<string> Warnings);

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
                Target: ResolveTargetComputerName(computerName),
                IncludeListeners: reader.Boolean("include_listeners", defaultValue: true),
                IncludeServiceRootSddl: reader.Boolean("include_service_root_sddl", defaultValue: false)));
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
            var raw = WinRmPolicyQuery.Get(request.ComputerName);
            var listeners = request.IncludeListeners ? raw.Listeners : new List<WinRmListener>();
            var policy = new WinRmPolicyState {
                ServiceAllowUnencrypted = raw.ServiceAllowUnencrypted,
                ClientAllowUnencrypted = raw.ClientAllowUnencrypted,
                ServiceAuth = raw.ServiceAuth,
                ClientAuth = raw.ClientAuth,
                Listeners = listeners,
                ServiceRootSddl = request.IncludeServiceRootSddl ? raw.ServiceRootSddl : null
            };

            var httpListenerCount = raw.Listeners.Count(listener =>
                string.Equals(listener.Transport, "HTTP", StringComparison.OrdinalIgnoreCase));
            var httpsListenerCount = raw.Listeners.Count(listener =>
                string.Equals(listener.Transport, "HTTPS", StringComparison.OrdinalIgnoreCase));

            var warnings = new List<string>();
            if (raw.ServiceAllowUnencrypted == true || raw.ClientAllowUnencrypted == true) {
                warnings.Add("WinRM allows unencrypted traffic on the client or service side.");
            }
            if (raw.ServiceAuth.Basic == true || raw.ClientAuth.Basic == true) {
                warnings.Add("WinRM Basic authentication is enabled.");
            }
            if (httpListenerCount > 0) {
                warnings.Add("WinRM HTTP listeners are configured.");
            }
            if (raw.Listeners.Count > 0 && httpsListenerCount == 0) {
                warnings.Add("WinRM listeners are present, but no HTTPS listener is configured.");
            }

            var model = new SystemWinRmPostureResult(
                ComputerName: request.Target,
                Policy: policy,
                HttpListenerCount: httpListenerCount,
                HttpsListenerCount: httpsListenerCount,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System WinRM posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("ServiceAllowUnencrypted", raw.ServiceAllowUnencrypted?.ToString() ?? string.Empty),
                    ("ClientAllowUnencrypted", raw.ClientAllowUnencrypted?.ToString() ?? string.Empty),
                    ("BasicAuthEnabled", ((raw.ServiceAuth.Basic == true) || (raw.ClientAuth.Basic == true)).ToString()),
                    ("HttpListeners", httpListenerCount.ToString()),
                    ("HttpsListeners", httpsListenerCount.ToString()),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => {
                        meta.Add("include_listeners", request.IncludeListeners);
                        meta.Add("include_service_root_sddl", request.IncludeServiceRootSddl);
                    }),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "WinRM posture query failed."));
        }
    }
}
