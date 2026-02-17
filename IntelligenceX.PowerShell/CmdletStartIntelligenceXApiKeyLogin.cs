using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Authenticates the active client with an OpenAI API key.</para>
/// <para type="description">Stores the API key in the active client for API-based requests. Use only if ChatGPT OAuth
/// is not desired or available.</para>
/// <example>
///  <para>Log in with an API key</para>
///  <code>Start-IntelligenceXApiKeyLogin -ApiKey $env:OPENAI_API_KEY</code>
/// </example>
/// <example>
///  <para>Log in with an explicit client and verify account access</para>
///  <code>$client = Connect-IntelligenceX; Start-IntelligenceXApiKeyLogin -Client $client -ApiKey $env:OPENAI_API_KEY; Get-IntelligenceXAccount -Client $client</code>
/// </example>
/// <example>
///  <para>Use API key auth in non-interactive CI flows</para>
///  <code>Start-IntelligenceXApiKeyLogin -ApiKey (Get-Item Env:OPENAI_API_KEY).Value</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXApiKeyLogin")]
public sealed class CmdletStartIntelligenceXApiKeyLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">OpenAI API key used for authentication.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ApiKey { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        await resolved.LoginApiKeyAsync(ApiKey, CancelToken).ConfigureAwait(false);
    }
}
