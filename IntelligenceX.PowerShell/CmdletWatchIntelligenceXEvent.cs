using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.Rpc;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Watches JSON-RPC notifications from the app-server.</para>
/// <para type="description">Streams notification events until the cmdlet is cancelled. Optionally filter by method.</para>
/// <example>
///  <para>Watch all events</para>
///  <code>Watch-IntelligenceXEvent</code>
/// </example>
/// <example>
///  <para>Watch only turn deltas</para>
///  <code>Watch-IntelligenceXEvent -Method "turn/delta"</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Watch, "IntelligenceXEvent")]
[OutputType(typeof(RpcNotificationRecord))]
public sealed class CmdletWatchIntelligenceXEvent : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Optional method filter.</para>
    /// </summary>
    [Parameter]
    public string[]? Method { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        void Handler(object? sender, JsonRpcNotificationEventArgs args) {
            if (Method is not null && Method.Length > 0) {
                var matched = false;
                foreach (var method in Method) {
                    if (string.Equals(method, args.Method, StringComparison.OrdinalIgnoreCase)) {
                        matched = true;
                        break;
                    }
                }
                if (!matched) {
                    return;
                }
            }
            WriteObject(new RpcNotificationRecord(args.Method, args.Params));
        }

        resolved.NotificationReceived += Handler;
        try {
            await Task.Delay(Timeout.Infinite, CancelToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Cmdlet stopped.
        } finally {
            resolved.NotificationReceived -= Handler;
        }
    }
}
