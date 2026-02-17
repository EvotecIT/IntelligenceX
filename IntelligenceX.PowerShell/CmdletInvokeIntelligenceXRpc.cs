using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Invokes a raw JSON-RPC method directly against app-server.</para>
/// <para type="description">Low-level escape hatch for advanced scenarios not covered by high-level cmdlets.
/// Parameters are converted from PowerShell objects/hashtables to JSON payloads.</para>
/// <example>
///  <para>Call a raw RPC method</para>
///  <code>Invoke-IntelligenceXRpc -Method "thread/list" -Params @{ limit = 10 }</code>
/// </example>
/// <example>
///  <para>Read config using raw JSON-RPC</para>
///  <code>Invoke-IntelligenceXRpc -Method "config/read"</code>
/// </example>
/// <example>
///  <para>Call an RPC method with nested parameters</para>
///  <code>Invoke-IntelligenceXRpc -Method "command/exec" -Params @{ command = "dotnet --info"; cwd = (Get-Location).Path }</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXRpc")]
[OutputType(typeof(JsonValue))]
public sealed class CmdletInvokeIntelligenceXRpc : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">JSON-RPC method name (for example <c>thread/list</c>).</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Optional method parameters supplied as a PowerShell object/hashtable.</para>
    /// </summary>
    [Parameter]
    public object? Params { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        var parameters = JsonConversion.ToJsonObject(Params);
        var result = await resolved.CallAsync(Method, parameters, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
