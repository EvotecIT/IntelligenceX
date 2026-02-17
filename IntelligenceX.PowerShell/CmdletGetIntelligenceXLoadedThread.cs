using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists threads currently loaded in the app-server process.</para>
/// <para type="description">Returns thread ids that are currently active in memory for the running app-server session.
/// Useful for diagnostics and cleanup scripts.</para>
/// <example>
///  <para>List loaded threads</para>
///  <code>Get-IntelligenceXLoadedThread</code>
/// </example>
/// <example>
///  <para>Get raw JSON output</para>
///  <code>Get-IntelligenceXLoadedThread -Raw</code>
/// </example>
/// <example>
///  <para>Expand loaded thread ids as plain values</para>
///  <code>(Get-IntelligenceXLoadedThread).Data</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXLoadedThread")]
[OutputType(typeof(ThreadIdListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXLoadedThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed thread-id models.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var result = await resolved.CallAsync("thread/loaded/list", (JsonObject?)null, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ListLoadedThreadsAsync(CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
