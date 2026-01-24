using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Writes multiple configuration values.</para>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXConfigBatch")]
public sealed class CmdletSetIntelligenceXConfigBatch : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Hashtable of key/value pairs.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public Hashtable Values { get; set; } = new Hashtable();

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var entries = new List<ConfigEntry>();
        foreach (DictionaryEntry entry in Values) {
            var key = entry.Key?.ToString() ?? string.Empty;
            var value = JsonConversion.ToJsonValue(entry.Value);
            entries.Add(new ConfigEntry(key, value));
        }

        await resolved.WriteConfigBatchAsync(entries, CancelToken).ConfigureAwait(false);
    }
}
