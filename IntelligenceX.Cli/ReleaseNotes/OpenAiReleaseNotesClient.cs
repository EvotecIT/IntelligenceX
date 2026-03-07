using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Cli.ReleaseNotes;

internal static class OpenAiReleaseNotesClient {
    public static async Task<string> GenerateAsync(string prompt, ReleaseNotesOptions options, CancellationToken cancellationToken) {
        var attempts = Math.Max(1, options.RetryCount);
        var delaySeconds = Math.Max(1, options.RetryDelaySeconds);
        var maxDelaySeconds = Math.Max(delaySeconds, options.RetryMaxDelaySeconds);
        var delay = TimeSpan.FromSeconds(delaySeconds);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++) {
            try {
                return await GenerateOnceAsync(prompt, options, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (IsTransient(ex) && attempt < attempts && !cancellationToken.IsCancellationRequested) {
                lastError = ex;
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800));
                var wait = delay + jitter;
                Console.Error.WriteLine($"OpenAI request failed (attempt {attempt}/{attempts}): {ex.Message}. Retrying in {wait.TotalSeconds:0.0}s.");
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                var nextDelaySeconds = Math.Min(maxDelaySeconds, delay.TotalSeconds * 2);
                delay = TimeSpan.FromSeconds(nextDelaySeconds);
            }
        }

        if (lastError is not null) {
            throw lastError;
        }
        return string.Empty;
    }

    private static async Task<string> GenerateOnceAsync(string prompt, ReleaseNotesOptions options, CancellationToken cancellationToken) {
        var model = options.Model
                    ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
                    ?? OpenAIModelCatalog.DefaultModel;
        var transport = options.Transport
                        ?? ReleaseNotesOptions.ParseTransportValue(Environment.GetEnvironmentVariable("OPENAI_TRANSPORT"))
                        ?? OpenAITransportKind.AppServer;

        var clientOptions = new IntelligenceXClientOptions {
            DefaultModel = model,
            TransportKind = transport
        };

        if (clientOptions.TransportKind == OpenAITransportKind.AppServer) {
            var codexPath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH");
            var codexArgs = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS");
            var codexCwd = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_CWD");
            if (!string.IsNullOrWhiteSpace(codexPath)) {
                clientOptions.AppServerOptions.ExecutablePath = codexPath;
            }
            if (!string.IsNullOrWhiteSpace(codexArgs)) {
                clientOptions.AppServerOptions.Arguments = codexArgs;
            }
            if (!string.IsNullOrWhiteSpace(codexCwd)) {
                clientOptions.AppServerOptions.WorkingDirectory = codexCwd;
            }
        }

        await using var client = await IntelligenceXClient.ConnectAsync(clientOptions, cancellationToken)
            .ConfigureAwait(false);

        var deltas = new StringBuilder();
        var lastDelta = DateTimeOffset.UtcNow;
        using var subscription = client.SubscribeDelta(text => {
            if (!string.IsNullOrWhiteSpace(text)) {
                lock (deltas) {
                    deltas.Append(text);
                    lastDelta = DateTimeOffset.UtcNow;
                }
            }
        });

        var chatOptions = new ChatOptions {
            Model = model,
            NewThread = true,
            ReasoningEffort = options.ReasoningEffort,
            ReasoningSummary = options.ReasoningSummary
        };

        var input = ChatInput.FromText(prompt);
        var turn = await client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);
        var output = ExtractOutputs(turn.Outputs);
        if (!string.IsNullOrWhiteSpace(output)) {
            return output;
        }

        return await WaitForDeltasAsync(deltas, () => lastDelta, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> WaitForDeltasAsync(StringBuilder deltas, Func<DateTimeOffset> getLastDelta,
        CancellationToken cancellationToken) {
        var start = DateTimeOffset.UtcNow;
        var max = TimeSpan.FromSeconds(90);
        var idle = TimeSpan.FromSeconds(3);

        while (DateTimeOffset.UtcNow - start < max) {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var last = getLastDelta();
            if (DateTimeOffset.UtcNow - last > idle) {
                break;
            }
        }

        lock (deltas) {
            return deltas.ToString();
        }
    }

    private static string ExtractOutputs(IReadOnlyList<TurnOutput> outputs) {
        if (outputs.Count == 0) {
            return string.Empty;
        }
        var builder = new StringBuilder();
        foreach (var output in outputs.Where(o => o.IsText && !string.IsNullOrWhiteSpace(o.Text))) {
            builder.AppendLine(output.Text);
        }
        return builder.ToString().Trim();
    }

    private static bool IsTransient(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (ex is HttpRequestException || ex is IOException || ex is TimeoutException) {
            return true;
        }
        return ex.InnerException is not null && IsTransient(ex.InnerException);
    }
}
