using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Forks an existing thread.</para>
/// </summary>
[Cmdlet(VerbsCommon.New, "IntelligenceXThreadFork")]
[OutputType(typeof(ThreadInfo))]
public sealed class CmdletForkIntelligenceXThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Thread identifier.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var thread = await resolved.ForkThreadAsync(ThreadId, CancelToken).ConfigureAwait(false);
        WriteObject(thread);
    }
}
