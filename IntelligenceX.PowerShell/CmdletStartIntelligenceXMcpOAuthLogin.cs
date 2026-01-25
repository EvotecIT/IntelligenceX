using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts an OAuth login for an MCP server.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXMcpOAuthLogin")]
[OutputType(typeof(McpOauthLoginStart))]
public sealed class CmdletStartIntelligenceXMcpOAuthLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">MCP server identifier.</para>
    /// </summary>
    [Parameter]
    public string? ServerId { get; set; }

    /// <summary>
    /// <para type="description">MCP server name.</para>
    /// </summary>
    [Parameter]
    public string? ServerName { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var result = await resolved.StartMcpOauthLoginAsync(ServerId, ServerName, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
