using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Analysis;

namespace IntelligenceX.Cli.Ci;

internal static class CiRepositoryQualityCommand {
    private static readonly SemaphoreSlim ConsoleCaptureLock = new(1, 1);

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseArgs(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }
        if (options.Error is not null) {
            Console.Error.WriteLine(options.Error);
            return 1;
        }

        var workspace = Path.GetFullPath(options.Workspace ?? ".");
        var outDir = Path.GetFullPath(Path.Combine(workspace, options.OutDir ?? "artifacts"));
        Directory.CreateDirectory(outDir);

        var workflowInputs = LoadWorkflowDispatchInputs();
        var configPath = ResolvePath(workspace,
            options.ConfigPath ?? GetWorkflowInput(workflowInputs, "config_path") ?? ".intelligencex/reviewer.json");
        var baselinePath = ResolvePath(workspace,
            options.BaselinePath ?? GetWorkflowInput(workflowInputs, "baseline_path") ?? ".intelligencex/analysis-baseline.json");
        var framework = options.Framework ?? GetWorkflowInput(workflowInputs, "framework") ?? "net8.0";
        var strict = options.Strict ?? GetWorkflowBool(workflowInputs, "strict") ?? false;
        var gateNewOnly = options.GateNewOnly ?? GetWorkflowBool(workflowInputs, "gate_new_only") ?? true;

        var runLogPath = Path.Combine(outDir, "analysis-run.log");
        var gateLogPath = Path.Combine(outDir, "analysis-gate.log");
        var generatedBaselinePath = Path.Combine(outDir, "analysis-baseline.generated.json");
        var bootstrapBaselinePath = Path.Combine(outDir, "analysis-baseline.bootstrap.json");

        Console.WriteLine("IntelligenceX repository quality posture");
        Console.WriteLine($"- Workspace: {workspace}");
        Console.WriteLine($"- Config: {configPath}");
        Console.WriteLine($"- Baseline: {baselinePath}");
        Console.WriteLine($"- Framework: {framework}");
        Console.WriteLine($"- Strict: {strict.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
        Console.WriteLine($"- New-only gate: {gateNewOnly.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");

        if (!File.Exists(configPath)) {
            Console.Error.WriteLine($"Missing reviewer config: {configPath}");
            Console.Error.WriteLine("Run IntelligenceX setup before enabling repository quality posture.");
            WriteSummary(options, configPath, baselinePath, framework, gateNewOnly, gateLogPath);
            return 1;
        }

        var catalogExit = await RunAndCaptureAsync(
            () => AnalyzeRunner.RunAsync(new[] { "validate-catalog", "--workspace", workspace }),
            Path.Combine(outDir, "analysis-catalog.log")).ConfigureAwait(false);
        if (catalogExit != 0) {
            WriteSummary(options, configPath, baselinePath, framework, gateNewOnly, gateLogPath);
            return catalogExit;
        }

        var runArgs = new List<string> {
            "run",
            "--workspace", workspace,
            "--config", configPath,
            "--out", outDir,
            "--framework", framework
        };
        if (strict) {
            runArgs.Add("--strict");
        }

        var runExit = await RunAndCaptureAsync(() => AnalyzeRunner.RunAsync(runArgs.ToArray()), runLogPath)
            .ConfigureAwait(false);
        if (runExit != 0) {
            WriteSummary(options, configPath, baselinePath, framework, gateNewOnly, gateLogPath);
            return runExit;
        }

        var gateArgs = new List<string> {
            "gate",
            "--workspace", workspace,
            "--config", configPath,
            "--write-baseline", generatedBaselinePath
        };

        if (gateNewOnly) {
            if (File.Exists(baselinePath)) {
                gateArgs.Add("--new-only");
                gateArgs.Add("--baseline");
                gateArgs.Add(baselinePath);
            } else {
                Console.WriteLine($"No committed baseline found at {baselinePath}.");
                Console.WriteLine($"Writing bootstrap baseline artifact at {bootstrapBaselinePath} and passing this run when baseline generation succeeds.");
                var bootstrapExit = await RunAndCaptureAsync(
                        () => AnalyzeRunner.RunAsync(new[] {
                            "gate",
                            "--workspace", workspace,
                            "--config", configPath,
                            "--write-baseline", bootstrapBaselinePath
                        }),
                        gateLogPath)
                    .ConfigureAwait(false);
                WriteSummary(options, configPath, baselinePath, framework, gateNewOnly, gateLogPath);
                return bootstrapExit == 0 ? 0 : bootstrapExit;
            }
        }

        var gateExit = await RunAndCaptureAsync(() => AnalyzeRunner.RunAsync(gateArgs.ToArray()), gateLogPath)
            .ConfigureAwait(false);
        WriteSummary(options, configPath, baselinePath, framework, gateNewOnly, gateLogPath);
        return gateExit;
    }

