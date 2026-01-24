using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Invokes a raw JSON-RPC method on the app-server.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXRpc")]
[OutputType(typeof(JsonValue))]
public sealed class CmdletInvokeIntelligenceXRpc : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">JSON-RPC method name.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Optional parameters as a hashtable.</para>
    /// </summary>
    [Parameter]
    public object? Params { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var parameters = JsonConversion.ToJsonObject(Params);
        var result = await resolved.CallAsync(Method, parameters, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
