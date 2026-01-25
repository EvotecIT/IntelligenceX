using System;
using System.Threading.Tasks;
using IntelligenceX.Copilot;

namespace IntelligenceX.Examples;

internal sealed class ExampleCopilotChat : IExample {
    public string Name => "copilot-chat";
    public string Description => "Connect to Copilot CLI and send a prompt.";

    public async Task RunAsync() {
        var options = new CopilotClientOptions();
        var cliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(cliPath)) {
            options.CliPath = cliPath;
        }
        var autoInstall = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL");
        if (string.Equals(autoInstall, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(autoInstall, "true", StringComparison.OrdinalIgnoreCase)) {
            options.AutoInstallCli = true;
        }

        await using var client = await CopilotClient.StartAsync(options).ConfigureAwait(false);
        var auth = await client.GetAuthStatusAsync().ConfigureAwait(false);
        if (!auth.IsAuthenticated) {
            Console.WriteLine("Copilot CLI is not authenticated. Run `copilot` to login first.");
            if (!string.IsNullOrWhiteSpace(auth.StatusMessage)) {
                Console.WriteLine(auth.StatusMessage);
            }
            return;
        }

        var session = await client.CreateSessionAsync(new CopilotSessionOptions {
            Model = "gpt-5"
        }).ConfigureAwait(false);

        var response = await session.SendAndWaitAsync(new CopilotMessageOptions {
            Prompt = "Say hello from Copilot CLI."
        }).ConfigureAwait(false);

        Console.WriteLine(response ?? "<no response>");
    }
}
