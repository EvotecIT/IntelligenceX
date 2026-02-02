using System;
using System.Management.Automation;
using IntelligenceX.Copilot;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Installs GitHub Copilot CLI (opt-in).</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Install, "IntelligenceXCopilotCli", SupportsShouldProcess = true)]
[OutputType(typeof(CopilotCliInstallCommand))]
public sealed class CmdletInstallIntelligenceXCopilotCli : PSCmdlet {
    /// <summary>
    /// <para type="description">Install method to use.</para>
    /// </summary>
    [Parameter]
    public CopilotCliInstallMethod Method { get; set; } = CopilotCliInstallMethod.Auto;

    /// <summary>
    /// <para type="description">Install prerelease version.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>
    /// <para type="description">Return the install command object.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        var command = CopilotCliInstall.GetCommand(Method, Prerelease.IsPresent);
        if (!ShouldProcess($"{command.FileName} {command.Arguments}", "Install Copilot CLI")) {
            return;
        }

        var exitCode = CopilotCliInstall.InstallAsync(command).GetAwaiter().GetResult();
        if (exitCode != 0) {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException($"Copilot CLI install failed with exit code {exitCode}."),
                "CopilotCliInstallFailed",
                ErrorCategory.InvalidResult,
                command));
        }

        if (PassThru.IsPresent) {
            WriteObject(command);
        }
    }
}
