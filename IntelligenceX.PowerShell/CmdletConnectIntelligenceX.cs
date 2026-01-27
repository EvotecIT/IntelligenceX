using System;
using System.ComponentModel;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.Configuration;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Connects to IntelligenceX (native or app-server) and returns a client instance.</para>
/// </summary>
[Cmdlet(VerbsCommunications.Connect, "IntelligenceX")]
[OutputType(typeof(IntelligenceXClient))]
public sealed class CmdletConnectIntelligenceX : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Transport to use (Native or AppServer). Native uses ChatGPT OAuth directly.</para>
    /// </summary>
    [Parameter]
    public OpenAITransportKind Transport { get; set; } = OpenAITransportKind.Native;

    /// <summary>
    /// <para type="description">Path to the codex executable. Defaults to 'codex' on PATH.</para>
    /// </summary>
    [Parameter]
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// <para type="description">Arguments to pass to the app-server. Defaults to 'app-server'.</para>
    /// </summary>
    [Parameter]
    public string? Arguments { get; set; }

    /// <summary>
    /// <para type="description">Working directory for the app-server process.</para>
    /// </summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// <para type="description">Enable diagnostics output (RPC calls, login events, stderr).</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Diagnostics { get; set; }

    /// <summary>
    /// <para type="description">Ignore .intelligencex/config.json overrides.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter NoConfig { get; set; }

    protected override async Task ProcessRecordAsync() {
        var options = new IntelligenceXClientOptions();
        if (!NoConfig.IsPresent && IntelligenceXConfig.TryLoad(out var config)) {
            config.OpenAI.ApplyTo(options);
        }
        if (MyInvocation.BoundParameters.ContainsKey(nameof(Transport))) {
            options.TransportKind = Transport;
        }
        if (!string.IsNullOrWhiteSpace(ExecutablePath)) {
            options.AppServerOptions.ExecutablePath = ExecutablePath!;
        }
        if (!string.IsNullOrWhiteSpace(Arguments)) {
            options.AppServerOptions.Arguments = Arguments!;
        }
        if (!string.IsNullOrWhiteSpace(WorkingDirectory)) {
            options.AppServerOptions.WorkingDirectory = WorkingDirectory!;
        }

        IntelligenceXClient client;
        try {
            client = await IntelligenceXClient.ConnectAsync(options, CancelToken).ConfigureAwait(false);
        } catch (Win32Exception ex) when (options.TransportKind == OpenAITransportKind.AppServer) {
            var message =
                "Failed to start the Codex app-server. The executable was not found. " +
                "Install Codex CLI or pass -ExecutablePath to the app-server binary. " +
                "Alternatively use -Transport Native.";
            var error = new ErrorRecord(new InvalidOperationException(message, ex),
                "CodexAppServerNotFound", ErrorCategory.ResourceUnavailable, options.AppServerOptions.ExecutablePath);
            ThrowTerminatingError(error);
            return;
        } catch (InvalidOperationException ex) when (options.TransportKind == OpenAITransportKind.AppServer &&
                                                     ex.InnerException is Win32Exception win32) {
            var message =
                "Failed to start the Codex app-server. The executable was not found. " +
                "Install Codex CLI or pass -ExecutablePath to the app-server binary. " +
                "Alternatively use -Transport Native.";
            var error = new ErrorRecord(new InvalidOperationException(message, win32),
                "CodexAppServerNotFound", ErrorCategory.ResourceUnavailable, options.AppServerOptions.ExecutablePath);
            ThrowTerminatingError(error);
            return;
        }
        if (Diagnostics.IsPresent || MyInvocation.BoundParameters.ContainsKey("Verbose")) {
            ClientContext.Diagnostics?.Dispose();
            ClientContext.Diagnostics = new DiagnosticsSubscription(client, message => System.Console.Error.WriteLine(message));
        }
        SetDefaultClient(client);
        WriteObject(client);
    }
}
