using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists currently loaded threads.</para>
/// <para type="description">Shows threads currently loaded in the app-server process.</para>
/// <example>
///  <para>List loaded threads</para>
///  <code>Get-IntelligenceXLoadedThread</code>
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
    /// <para type="description">Return raw JSON response.</para>
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
