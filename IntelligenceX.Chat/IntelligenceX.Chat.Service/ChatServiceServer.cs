using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Service;

internal sealed class ChatServiceServer {
    private readonly ServiceOptions _options;
    private readonly ChatServiceToolingBootstrapCache _toolingBootstrapCache;

    public ChatServiceServer(ServiceOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _toolingBootstrapCache = new ChatServiceToolingBootstrapCache(
            ServiceOptions.GetDefaultToolingBootstrapCachePath(_options.StateDbPath));
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        Console.WriteLine("IntelligenceX Chat Service (Named Pipes)");
        Console.WriteLine($"Pipe: {_options.PipeName}");
        Console.WriteLine($"Model: {_options.Model}");
        Console.WriteLine();

        while (!cancellationToken.IsCancellationRequested) {
            await using var server = new NamedPipeServerStream(
                _options.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Console.WriteLine("Waiting for client...");
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Client connected.");

            var session = new ChatServiceSession(_options, server, _toolingBootstrapCache);
            try {
                await session.RunAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                Console.Error.WriteLine("Session canceled unexpectedly.");
            } catch (Exception ex) {
                Console.Error.WriteLine("Session failed: " + ex.Message);
            }

            Console.WriteLine("Client disconnected.");
        }
    }
}
