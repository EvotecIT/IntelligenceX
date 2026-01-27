using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Reads the current configuration.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXConfig")]
[OutputType(typeof(ConfigReadResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXConfig : IntelligenceXCmdlet {
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

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var result = await resolved.CallAsync("config/read", (JsonObject?)null, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ReadConfigAsync(CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
