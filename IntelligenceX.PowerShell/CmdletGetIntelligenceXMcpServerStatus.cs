using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists configured MCP servers with auth, tool, and resource status.</para>
/// <para type="description">Queries the app-server MCP registry and returns one page of server status data. The result includes
/// <c>Servers</c> (current page) and <c>NextCursor</c> (for pagination).</para>
/// <example>
///  <para>List the first page of MCP server statuses</para>
///  <code>Get-IntelligenceXMcpServerStatus</code>
/// </example>
/// <example>
///  <para>Inspect server names, auth status, and available tool counts</para>
///  <code>$status = Get-IntelligenceXMcpServerStatus; $status.Servers | Select-Object Name, AuthStatus, @{Name='ToolCount';Expression={$_.Tools.Count}}</code>
/// </example>
/// <example>
///  <para>Continue with a pagination cursor</para>
///  <code>$page1 = Get-IntelligenceXMcpServerStatus -Limit 20; if ($page1.NextCursor) { Get-IntelligenceXMcpServerStatus -Cursor $page1.NextCursor -Limit 20 }</code>
/// </example>
/// <example>
///  <para>Return raw JSON when you need provider-specific fields</para>
///  <code>Get-IntelligenceXMcpServerStatus -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXMcpServerStatus")]
[OutputType(typeof(McpServerStatusListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXMcpServerStatus : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">App-server client instance to query. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Pagination cursor from a previous response (<c>NextCursor</c>).</para>
    /// </summary>
    [Parameter]
    public string? Cursor { get; set; }

    /// <summary>
    /// <para type="description">Maximum number of server entries to return in this page.</para>
    /// </summary>
    [Parameter]
    public int? Limit { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed models.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject();
            if (!string.IsNullOrWhiteSpace(Cursor)) {
                parameters.Add("cursor", Cursor);
            }
            if (Limit.HasValue) {
                parameters.Add("limit", Limit.Value);
            }
            var result = await resolved.CallAsync("mcpServerStatus/list", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ListMcpServerStatusAsync(Cursor, Limit, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