    private static async Task<int> RunAndCaptureAsync(Func<Task<int>> run, string logPath) {
        var directory = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        TextWriter? originalOut = null;
        TextWriter? originalError = null;
        var builder = new StringBuilder();
        int exit;

        await ConsoleCaptureLock.WaitAsync().ConfigureAwait(false);
        try {
            originalOut = Console.Out;
            originalError = Console.Error;
            var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
            Console.SetOut(writer);
            Console.SetError(writer);

            exit = await run().ConfigureAwait(false);
        } finally {
            if (originalOut is not null) {
                Console.SetOut(originalOut);
            }
            if (originalError is not null) {
                Console.SetError(originalError);
            }
            ConsoleCaptureLock.Release();
        }

        var output = builder.ToString();
        await File.WriteAllTextAsync(logPath, output, Encoding.UTF8).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(output)) {
            Console.Write(output);
        }
        return exit;
    }

    private static void WriteSummary(Options options, string configPath, string baselinePath, string framework,
        bool gateNewOnly, string gateLogPath) {
        var summaryPath = ResolveSummaryPath(options);
        if (string.IsNullOrWhiteSpace(summaryPath)) {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("## IntelligenceX Repository Quality");
        builder.AppendLine();
        builder.AppendLine($"- Config: `{configPath}`");
        builder.AppendLine($"- Baseline: `{baselinePath}`");
        builder.AppendLine($"- New-only gate: `{gateNewOnly.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}`");
        builder.AppendLine($"- Framework: `{framework}`");
        builder.AppendLine();

        if (File.Exists(gateLogPath)) {
            builder.AppendLine("### Gate output");
            builder.AppendLine();
            builder.AppendLine("```text");
            foreach (var line in TailLines(gateLogPath, 80)) {
                builder.AppendLine(line);
            }
            builder.AppendLine("```");
        } else {
            builder.AppendLine("_No gate log was produced._");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(summaryPath));
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.AppendAllText(summaryPath, builder.ToString(), Encoding.UTF8);
    }

    private static IEnumerable<string> TailLines(string path, int maxLines) {
        var lines = File.ReadAllLines(path);
        return lines.Skip(Math.Max(0, lines.Length - maxLines));
    }

    private static string? ResolveSummaryPath(Options options) {
        if (!string.IsNullOrWhiteSpace(options.SummaryPath)) {
            return options.SummaryPath;
        }
        var envPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        return string.IsNullOrWhiteSpace(envPath) ? null : envPath;
    }

    private static string ResolvePath(string workspace, string value) {
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(workspace, value));
    }

    private static Dictionary<string, string> LoadWorkflowDispatchInputs() {
        var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath)) {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try {
            using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
            if (!document.RootElement.TryGetProperty("inputs", out var inputs) ||
                inputs.ValueKind != JsonValueKind.Object) {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in inputs.EnumerateObject()) {
                if (property.Value.ValueKind == JsonValueKind.String) {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) {
                        result[property.Name] = value.Trim();
                    }
                }
            }
            return result;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse GITHUB_EVENT_PATH inputs: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? GetWorkflowInput(IReadOnlyDictionary<string, string> inputs, string name) {
        return inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
    }

    private static bool? GetWorkflowBool(IReadOnlyDictionary<string, string> inputs, string name) {
        var value = GetWorkflowInput(inputs, name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (bool.TryParse(value, out var parsed)) {
            return parsed;
        }
        Console.Error.WriteLine($"Warning: ignoring invalid workflow input '{name}' value '{value}'. Expected true or false.");
        return null;
    }

    private static Options ParseArgs(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg.ToLowerInvariant()) {
                case "help":
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return options;
                case "--workspace":
                    options.Workspace = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--config":
                    options.ConfigPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--baseline":
                    options.BaselinePath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--framework":
                    options.Framework = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--strict":
                    options.Strict = ReadBoolValue(args, ref i, arg, options);
                    break;
                case "--gate-new-only":
                    options.GateNewOnly = ReadBoolValue(args, ref i, arg, options);
                    break;
                case "--out":
                    options.OutDir = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--summary":
                    options.SummaryPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                default:
                    options.Error = $"Unknown option '{arg}' for repository-quality.";
                    return options;
            }
        }
        return options;
    }

    private static bool ReadBoolValue(string[] args, ref int index, string name, Options options) {
        var value = ReadRequiredValue(args, ref index, name, options);
        if (options.Error is not null) {
            return false;
        }
        if (bool.TryParse(value, out var parsed)) {
            return parsed;
        }
        options.Error = $"Invalid value for {name}. Use true or false.";
        return false;
    }

    private static string? ReadRequiredValue(string[] args, ref int index, string name, Options options) {
        if (index + 1 >= args.Length) {
            options.Error = $"Missing value for {name}.";
            return null;
        }
        index++;
        var value = args[index];
        if (string.IsNullOrWhiteSpace(value)) {
            options.Error = $"Empty value for {name}.";
            return null;
        }
        return value.Trim();
    }

    private static void PrintHelp() {
        Console.WriteLine("Run repository-wide IntelligenceX quality posture in CI.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex ci repository-quality [--workspace <path>] [--config <path>] [--baseline <path>]");
        Console.WriteLine("      [--framework <tfm>] [--strict <true|false>] [--gate-new-only <true|false>] [--out <path>]");
    }

    private sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? BaselinePath { get; set; }
        public string? Framework { get; set; }
        public bool? Strict { get; set; }
        public bool? GateNewOnly { get; set; }
        public string? OutDir { get; set; }
        public string? SummaryPath { get; set; }
    }
}
