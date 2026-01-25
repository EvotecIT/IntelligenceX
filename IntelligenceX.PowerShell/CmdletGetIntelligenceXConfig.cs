using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Reads the current configuration.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXConfig")]
[OutputType(typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXConfig : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.ReadConfigAsync(CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
