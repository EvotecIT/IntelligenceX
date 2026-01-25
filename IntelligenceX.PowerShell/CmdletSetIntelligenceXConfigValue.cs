using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Writes a configuration value.</para>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXConfigValue")]
public sealed class CmdletSetIntelligenceXConfigValue : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Configuration key.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Configuration value.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public object? Value { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var jsonValue = JsonConversion.ToJsonValue(Value);
        await resolved.WriteConfigValueAsync(Key, jsonValue, CancelToken).ConfigureAwait(false);
    }
}
