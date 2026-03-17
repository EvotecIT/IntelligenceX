using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.RemoteAccess;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns remote-access posture for the local or remote Windows host.
/// </summary>
public sealed class SystemRemoteAccessPostureTool : SystemToolBase, ITool {
    private sealed record RemoteAccessRequest(
        string? ComputerName,
        string Target);

    private sealed record RemoteAccessResponse(
        string ComputerName,
        bool? SshdServiceInstalled,
        bool? SshdServiceRunning,
        string? SshdStartupType,
        bool? SshAgentServiceInstalled,
        bool? SshAgentServiceRunning,
        string? SshAgentStartupType,
        bool? OpenSshConfigPresent,
        bool? OpenSshPort22Listening,
        bool? RemoteAssistanceEnabled,
        int? RemoteAssistanceAllowHelpRaw,
        int? RemoteAssistanceAllowFullControlRaw,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_remote_access_posture",
        "Return remote-access posture (OpenSSH server/agent and Remote Assistance) for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:remote_access_posture", "intent:ssh_remote_assistance", "scope:host_remote_access" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRemoteAccessPostureTool"/> class.
    /// </summary>
    public SystemRemoteAccessPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<RemoteAccessRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<RemoteAccessRequest>.Success(new RemoteAccessRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<RemoteAccessRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_remote_access_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await RemoteAccess.GetAsync(request.ComputerName, cancellationToken).ConfigureAwait(false);
            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var warnings = RemoteAccessRiskEvaluator.Evaluate(
                posture,
                new RemoteAccessRiskOptions { IsRemoteScope = !IsLocalTarget(request.ComputerName, request.Target) });
            var model = new RemoteAccessResponse(
                ComputerName: effectiveComputerName,
                SshdServiceInstalled: posture.SshdServiceInstalled,
                SshdServiceRunning: posture.SshdServiceRunning,
                SshdStartupType: posture.SshdStartupType,
                SshAgentServiceInstalled: posture.SshAgentServiceInstalled,
                SshAgentServiceRunning: posture.SshAgentServiceRunning,
                SshAgentStartupType: posture.SshAgentStartupType,
                OpenSshConfigPresent: posture.OpenSshConfigPresent,
                OpenSshPort22Listening: posture.OpenSshPort22Listening,
                RemoteAssistanceEnabled: posture.RemoteAssistanceEnabled,
                RemoteAssistanceAllowHelpRaw: posture.RemoteAssistanceAllowHelpRaw,
                RemoteAssistanceAllowFullControlRaw: posture.RemoteAssistanceAllowFullControlRaw,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_remote_access_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Remote access posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("sshd Installed", FormatNullableBool(posture.SshdServiceInstalled)),
                    ("sshd Running", FormatNullableBool(posture.SshdServiceRunning)),
                    ("sshd Startup Type", posture.SshdStartupType ?? "unknown"),
                    ("ssh-agent Installed", FormatNullableBool(posture.SshAgentServiceInstalled)),
                    ("ssh-agent Running", FormatNullableBool(posture.SshAgentServiceRunning)),
                    ("ssh-agent Startup Type", posture.SshAgentStartupType ?? "unknown"),
                    ("OpenSSH Config Present", FormatNullableBool(posture.OpenSshConfigPresent)),
                    ("OpenSSH Port 22 Listening", FormatNullableBool(posture.OpenSshPort22Listening)),
                    ("Remote Assistance Enabled", FormatNullableBool(posture.RemoteAssistanceEnabled)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Remote-access posture query failed.");
        }
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
