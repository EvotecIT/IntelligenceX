using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists MCP server status entries.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXMcpServerStatus")]
[OutputType(typeof(McpServerStatusListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXMcpServerStatus : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Cursor from a previous list response.</para>
    /// </summary>
    [Parameter]
    public string? Cursor { get; set; }

    /// <summary>
    /// <para type="description">Maximum number of entries to return.</para>
    /// </summary>
    [Parameter]
    public int? Limit { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
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
