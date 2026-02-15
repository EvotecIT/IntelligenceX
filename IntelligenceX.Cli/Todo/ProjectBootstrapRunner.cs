using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Todo;

internal static class ProjectBootstrapRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const string TemplateResourceName = "triage-project-sync.yml";
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private const string DefaultConfigPath = "artifacts/triage/ix-project-config.json";
    private const string DefaultWorkflowPath = ".github/workflows/ix-triage-project-sync.yml";

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public string? Owner { get; set; }
        public int? ProjectNumber { get; set; }
        public string Title { get; set; } = "IX Triage Control";
        public string? Description { get; set; }
        public bool DescriptionSpecified { get; set; }
        public bool IsPublic { get; set; }
        public bool VisibilitySpecified { get; set; }
        public bool LinkRepo { get; set; } = true;
        public string ConfigPath { get; set; } = DefaultConfigPath;
        public string WorkflowPath { get; set; } = DefaultWorkflowPath;
        public int MaxItems { get; set; } = 500;
        public bool SkipProjectInit { get; set; }
        public bool ForceWorkflowWrite { get; set; }
        public bool ShowHelp { get; set; }
        public bool ParseFailed { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return options.ParseFailed ? 1 : 0;
        }

        if (!options.SkipProjectInit) {
            var initExitCode = await ProjectInitRunner.RunAsync(BuildProjectInitArgs(options)).ConfigureAwait(false);
            if (initExitCode != 0) {
                return initExitCode;
            }
        }

        if (!TryReadProjectTarget(options.ConfigPath, out var owner, out var projectNumber, out var readError)) {
            Console.Error.WriteLine(readError);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(owner) || projectNumber <= 0) {
            Console.Error.WriteLine("Unable to resolve owner/project from project config.");
            return 1;
        }

        string template;
        try {
            template = ReadEmbeddedResource(TemplateResourceName);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load workflow template: {ex.Message}");
            return 1;
        }

        var workflowYaml = RenderWorkflowTemplate(template, owner, projectNumber, options.MaxItems);

        var workflowPath = Path.GetFullPath(options.WorkflowPath);
        if (File.Exists(workflowPath) && !options.ForceWorkflowWrite) {
            Console.Error.WriteLine($"Workflow file already exists: {options.WorkflowPath}");
            Console.Error.WriteLine("Use --force-workflow-write to overwrite the existing file.");
            return 1;
        }

        WriteText(workflowPath, workflowYaml);

        Console.WriteLine(options.SkipProjectInit
            ? "Bootstrap workflow generated from existing project config."
            : "Project initialized and bootstrap workflow generated.");
        Console.WriteLine($"Project target: {owner}#{projectNumber}");
        Console.WriteLine($"Project config: {options.ConfigPath}");
        Console.WriteLine($"Workflow file: {options.WorkflowPath}");
        Console.WriteLine("Next step: commit the workflow file to enable scheduled GitHub-only triage sync.");
        return 0;
    }

    internal static string RenderWorkflowTemplate(string template, string owner, int projectNumber, int maxItems) {
        var result = template;
        result = result.Replace("{{Owner}}", owner, StringComparison.Ordinal);
        result = result.Replace("{{ProjectNumber}}", projectNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        result = result.Replace("{{MaxItems}}", Math.Max(1, maxItems).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return result;
    }

    private static string[] BuildProjectInitArgs(Options options) {
        var result = new List<string> {
            "--repo", options.Repo,
            "--title", options.Title,
            "--out", options.ConfigPath
        };

        if (!string.IsNullOrWhiteSpace(options.Owner)) {
            result.Add("--owner");
            result.Add(options.Owner.Trim());
        }

        if (options.ProjectNumber.HasValue && options.ProjectNumber.Value > 0) {
            result.Add("--project");
            result.Add(options.ProjectNumber.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (options.DescriptionSpecified) {
            result.Add("--description");
            result.Add(options.Description ?? string.Empty);
        }

        if (options.VisibilitySpecified) {
            result.Add(options.IsPublic ? "--public" : "--private");
        }

        result.Add(options.LinkRepo ? "--link-repo" : "--no-link-repo");
        return result.ToArray();
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();

        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--owner":
                    if (i + 1 < args.Length) {
                        options.Owner = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--project":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var projectNumber) &&
                        projectNumber > 0) {
                        options.ProjectNumber = projectNumber;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--title":
                    if (i + 1 < args.Length) {
                        options.Title = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--description":
                    if (i + 1 < args.Length) {
                        options.Description = args[++i];
                        options.DescriptionSpecified = true;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--public":
                    options.IsPublic = true;
                    options.VisibilitySpecified = true;
                    break;
                case "--private":
                    options.IsPublic = false;
                    options.VisibilitySpecified = true;
                    break;
                case "--link-repo":
                    options.LinkRepo = true;
                    break;
                case "--no-link-repo":
                    options.LinkRepo = false;
                    break;
                case "--config-out":
                    if (i + 1 < args.Length) {
                        options.ConfigPath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--workflow-out":
                    if (i + 1 < args.Length) {
                        options.WorkflowPath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--max-items":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxItems) &&
                        maxItems > 0) {
                        options.MaxItems = Math.Min(maxItems, 5000);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--skip-project-init":
                    options.SkipProjectInit = true;
                    break;
                case "--force-workflow-write":
                    options.ForceWorkflowWrite = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ParseFailed = true;
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo project-bootstrap [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>       Repository context (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --owner <login>           Project owner login (defaults to repo owner)");
        Console.WriteLine("  --project <n>             Existing project number to initialize");
        Console.WriteLine("  --title <text>            New project title when creating (default: IX Triage Control)");
        Console.WriteLine("  --description <text>      Project short description");
        Console.WriteLine("  --public                  Set project visibility to public");
        Console.WriteLine("  --private                 Set project visibility to private (default)");
        Console.WriteLine("  --link-repo               Link project to --repo (default)");
        Console.WriteLine("  --no-link-repo            Do not link project to repo");
        Console.WriteLine("  --config-out <path>       Project config path (default: artifacts/triage/ix-project-config.json)");
        Console.WriteLine("  --workflow-out <path>     Workflow output path (default: .github/workflows/ix-triage-project-sync.yml)");
        Console.WriteLine("  --max-items <n>           Default max items for scheduled sync (default: 500)");
        Console.WriteLine("  --skip-project-init       Do not run project-init; only render workflow from --config-out");
        Console.WriteLine("  --force-workflow-write    Overwrite existing workflow output file");
        Console.WriteLine();
        Console.WriteLine("Required token scopes: project (+ read:project for sync operations).");
    }

    private static bool TryReadProjectTarget(string configPath, out string owner, out int projectNumber, out string error) {
        owner = string.Empty;
        projectNumber = 0;
        error = string.Empty;

        if (!File.Exists(configPath)) {
            error = $"Project config file not found: {configPath}";
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;

            if (TryGetProperty(root, "owner", out var ownerProp) && ownerProp.ValueKind == JsonValueKind.String) {
                owner = ownerProp.GetString() ?? string.Empty;
            }

            if (TryGetProperty(root, "project", out var projectObj) &&
                projectObj.ValueKind == JsonValueKind.Object &&
                TryGetProperty(projectObj, "number", out var numberProp) &&
                numberProp.ValueKind == JsonValueKind.Number &&
                numberProp.TryGetInt32(out var numberValue)) {
                projectNumber = numberValue;
            }

            return true;
        } catch (Exception ex) {
            error = $"Failed to parse project config at {configPath}: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return element.TryGetProperty(name, out value);
    }

    private static string ReadEmbeddedResource(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) {
            throw new InvalidOperationException($"Embedded template not found: {resourceName}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void WriteText(string path, string content) {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, content, Utf8NoBom);
    }
}
