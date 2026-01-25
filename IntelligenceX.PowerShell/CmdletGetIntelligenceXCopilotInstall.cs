using System.Management.Automation;
using IntelligenceX.Copilot;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Shows install commands for GitHub Copilot CLI.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXCopilotInstall")]
[OutputType(typeof(CopilotCliInstallCommand))]
public sealed class CmdletGetIntelligenceXCopilotInstall : PSCmdlet {
    /// <summary>
    /// <para type="description">Show prerelease install commands.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    protected override void ProcessRecord() {
        var commands = CopilotCliInstall.GetInstallCommands(Prerelease.IsPresent);
        foreach (var command in commands) {
            WriteObject(command);
        }
    }
}
