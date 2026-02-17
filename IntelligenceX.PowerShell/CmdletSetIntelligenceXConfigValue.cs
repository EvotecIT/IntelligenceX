using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Writes a single app-server configuration value.</para>
/// <para type="description">Updates one config key on the app-server. PowerShell values are converted to JSON before sending.
/// Use <c>Get-IntelligenceXConfig</c> to verify the effective result and layer origin.</para>
/// <example>
///  <para>Set the model used by default</para>
///  <code>Set-IntelligenceXConfigValue -Key "model" -Value "gpt-5.3-codex"</code>
/// </example>
/// <example>
///  <para>Set a boolean or numeric value</para>
///  <code>Set-IntelligenceXConfigValue -Key "stream" -Value $true; Set-IntelligenceXConfigValue -Key "maxOutputTokens" -Value 4096</code>
/// </example>
/// <example>
///  <para>Write a nested object using a hashtable</para>
///  <code>Set-IntelligenceXConfigValue -Key "responseFormat" -Value @{ type = "json_schema"; strict = $true }</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Set, "IntelligenceXConfigValue")]
public sealed class CmdletSetIntelligenceXConfigValue : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">App-server client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Configuration key to write.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Configuration value. Converted to JSON (string, number, boolean, array, object).</para>
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
