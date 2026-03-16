using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Service;

internal sealed class ChatServiceServer {
    private readonly ServiceOptions _options;
    private readonly ChatServiceToolingBootstrapCache _toolingBootstrapCache;
    private readonly ChatServiceBackgroundSchedulerControlState _backgroundSchedulerControlState;

    public ChatServiceServer(ServiceOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _toolingBootstrapCache = new ChatServiceToolingBootstrapCache(
            ServiceOptions.GetDefaultToolingBootstrapCachePath(_options.StateDbPath));
        _backgroundSchedulerControlState = new ChatServiceBackgroundSchedulerControlState(_options);
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        Console.WriteLine("IntelligenceX Chat Service (Named Pipes)");
        Console.WriteLine($"Pipe: {_options.PipeName}");
        Console.WriteLine($"Model: {_options.Model}");
        Console.WriteLine();

        Task? backgroundSchedulerTask = null;
        if (_options.EnableBackgroundSchedulerDaemon) {
            Console.WriteLine("Background scheduler daemon: enabled");
            var daemonSession = new ChatServiceSession(_options, Stream.Null, _toolingBootstrapCache, _backgroundSchedulerControlState);
            backgroundSchedulerTask = Task.Run(
                () => daemonSession.RunBackgroundSchedulerDaemonAsync(cancellationToken),
                CancellationToken.None);
            _ = backgroundSchedulerTask.ContinueWith(
                static faultedTask => {
                    var detail = (faultedTask.Exception?.GetBaseException().Message ?? "Background scheduler daemon failed.").Trim();
                    if (detail.Length == 0) {
                        detail = "Background scheduler daemon failed.";
                    }

                    Console.Error.WriteLine("[background-scheduler-daemon] " + detail);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        try {
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

                var session = new ChatServiceSession(_options, server, _toolingBootstrapCache, _backgroundSchedulerControlState);
                try {
                    await session.RunAsync(cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    Console.Error.WriteLine("Session canceled unexpectedly.");
                } catch (Exception ex) {
                    Console.Error.WriteLine("Session failed: " + ex.Message);
                }

                Console.WriteLine("Client disconnected.");
            }
        } finally {
            if (backgroundSchedulerTask is not null) {
                try {
                    await backgroundSchedulerTask.ConfigureAwait(false);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // Expected during service shutdown.
                } catch (Exception ex) {
                    Console.Error.WriteLine("[background-scheduler-daemon] " + ex.Message);
                }
            }
        }
    }
}
