using IntelligenceX.Copilot.Native;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    private IntelligenceXClientOptions BuildCopilotClientOptions() {
        string model = ResolveCopilotModel(_settings) ?? throw new InvalidOperationException(
            "Copilot requires an explicit copilot.model or review.model. Select an available model from the native model catalog.");
        var native = new CopilotNativeOptions {
            BaseUrl = _settings.CopilotBaseUrl ?? "https://api.githubcopilot.com/",
            GitHubToken = _settings.CopilotToken,
            RequestTimeout = TimeSpan.FromSeconds(Math.Max(1, _settings.CopilotRequestTimeoutSeconds)),
            Streaming = true
        };
        if (!string.IsNullOrWhiteSpace(_settings.CopilotTokenEnvironmentVariable)) {
            if (!string.IsNullOrWhiteSpace(native.GitHubToken)) throw new InvalidOperationException("Choose a Copilot token or a token environment variable, not both.");
            string variable = _settings.CopilotTokenEnvironmentVariable!;
            native.UseEnvironmentCredentials = false;
            native.TokenProvider = _ => Task.FromResult(Environment.GetEnvironmentVariable(variable)
                ?? throw new InvalidOperationException("The configured Copilot token environment variable is empty."));
            SecretsAudit.Record($"Copilot credential from {variable}");
        }
        return new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CopilotNative, DefaultModel = model, CopilotOptions = native,
            EnableUsageTelemetry = true,
            UsageTelemetrySessionFactory = new IntelligenceX.Telemetry.Usage.SqliteInternalIxUsageTelemetrySessionFactory()
        };
    }

    private async Task RunCopilotHealthCheckAsync(TimeSpan timeout, CancellationToken cancellationToken) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        await using var client = await IntelligenceXClient.ConnectAsync(BuildCopilotClientOptions(), deadline.Token).ConfigureAwait(false);
        _ = await client.ListModelsAsync(deadline.Token).ConfigureAwait(false);
    }

    private Task<string> RunCopilotAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) =>
        RunChatOnceAsync(BuildCopilotClientOptions(), prompt, onPartial, updateInterval, cancellationToken, captureSnapshot: null);

    internal IntelligenceXClientOptions BuildCopilotClientOptionsForTests() => BuildCopilotClientOptions();
}
