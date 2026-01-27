using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists available skills.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXSkill")]
[OutputType(typeof(SkillListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXSkill : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

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

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject();
            if (Cwd is not null && Cwd.Length > 0) {
                var array = new JsonArray();
                foreach (var cwd in Cwd) {
                    array.Add(cwd);
                }
                parameters.Add("cwds", array);
            }
            if (ForceReload.IsPresent) {
                parameters.Add("forceReload", true);
            }
            var result = await resolved.CallAsync("skills/list", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ListSkillsAsync(Cwd, ForceReload.IsPresent, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
