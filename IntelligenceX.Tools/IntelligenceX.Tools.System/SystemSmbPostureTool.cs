using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Smb;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns SMB security posture highlights from ComputerX SMB configuration (read-only).
/// </summary>
public sealed class SystemSmbPostureTool : SystemToolBase, ITool {
    private sealed record SmbPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeNetbiosInterfaces);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_smb_posture",
        "Return SMB server/client security posture highlights (SMB1/signing/guest/null-session/NetBIOS) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_netbios_interfaces", ToolSchema.Boolean("When true, include per-interface NetBIOS options map.")))
            .NoAdditionalProperties());

    private sealed record SystemSmbPostureResult(
        string ComputerName,
        SmbConfigInfo Configuration,
        bool? Smb1EnabledRisk,
        bool? SigningNotRequiredRisk,
        bool? InsecureGuestRisk,
        bool? NullSessionExposureRisk,
        bool? WeakLmNtlmPolicyRisk,
        int NetbiosInterfacesNotDisabled,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemSmbPostureTool"/> class.
    /// </summary>
    public SystemSmbPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<SmbPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<SmbPostureRequest>.Success(new SmbPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeNetbiosInterfaces: reader.Boolean("include_netbios_interfaces", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<SmbPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_smb_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;

        try {
            var raw = SmbConfigQuery.Get(request.ComputerName);
            var config = request.IncludeNetbiosInterfaces ? raw : new SmbConfigInfo {
                ComputerName = raw.ComputerName,
                ServerSmb1Enabled = raw.ServerSmb1Enabled,
                ServerSmb2Enabled = raw.ServerSmb2Enabled,
                ServerSigningRequired = raw.ServerSigningRequired,
                ServerSigningEnabled = raw.ServerSigningEnabled,
                ClientSigningRequired = raw.ClientSigningRequired,
                ClientSigningEnabled = raw.ClientSigningEnabled,
                ClientAllowInsecureGuestAuth = raw.ClientAllowInsecureGuestAuth,
                ClientPolicyAllowInsecureGuestAuth = raw.ClientPolicyAllowInsecureGuestAuth,
                ServerRestrictNullSessAccess = raw.ServerRestrictNullSessAccess,
                EveryoneIncludesAnonymous = raw.EveryoneIncludesAnonymous,
                NullSessionShares = raw.NullSessionShares,
                NullSessionPipes = raw.NullSessionPipes,
                RestrictAnonymous = raw.RestrictAnonymous,
                RestrictAnonymousSam = raw.RestrictAnonymousSam,
                LmCompatibilityLevel = raw.LmCompatibilityLevel,
                EnablePlainTextPassword = raw.EnablePlainTextPassword,
                EnableLmHosts = raw.EnableLmHosts,
                NodeType = raw.NodeType,
                AutoShareServer = raw.AutoShareServer,
                AutoShareWks = raw.AutoShareWks,
                NetbiosOptionsPerInterface = new Dictionary<string, int?>()
            };

            bool? smb1Risk = raw.ServerSmb1Enabled;
            bool? signingRisk = null;
            if (raw.ServerSigningRequired.HasValue || raw.ClientSigningRequired.HasValue) {
                signingRisk = raw.ServerSigningRequired == false || raw.ClientSigningRequired == false;
            }
            bool? insecureGuestRisk = null;
            if (raw.ClientAllowInsecureGuestAuth.HasValue || raw.ClientPolicyAllowInsecureGuestAuth.HasValue) {
                insecureGuestRisk = raw.ClientAllowInsecureGuestAuth == true || raw.ClientPolicyAllowInsecureGuestAuth == true;
            }
            bool? nullSessionRisk = null;
            if (raw.ServerRestrictNullSessAccess.HasValue || raw.EveryoneIncludesAnonymous.HasValue || raw.NullSessionPipes.Length > 0 || raw.NullSessionShares.Length > 0) {
                nullSessionRisk = raw.ServerRestrictNullSessAccess == false
                    || raw.EveryoneIncludesAnonymous == true
                    || raw.NullSessionPipes.Length > 0
                    || raw.NullSessionShares.Length > 0;
            }
            bool? weakLmNtlmRisk = raw.LmCompatibilityLevel.HasValue
                ? raw.LmCompatibilityLevel.Value < 5
                : null;

            var netbiosInterfacesNotDisabled = raw.NetbiosOptionsPerInterface.Values.Count(v => !v.HasValue || v.Value != 2);
            var warnings = new List<string>();
            if (smb1Risk == true) warnings.Add("SMB1 server component is enabled.");
            if (signingRisk == true) warnings.Add("SMB signing is not required on server or client.");
            if (insecureGuestRisk == true) warnings.Add("Insecure SMB guest authentication is allowed.");
            if (nullSessionRisk == true) warnings.Add("Null-session or anonymous SMB exposure detected.");
            if (weakLmNtlmRisk == true) warnings.Add("LM/NTLM compatibility level is weaker than level 5.");
            if (netbiosInterfacesNotDisabled > 0) warnings.Add("One or more NetBIOS interfaces are not disabled.");

            var model = new SystemSmbPostureResult(
                ComputerName: request.Target,
                Configuration: config,
                Smb1EnabledRisk: smb1Risk,
                SigningNotRequiredRisk: signingRisk,
                InsecureGuestRisk: insecureGuestRisk,
                NullSessionExposureRisk: nullSessionRisk,
                WeakLmNtlmPolicyRisk: weakLmNtlmRisk,
                NetbiosInterfacesNotDisabled: netbiosInterfacesNotDisabled,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System SMB posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("ServerSmb1Enabled", raw.ServerSmb1Enabled?.ToString() ?? string.Empty),
                    ("ServerSigningRequired", raw.ServerSigningRequired?.ToString() ?? string.Empty),
                    ("ClientSigningRequired", raw.ClientSigningRequired?.ToString() ?? string.Empty),
                    ("InsecureGuestAllowed", insecureGuestRisk?.ToString() ?? string.Empty),
                    ("NullSessionExposure", nullSessionRisk?.ToString() ?? string.Empty),
                    ("NetbiosInterfacesNotDisabled", netbiosInterfacesNotDisabled.ToString()),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => meta.Add("include_netbios_interfaces", request.IncludeNetbiosInterfaces)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildSmbPostureHints(
                    warningCount: warnings.Count,
                    nullSessionShareCount: raw.NullSessionShares.Length,
                    nullSessionPipeCount: raw.NullSessionPipes.Length)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "SMB posture query failed."));
        }
    }
}
