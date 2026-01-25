using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Enables or disables a skill.</para>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXSkill")]
public sealed class CmdletSetIntelligenceXSkill : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Path to the skill configuration.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Enable the skill.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public bool Enabled { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        await resolved.WriteSkillConfigAsync(Path, Enabled, CancelToken).ConfigureAwait(false);
    }
}
