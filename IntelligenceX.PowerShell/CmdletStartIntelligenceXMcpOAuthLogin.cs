using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts an MCP OAuth login flow and returns the browser authorization URL.</para>
/// <para type="description">Use this cmdlet when an MCP server reports <c>OAuth</c> auth status. The response includes a
/// <c>LoginId</c> and <c>AuthUrl</c> you can open in a browser.</para>
/// <example>
///  <para>Start OAuth login by server name and open the browser</para>
///  <code>$login = Start-IntelligenceXMcpOAuthLogin -ServerName "github"; Start-Process $login.AuthUrl; $login</code>
/// </example>
/// <example>
///  <para>Start OAuth login by server id</para>
///  <code>Start-IntelligenceXMcpOAuthLogin -ServerId "srv_123"</code>
/// </example>
/// <example>
///  <para>Discover an OAuth server first, then start login</para>
///  <code>$server = (Get-IntelligenceXMcpServerStatus).Servers | Where-Object AuthStatus -eq "OAuth" | Select-Object -First 1; Start-IntelligenceXMcpOAuthLogin -ServerName $server.Name</code>
/// </example>
/// <example>
///  <para>Return raw JSON response for custom handling</para>
///  <code>Start-IntelligenceXMcpOAuthLogin -ServerName "github" -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXMcpOAuthLogin")]
[OutputType(typeof(McpOauthLoginStart), typeof(JsonValue))]
public sealed class CmdletStartIntelligenceXMcpOAuthLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">App-server client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">MCP server identifier to start login for.</para>
    /// </summary>
    [Parameter]
    public string? ServerId { get; set; }

    /// <summary>
    /// <para type="description">MCP server name to start login for.</para>
    /// </summary>
    [Parameter]
    public string? ServerName { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of a typed model.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
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
