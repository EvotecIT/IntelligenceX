using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists available threads.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXThread")]
[OutputType(typeof(ThreadListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXThreadList : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Cursor from a previous list response.</para>
    /// </summary>
    [Parameter]
    public string? Cursor { get; set; }

    /// <summary>
    /// <para type="description">Maximum number of threads to return.</para>
    /// </summary>
    [Parameter]
    public int? Limit { get; set; }

    /// <summary>
    /// <para type="description">Sort key (created_at or updated_at).</para>
    /// </summary>
    [Parameter]
    public string? SortKey { get; set; }

    /// <summary>
    /// <para type="description">Filter by model providers.</para>
    /// </summary>
    [Parameter]
    public string[]? ModelProvider { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

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
            if (!string.IsNullOrWhiteSpace(SortKey)) {
                parameters.Add("sortKey", SortKey);
            }
            if (ModelProvider is not null && ModelProvider.Length > 0) {
                var providers = new JsonArray();
                foreach (var provider in ModelProvider) {
                    providers.Add(provider);
                }
                parameters.Add("modelProviders", providers);
            }
            var result = await resolved.CallAsync("thread/list", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ListThreadsAsync(Cursor, Limit, SortKey, ModelProvider, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
