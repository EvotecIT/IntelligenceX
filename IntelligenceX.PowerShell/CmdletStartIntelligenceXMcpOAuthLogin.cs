using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts an OAuth login for an MCP server.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXMcpOAuthLogin")]
[OutputType(typeof(McpOauthLoginStart), typeof(JsonValue))]
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

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject();
            if (!string.IsNullOrWhiteSpace(ServerId)) {
                parameters.Add("serverId", ServerId);
            }
            if (!string.IsNullOrWhiteSpace(ServerName)) {
                parameters.Add("serverName", ServerName);
            }
            var result = await resolved.CallAsync("mcpServer/oauth/login", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.StartMcpOauthLoginAsync(ServerId, ServerName, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
