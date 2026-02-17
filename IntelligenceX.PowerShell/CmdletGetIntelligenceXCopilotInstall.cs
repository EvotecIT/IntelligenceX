using System.Management.Automation;
using IntelligenceX.Copilot;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Shows platform-specific installation commands for GitHub Copilot CLI.</para>
/// <para type="description">This cmdlet does not install anything. It returns suggested install command metadata
/// so you can preview, log, or execute it manually.</para>
/// <example>
///  <para>Show stable install commands</para>
///  <code>Get-IntelligenceXCopilotInstall</code>
/// </example>
/// <example>
///  <para>Show prerelease install commands</para>
///  <code>Get-IntelligenceXCopilotInstall -Prerelease</code>
/// </example>
/// <example>
///  <para>Inspect command and arguments for automation scripts</para>
///  <code>Get-IntelligenceXCopilotInstall | Select-Object Method, FileName, Arguments</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXCopilotInstall")]
[OutputType(typeof(CopilotCliInstallCommand))]
public sealed class CmdletGetIntelligenceXCopilotInstall : PSCmdlet {
    /// <summary>
    /// <para type="description">Returns commands for installing prerelease builds.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        var commands = CopilotCliInstall.GetInstallCommands(Prerelease.IsPresent);
        foreach (var command in commands) {
            WriteObject(command);
        }
    }
}
