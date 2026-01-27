using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Reloads MCP server configuration.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXMcpServerConfigReload")]
public sealed class CmdletReloadIntelligenceXMcpServerConfig : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.ReloadMcpServerConfigAsync(CancelToken).ConfigureAwait(false);
    }
}
