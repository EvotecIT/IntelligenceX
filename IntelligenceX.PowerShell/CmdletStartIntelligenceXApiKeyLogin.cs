using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Authenticates using an OpenAI API key.</para>
/// <para type="description">Stores the API key in the active client for API-based requests. Use only if ChatGPT OAuth
/// is not desired or available.</para>
/// <example>
///  <para>Log in with an API key</para>
///  <code>Start-IntelligenceXApiKeyLogin -ApiKey $env:OPENAI_API_KEY</code>
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
    /// <para type="description">API key to authenticate with.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ApiKey { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        await resolved.LoginApiKeyAsync(ApiKey, CancelToken).ConfigureAwait(false);
    }
}
