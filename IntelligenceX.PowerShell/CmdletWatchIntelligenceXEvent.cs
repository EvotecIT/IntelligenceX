using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.Rpc;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Watches JSON-RPC notifications from the app-server.</para>
/// </summary>
[Cmdlet(VerbsCommon.Watch, "IntelligenceXEvent")]
[OutputType(typeof(RpcNotificationRecord))]
public sealed class CmdletWatchIntelligenceXEvent : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Optional method filter.</para>
    /// </summary>
    [Parameter]
    public string[]? Method { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
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
