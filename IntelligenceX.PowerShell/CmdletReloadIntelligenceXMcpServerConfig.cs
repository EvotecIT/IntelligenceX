using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Reloads MCP server configuration.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXMcpServerConfigReload")]
[OutputType(typeof(JsonValue))]
public sealed class CmdletReloadIntelligenceXMcpServerConfig : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.ReloadMcpServerConfigAsync(CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
