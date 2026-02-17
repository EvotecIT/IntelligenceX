using System;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists models available for the current transport context.</para>
/// <para type="description">Returns model metadata for the active client. Raw JSON mode is available only on app-server
/// transport because native transport returns strongly typed models directly.</para>
/// <example>
///  <para>List available models</para>
///  <code>Get-IntelligenceXModel</code>
/// </example>
/// <example>
///  <para>List models as raw JSON (app-server transport only)</para>
///  <code>Get-IntelligenceXModel -Raw</code>
/// </example>
/// <example>
///  <para>Filter to model ids for selection prompts</para>
///  <code>(Get-IntelligenceXModel).Models | Select-Object Id</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXModel")]
[OutputType(typeof(ModelListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXModel : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Returns raw JSON-RPC payload (app-server transport only).</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var client = ResolveClient(Client);
        if (client.TransportKind == OpenAITransportKind.AppServer) {
            var resolved = ResolveAppServerClient(Client);
            if (Raw.IsPresent) {
                var result = await resolved.CallAsync("model/list", (JsonObject?)null, CancelToken).ConfigureAwait(false);
                WriteObject(result);
            } else {
                var result = await resolved.ListModelsAsync(CancelToken).ConfigureAwait(false);
                WriteObject(result);
            }
            return;
        }

        if (Raw.IsPresent) {
            throw new InvalidOperationException("Raw output is only available for the app-server transport.");
        }
        var list = await client.ListModelsAsync(CancelToken).ConfigureAwait(false);
        WriteObject(list);
    }
}
