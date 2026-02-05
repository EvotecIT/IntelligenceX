using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Writes a configuration value.</para>
/// <para type="description">Updates a single config key on the app-server. Use Get-IntelligenceXConfig to inspect current values.</para>
/// <example>
///  <para>Set the model used by default</para>
///  <code>Set-IntelligenceXConfigValue -Key "model" -Value "gpt-5.3-codex"</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXConfigValue")]
public sealed class CmdletSetIntelligenceXConfigValue : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

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

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        var jsonValue = JsonConversion.ToJsonValue(Value);
        await resolved.WriteConfigValueAsync(Key, jsonValue, CancelToken).ConfigureAwait(false);
    }
}
