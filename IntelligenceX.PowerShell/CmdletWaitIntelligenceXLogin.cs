using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Waits for the login flow to complete.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Wait, "IntelligenceXLogin")]
public sealed class CmdletWaitIntelligenceXLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Optional login identifier to wait for.</para>
    /// </summary>
    [Parameter]
    public string? LoginId { get; set; }

    /// <summary>
    /// <para type="description">Maximum wait time in seconds.</para>
    /// </summary>
    [Parameter]
    public int TimeoutSeconds { get; set; } = 300;

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(CancelToken, timeoutCts.Token);
        await resolved.WaitForLoginCompletionAsync(LoginId, linkedCts.Token).ConfigureAwait(false);
    }
}
