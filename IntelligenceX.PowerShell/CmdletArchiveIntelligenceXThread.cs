using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Archives a thread.</para>
/// </summary>
[Cmdlet(VerbsData.Backup, "IntelligenceXThread")]
public sealed class CmdletArchiveIntelligenceXThread : IntelligenceXCmdlet {
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
        await resolved.ArchiveThreadAsync(ThreadId, CancelToken).ConfigureAwait(false);
    }
}
