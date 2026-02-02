using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Enables or disables a skill.</para>
/// <para type="description">Updates a skill configuration file to enable or disable it.</para>
/// <example>
///  <para>Disable a skill by config path</para>
///  <code>Set-IntelligenceXSkill -Path ".intelligencex/skills/my-skill/skill.json" -Enabled $false</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXSkill")]
public sealed class CmdletSetIntelligenceXSkill : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

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

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.WriteSkillConfigAsync(Path, Enabled, CancelToken).ConfigureAwait(false);
    }
}
