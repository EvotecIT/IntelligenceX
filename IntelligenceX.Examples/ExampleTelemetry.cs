using System;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Examples;

internal sealed class ExampleTelemetry : IExample {
    public string Name => "telemetry";
    public string Description => "Subscribe to RPC/login events and run a basic chat.";

    public async Task RunAsync() {
        await using var client = await IntelligenceXClient.ConnectAsync().ConfigureAwait(false);
        client.RpcCallStarted += (_, args) => Console.WriteLine($"RPC -> {args.Method}");
        client.RpcCallCompleted += (_, args) => Console.WriteLine($"RPC <- {args.Method} ({args.Duration.TotalMilliseconds:0} ms)");
        client.LoginStarted += (_, args) => Console.WriteLine($"Login started: {args.LoginType}");
        client.LoginCompleted += (_, args) => Console.WriteLine($"Login completed: {args.LoginType}");
        client.StandardErrorReceived += (_, line) => Console.WriteLine($"STDERR: {line}");

        await client.EnsureChatGptLoginAsync(onUrl: url => Console.WriteLine($"Login URL: {url}")).ConfigureAwait(false);
        await client.ChatAsync("Hello from telemetry example.").ConfigureAwait(false);
    }
}
