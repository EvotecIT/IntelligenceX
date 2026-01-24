using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists collaboration modes.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXCollaborationMode")]
[OutputType(typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXCollaborationMode : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.ListCollaborationModesAsync(CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
