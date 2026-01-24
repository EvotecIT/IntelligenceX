using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists available threads.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXThread")]
[OutputType(typeof(ThreadListResult))]
public sealed class CmdletGetIntelligenceXThreadList : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Cursor from a previous list response.</para>
    /// </summary>
    [Parameter]
    public string? Cursor { get; set; }

    /// <summary>
    /// <para type="description">Maximum number of threads to return.</para>
    /// </summary>
    [Parameter]
    public int? Limit { get; set; }

    /// <summary>
    /// <para type="description">Sort key (created_at or updated_at).</para>
    /// </summary>
    [Parameter]
    public string? SortKey { get; set; }

    /// <summary>
    /// <para type="description">Filter by model providers.</para>
    /// </summary>
    [Parameter]
    public string[]? ModelProvider { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.ListThreadsAsync(Cursor, Limit, SortKey, ModelProvider, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
