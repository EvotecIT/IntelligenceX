using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Reloads MCP server configuration in the running app-server.</para>
/// <para type="description">Refreshes MCP configuration after file changes so newly added servers, tools, and auth settings
/// are visible without restarting the app-server process.</para>
/// <example>
///  <para>Reload MCP server config after editing config files</para>
///  <code>Invoke-IntelligenceXMcpServerConfigReload</code>
/// </example>
/// <example>
///  <para>Reload config, then verify with server status</para>
///  <code>Invoke-IntelligenceXMcpServerConfigReload; Get-IntelligenceXMcpServerStatus | Select-Object -ExpandProperty Servers | Select-Object Name, AuthStatus</code>
/// </example>
/// <example>
///  <para>Reload config for an explicit client instance</para>
///  <code>$client = Connect-IntelligenceX; Invoke-IntelligenceXMcpServerConfigReload -Client $client</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXMcpServerConfigReload")]
public sealed class CmdletReloadIntelligenceXMcpServerConfig : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">App-server client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.ReloadMcpServerConfigAsync(CancelToken).ConfigureAwait(false);
    }
}
