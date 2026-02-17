using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Enables or disables a skill entry in app-server skill configuration.</para>
/// <para type="description">Updates the persisted skill config for a specific skill path so future tool runs include
/// or exclude that skill.</para>
/// <example>
///  <para>Disable a skill by config path</para>
///  <code>Set-IntelligenceXSkill -Path ".intelligencex/skills/my-skill/skill.json" -Enabled $false</code>
/// </example>
/// <example>
///  <para>Enable a skill again</para>
///  <code>Set-IntelligenceXSkill -Path ".intelligencex/skills/my-skill/skill.json" -Enabled $true</code>
/// </example>
/// <example>
///  <para>Enable multiple known skill config files in a loop</para>
///  <code>@(".intelligencex/skills/analysis/skill.json", ".intelligencex/skills/review/skill.json") | ForEach-Object { Set-IntelligenceXSkill -Path $_ -Enabled $true }</code>
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
    /// <para type="description">Path to the skill configuration entry.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Set to <c>$true</c> to enable, <c>$false</c> to disable.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public bool Enabled { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.WriteSkillConfigAsync(Path, Enabled, CancelToken).ConfigureAwait(false);
    }
}
