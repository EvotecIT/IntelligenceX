using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Reads the effective app-server configuration with layer metadata.</para>
/// <para type="description">Returns merged config values plus metadata that explains where values come from
/// (for example defaults, workspace files, or environment overrides).</para>
/// <example>
///  <para>Read effective config as structured output</para>
///  <code>$config = Get-IntelligenceXConfig; $config.Config</code>
/// </example>
/// <example>
///  <para>Inspect where a specific setting comes from</para>
///  <code>$config = Get-IntelligenceXConfig; $config.Origins["model"]</code>
/// </example>
/// <example>
///  <para>Inspect active configuration layers</para>
///  <code>Get-IntelligenceXConfig | Select-Object -ExpandProperty Layers | Select-Object Version, DisabledReason</code>
/// </example>
/// <example>
///  <para>Read raw JSON for custom handling</para>
///  <code>Get-IntelligenceXConfig -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXConfig")]
[OutputType(typeof(ConfigReadResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXConfig : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">App-server client instance to query. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed config models.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var result = await resolved.CallAsync("config/read", (JsonObject?)null, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ReadConfigAsync(CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
