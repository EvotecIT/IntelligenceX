using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Models;

internal static class ModelsRunner {
    public static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex models list [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --json                Print JSON output");
        Console.WriteLine("  --account-id <id>     Select a specific ChatGPT account id");
        Console.WriteLine("  --auth-path <path>    Override auth store path");
        Console.WriteLine("  --auth-key <base64>   Override auth store encryption key");
        Console.WriteLine("  --model-url <url>     Add/override model endpoint URL (repeatable)");
    }

    public static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Length == 0 ? Array.Empty<string>() : args.Skip(1).ToArray();
            return await RunListAsync(rest).ConfigureAwait(false);
        }
        if (args[0] is "-h" or "--help" || args[0].Equals("help", StringComparison.OrdinalIgnoreCase)) {
            PrintHelp();
            return 0;
        }
        PrintHelp();
        return 1;
    }

    private static async Task<int> RunListAsync(string[] args) {
        ModelListOptions options;
        try {
            options = ModelListOptions.Parse(args);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            PrintHelp();
            return 1;
        }
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        try {
            var clientOptions = new IntelligenceXClientOptions {
                TransportKind = OpenAITransportKind.Native
            };
            if (!string.IsNullOrWhiteSpace(options.AccountId)) {
                clientOptions.NativeOptions.AuthAccountId = options.AccountId!.Trim();
            }
            if (!string.IsNullOrWhiteSpace(options.AuthPath) || !string.IsNullOrWhiteSpace(options.AuthKey)) {
                clientOptions.NativeOptions.AuthStore = new FileAuthBundleStore(options.AuthPath, options.AuthKey);
            }
            if (options.ModelUrls.Count > 0) {
                clientOptions.NativeOptions.ModelUrls = options.ModelUrls.ToArray();
            }

            await using var client = await IntelligenceXClient.ConnectAsync(clientOptions, CancellationToken.None).ConfigureAwait(false);
            var result = await client.ListModelsAsync(CancellationToken.None).ConfigureAwait(false);

            if (options.Json) {
                Console.WriteLine(JsonLite.Serialize(BuildJson(result)));
            } else {
                PrintText(result);
            }
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static JsonObject BuildJson(ModelListResult result) {
        var models = new JsonArray();
        foreach (var model in result.Models) {
            var item = new JsonObject()
                .Add("id", model.Id)
                .Add("model", model.Model)
                .Add("displayName", model.DisplayName)
                .Add("description", model.Description)
                .Add("isDefault", model.IsDefault)
                .Add("defaultReasoningEffort", model.DefaultReasoningEffort);
            var efforts = new JsonArray();
            foreach (var effort in model.SupportedReasoningEfforts) {
                efforts.Add(new JsonObject()
                    .Add("reasoningEffort", effort.ReasoningEffort)
                    .Add("description", effort.Description));
            }
            item.Add("supportedReasoningEfforts", efforts);
            models.Add(item);
        }
        return new JsonObject()
            .Add("items", models)
            .Add("nextCursor", result.NextCursor);
    }

    private static void PrintText(ModelListResult result) {
        Console.WriteLine("Available models");
        if (result.Models.Count == 0) {
            Console.WriteLine("  (none)");
            return;
        }

        var ordered = result.Models
            .OrderByDescending(static m => m.IsDefault)
            .ThenBy(static m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var model in ordered) {
            var id = string.IsNullOrWhiteSpace(model.Model) ? model.Id : model.Model;
            var suffix = model.IsDefault ? " [default]" : string.Empty;
            Console.WriteLine($"- {id}{suffix}");
            if (!string.IsNullOrWhiteSpace(model.DisplayName) &&
                !string.Equals(model.DisplayName, id, StringComparison.OrdinalIgnoreCase)) {
                Console.WriteLine($"  Name: {model.DisplayName}");
            }
            if (!string.IsNullOrWhiteSpace(model.DefaultReasoningEffort)) {
                Console.WriteLine($"  Default reasoning effort: {model.DefaultReasoningEffort}");
            }
            if (model.SupportedReasoningEfforts.Count > 0) {
                var values = model.SupportedReasoningEfforts
                    .Select(static e => e.ReasoningEffort)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                if (values.Length > 0) {
                    Console.WriteLine($"  Supported reasoning efforts: {string.Join(", ", values)}");
                }
            }
            if (!string.IsNullOrWhiteSpace(model.Description)) {
                Console.WriteLine($"  Description: {model.Description}");
            }
        }
    }
}

internal sealed class ModelListOptions {
    public bool ShowHelp { get; set; }
    public bool Json { get; set; }
    public string? AccountId { get; set; }
    public string? AuthPath { get; set; }
    public string? AuthKey { get; set; }
    public List<string> ModelUrls { get; } = new();

    public static ModelListOptions Parse(string[] args) {
        var options = new ModelListOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg is "-h" or "--help") {
                options.ShowHelp = true;
                return options;
            }
            switch (arg) {
                case "--json":
                    options.Json = true;
                    break;
                case "--account-id":
                    options.AccountId = ReadValue(args, ref i);
                    break;
                case "--auth-path":
                    options.AuthPath = ReadValue(args, ref i);
                    break;
                case "--auth-key":
                    options.AuthKey = ReadValue(args, ref i);
                    break;
                case "--model-url":
                    options.ModelUrls.Add(ReadValue(args, ref i));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option or unexpected argument: {arg}");
            }
        }
        return options;
    }

    private static string ReadValue(string[] args, ref int index) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"Missing value for {args[index]}.");
        }
        index++;
        return args[index];
    }
}
