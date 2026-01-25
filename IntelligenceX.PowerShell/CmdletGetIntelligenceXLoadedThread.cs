using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists currently loaded threads.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXLoadedThread")]
[OutputType(typeof(ThreadIdListResult))]
public sealed class CmdletGetIntelligenceXLoadedThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.ListLoadedThreadsAsync(CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
