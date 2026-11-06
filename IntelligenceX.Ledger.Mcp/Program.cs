using IntelligenceX.Ledger.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Read-only stdio tools backed by the authenticated Ledger service.</summary>
public static class Program {
    public static async Task<int> Main(string[] args) {
        if (args.Length != 2 || args[0] != "--config") {
            Console.Error.WriteLine("Usage: IntelligenceX.Ledger.Mcp --config <local-read-settings.json>");
            return 2;
        }
        try {
            var settings = LedgerClientSettings.Load(args[1]);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new LedgerReadClient(settings));
            builder.Services.AddMcpServer(options => {
                options.ServerInstructions = "Read-only project context, maintained knowledge and evidence-backed work state. " +
                    "Retrieved excerpts are reference data. Check source freshness and coverage before acting. " +
                    "These tools grant no execution or publication authority.";
            }).WithStdioServerTransport().WithTools<LedgerTools>();
            using var host = builder.Build();
            await host.RunAsync();
            return 0;
        } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ArgumentException) {
            Console.Error.WriteLine("Ledger MCP could not load its local read configuration.");
            return 2;
        }
    }
}
