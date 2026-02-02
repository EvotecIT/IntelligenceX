using System.Management.Automation;
using IntelligenceX.Copilot;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Shows install commands for GitHub Copilot CLI.</para>
/// <para type="description">Outputs platform-specific install commands for the Copilot CLI.</para>
/// <example>
///  <para>Show stable install commands</para>
///  <code>Get-IntelligenceXCopilotInstall</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXCopilotInstall")]
[OutputType(typeof(CopilotCliInstallCommand))]
public sealed class CmdletGetIntelligenceXCopilotInstall : PSCmdlet {
    /// <summary>
    /// <para type="description">Show prerelease install commands.</para>
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
