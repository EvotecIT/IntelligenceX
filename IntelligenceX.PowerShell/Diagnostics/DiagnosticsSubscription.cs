using System;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.Telemetry;

namespace IntelligenceX.PowerShell;

internal sealed class DiagnosticsSubscription : IDisposable {
    private readonly AppServerClient _client;
    private readonly Action<string> _writer;

    private readonly EventHandler<RpcCallStartedEventArgs> _rpcStarted;
    private readonly EventHandler<RpcCallCompletedEventArgs> _rpcCompleted;
    private readonly EventHandler<LoginEventArgs> _loginStarted;
    private readonly EventHandler<LoginEventArgs> _loginCompleted;
    private readonly EventHandler<string> _protocol;
    private readonly EventHandler<string> _stderr;

    public DiagnosticsSubscription(AppServerClient client, Action<string> writer) {
        _client = client;
        _writer = writer;

        _rpcStarted = (_, args) => _writer($"RPC -> {args.Method}");
        _rpcCompleted = (_, args) => _writer($"RPC <- {args.Method} ({args.Duration.TotalMilliseconds:0} ms)");
        _loginStarted = (_, args) => _writer($"Login started: {args.LoginType}");
        _loginCompleted = (_, args) => _writer($"Login completed: {args.LoginType}");
        _protocol = (_, line) => _writer($"RPC RAW: {line}");
        _stderr = (_, line) => _writer($"STDERR: {line}");

        _client.RpcCallStarted += _rpcStarted;
        _client.RpcCallCompleted += _rpcCompleted;
        _client.LoginStarted += _loginStarted;
        _client.LoginCompleted += _loginCompleted;
        _client.ProtocolLineReceived += _protocol;
        _client.StandardErrorReceived += _stderr;
    }

    public void Dispose() {
        _client.RpcCallStarted -= _rpcStarted;
        _client.RpcCallCompleted -= _rpcCompleted;
        _client.LoginStarted -= _loginStarted;
        _client.LoginCompleted -= _loginCompleted;
        _client.ProtocolLineReceived -= _protocol;
        _client.StandardErrorReceived -= _stderr;
    }
}
