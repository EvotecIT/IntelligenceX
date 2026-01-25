using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewRunner {
    private readonly ReviewSettings _settings;

    public ReviewRunner(ReviewSettings settings) {
        _settings = settings;
    }

    public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken) {
        var options = new IntelligenceXClientOptions {
            DefaultModel = _settings.Model
        };
        if (!string.IsNullOrWhiteSpace(_settings.CodexPath)) {
            options.AppServerOptions.ExecutablePath = _settings.CodexPath!;
        }
        if (!string.IsNullOrWhiteSpace(_settings.CodexArgs)) {
            options.AppServerOptions.Arguments = _settings.CodexArgs!;
        }
        if (!string.IsNullOrWhiteSpace(_settings.CodexWorkingDirectory)) {
            options.AppServerOptions.WorkingDirectory = _settings.CodexWorkingDirectory;
        }

        await using var client = await IntelligenceXClient.ConnectAsync(options, cancellationToken)
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
            Model = _settings.Model,
            NewThread = true
        };
        var input = ChatInput.FromText(prompt);
        var turn = await client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);

        var output = ExtractOutputs(turn.Outputs);
        if (!string.IsNullOrWhiteSpace(output)) {
            return output;
        }

        return await WaitForDeltasAsync(deltas, () => lastDelta, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> WaitForDeltasAsync(StringBuilder deltas, Func<DateTimeOffset> getLastDelta,
        CancellationToken cancellationToken) {
        var start = DateTimeOffset.UtcNow;
        var max = TimeSpan.FromSeconds(_settings.WaitSeconds);
        var idle = TimeSpan.FromSeconds(_settings.IdleSeconds);

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
        foreach (var output in outputs.Where(o => o.IsText)) {
            if (!string.IsNullOrWhiteSpace(output.Text)) {
                builder.AppendLine(output.Text);
            }
        }
        return builder.ToString().Trim();
    }
}
