using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Writes multiple app-server configuration values in one request.</para>
/// <para type="description">Sends a batch of key/value updates. This is useful for related settings you want to apply together.
/// Hashtable values are converted to JSON before sending.</para>
/// <example>
///  <para>Set multiple values at once</para>
///  <code>Set-IntelligenceXConfigBatch -Values @{ model = "gpt-5.3-codex"; approvalPolicy = "auto" }</code>
/// </example>
/// <example>
///  <para>Include booleans and nested objects</para>
///  <code>Set-IntelligenceXConfigBatch -Values @{ stream = $true; responseFormat = @{ type = "json_object" } }</code>
/// </example>
/// <example>
///  <para>Apply a batch, then inspect the effective config</para>
///  <code>Set-IntelligenceXConfigBatch -Values @{ model = "gpt-5.3-codex"; approvalPolicy = "on-failure" }; (Get-IntelligenceXConfig).Config</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXConfigBatch")]
public sealed class CmdletSetIntelligenceXConfigBatch : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">App-server client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Hashtable of configuration key/value pairs to write.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public Hashtable Values { get; set; } = new Hashtable();

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        var entries = new List<ConfigEntry>();
        foreach (DictionaryEntry entry in Values) {
            var key = entry.Key?.ToString() ?? string.Empty;
            var value = JsonConversion.ToJsonValue(entry.Value);
            entries.Add(new ConfigEntry(key, value));
        }

        await resolved.WriteConfigBatchAsync(entries, CancelToken).ConfigureAwait(false);
    }
}
