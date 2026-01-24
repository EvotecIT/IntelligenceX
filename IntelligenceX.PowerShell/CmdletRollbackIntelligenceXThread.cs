using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Rolls back the last N turns from a thread.</para>
/// </summary>
[Cmdlet(VerbsData.Restore, "IntelligenceXThread")]
[OutputType(typeof(ThreadInfo))]
public sealed class CmdletRollbackIntelligenceXThread : IntelligenceXCmdlet {
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

    /// <summary>
    /// <para type="description">Number of turns to roll back.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public int Turns { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var thread = await resolved.RollbackThreadAsync(ThreadId, Turns, CancelToken).ConfigureAwait(false);
        WriteObject(thread);
    }
}
