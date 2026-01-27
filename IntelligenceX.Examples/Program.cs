using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal static class Program {
    private static readonly List<IExample> Examples = new() {
        new ExampleEasyChat(),
        new ExampleEasySessionImages(),
        new ExampleModels(),
        new ExampleImagesAndFiles(),
        new ExampleChatGptLogin(),
        new ExampleApiKeyLogin(),
        new ExampleCopilotChat(),
        new ExampleChatLoop(),
        new ExampleThreadList(),
        new ExampleFluentChat(),
        new ExampleTelemetry()
    };

    private static async Task<int> Main(string[] args) {
        if (args.Length == 0) {
            PrintExamples();
            Console.Write("Select example by number: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var index)) {
                return await RunByIndex(index - 1).ConfigureAwait(false);
            }
            return 1;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase)) {
            PrintExamples();
            return 0;
        }

        if (int.TryParse(args[0], out var number)) {
            return await RunByIndex(number - 1).ConfigureAwait(false);
        }

        return await RunByName(args[0]).ConfigureAwait(false);
    }

    private static Task<int> RunByIndex(int index) {
        if (index < 0 || index >= Examples.Count) {
            Console.WriteLine("Invalid example index.");
            return Task.FromResult(1);
        }
        return RunExampleAsync(Examples[index]);
    }

    private static Task<int> RunByName(string name) {
        foreach (var example in Examples) {
            if (example.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                return RunExampleAsync(example);
            }
        }

        Console.WriteLine($"Unknown example '{name}'.");
        PrintExamples();
        return Task.FromResult(1);
    }

    private static async Task<int> RunExampleAsync(IExample example) {
        try {
            Console.WriteLine($"Running: {example.Name} - {example.Description}");
            await example.RunAsync().ConfigureAwait(false);
            return 0;
        } catch (Exception ex) {
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintExamples() {
        Console.WriteLine("Available examples:");
        for (var i = 0; i < Examples.Count; i++) {
            Console.WriteLine($"  {i + 1}. {Examples[i].Name} - {Examples[i].Description}");
        }
    }
}
