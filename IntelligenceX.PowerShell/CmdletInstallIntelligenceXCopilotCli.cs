using System;
using System.Management.Automation;
using IntelligenceX.Copilot;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Installs GitHub Copilot CLI using a selected install strategy.</para>
/// <para type="description">Executes the platform-specific installer command and optionally returns command metadata.
/// Supports WhatIf/Confirm through <c>ShouldProcess</c>.</para>
/// <example>
///  <para>Install Copilot CLI using the auto method</para>
///  <code>Install-IntelligenceXCopilotCli</code>
/// </example>
/// <example>
///  <para>Preview installation without executing changes</para>
///  <code>Install-IntelligenceXCopilotCli -WhatIf</code>
/// </example>
/// <example>
///  <para>Install prerelease and return command metadata</para>
///  <code>Install-IntelligenceXCopilotCli -Prerelease -PassThru</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Install, "IntelligenceXCopilotCli", SupportsShouldProcess = true)]
[OutputType(typeof(CopilotCliInstallCommand))]
public sealed class CmdletInstallIntelligenceXCopilotCli : PSCmdlet {
    /// <summary>
    /// <para type="description">Install method to use (Auto, Winget, Brew, Apt, etc. depending on platform).</para>
    /// </summary>
    [Parameter]
    public CopilotCliInstallMethod Method { get; set; } = CopilotCliInstallMethod.Auto;

    /// <summary>
    /// <para type="description">Installs a prerelease build when available.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>
    /// <para type="description">Returns the resolved install command object after successful execution.</para>
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
