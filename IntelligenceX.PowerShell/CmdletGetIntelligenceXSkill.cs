using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists available skills.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXSkill")]
[OutputType(typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXSkill : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Working directories to scan for skills.</para>
    /// </summary>
    [Parameter]
    public string[]? Cwd { get; set; }

    /// <summary>
    /// <para type="description">Force reload of skills.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter ForceReload { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.ListSkillsAsync(Cwd, ForceReload.IsPresent, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
