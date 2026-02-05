using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Writes multiple configuration values.</para>
/// <para type="description">Updates several config keys in a single request.</para>
/// <example>
///  <para>Set multiple values at once</para>
///  <code>Set-IntelligenceXConfigBatch -Values @{ model = "gpt-5.3-codex"; approvalPolicy = "auto" }</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXConfigBatch")]
public sealed class CmdletSetIntelligenceXConfigBatch : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Hashtable of key/value pairs.</para>
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
