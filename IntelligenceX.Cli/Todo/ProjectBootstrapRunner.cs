using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class ProjectBootstrapRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const string WorkflowTemplateResourceName = "triage-project-sync.yml";
    private const string VisionTemplateResourceName = "vision-template.md";
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private const string DefaultConfigPath = "artifacts/triage/ix-project-config.json";
    private const string DefaultWorkflowPath = ".github/workflows/ix-triage-project-sync.yml";
    private const string DefaultVisionPath = "VISION.md";
    private const string DefaultControlIssueTitle = "IX Triage Control";
    private const double DefaultVisionDriftThreshold = 0.70;
    private const string ControlIssueVariableName = "IX_TRIAGE_CONTROL_ISSUE";

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
        public bool EnsureDefaultViews { get; set; } = true;
        public bool IncludePrWatchGovernanceViews { get; set; }
        public int? ViewTemplateProjectNumber { get; set; }
        public string? ViewTemplateOwner { get; set; }
        public string ConfigPath { get; set; } = DefaultConfigPath;
        public string WorkflowPath { get; set; } = DefaultWorkflowPath;
        public string VisionPath { get; set; } = DefaultVisionPath;
        public int MaxItems { get; set; } = 500;
        public double VisionDriftThreshold { get; set; } = DefaultVisionDriftThreshold;
        public bool SkipProjectInit { get; set; }
        public bool ForceWorkflowWrite { get; set; }
        public bool SkipVisionScaffold { get; set; }
        public bool ForceVisionWrite { get; set; }
        public int? ControlIssueNumber { get; set; }
        public bool CreateControlIssue { get; set; }
        public string ControlIssueTitle { get; set; } = DefaultControlIssueTitle;
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

        string workflowTemplate;
        try {
            workflowTemplate = ReadEmbeddedResource(WorkflowTemplateResourceName);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load workflow template: {ex.Message}");
            return 1;
        }

        var workflowYaml = RenderWorkflowTemplate(workflowTemplate, owner, projectNumber, options.MaxItems, options.VisionDriftThreshold);

        string visionTemplate;
        if (!options.SkipVisionScaffold) {
            try {
                visionTemplate = ReadEmbeddedResource(VisionTemplateResourceName);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to load vision template: {ex.Message}");
                return 1;
            }
        } else {
            visionTemplate = string.Empty;
        }

        var workflowPath = Path.GetFullPath(options.WorkflowPath);
        if (File.Exists(workflowPath) && !options.ForceWorkflowWrite) {
            Console.Error.WriteLine($"Workflow file already exists: {options.WorkflowPath}");
            Console.Error.WriteLine("Use --force-workflow-write to overwrite the existing file.");
            return 1;
        }

        WriteText(workflowPath, workflowYaml);

        string visionStatusMessage;
        if (options.SkipVisionScaffold) {
            visionStatusMessage = "Vision scaffold skipped (--skip-vision-scaffold).";
        } else {
            var visionPath = Path.GetFullPath(options.VisionPath);
            if (File.Exists(visionPath) && !options.ForceVisionWrite) {
                visionStatusMessage = $"Vision file kept (already exists): {options.VisionPath}";
            } else {
                var visionMarkdown = RenderVisionTemplate(visionTemplate, options.Repo, owner, projectNumber);
                WriteText(visionPath, visionMarkdown);
                visionStatusMessage = $"Vision file: {options.VisionPath}";
            }
        }

        var controlIssueStatus = "Control issue variable unchanged.";
        if (options.CreateControlIssue || options.ControlIssueNumber.HasValue) {
            var controlIssueNumber = options.ControlIssueNumber ?? 0;
            if (options.CreateControlIssue) {
                var createResult = await CreateControlIssueAsync(options.Repo, options.ControlIssueTitle, owner, projectNumber)
                    .ConfigureAwait(false);
                if (!createResult.Success) {
                    Console.Error.WriteLine(createResult.Error);
                    return 1;
                }
                controlIssueNumber = createResult.IssueNumber;
            }

            var variableError = await SetControlIssueVariableAsync(options.Repo, controlIssueNumber).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(variableError)) {
                Console.Error.WriteLine(variableError);
                return 1;
            }

            controlIssueStatus = $"Control issue configured: #{controlIssueNumber} ({ControlIssueVariableName}).";
        }

        Console.WriteLine(options.SkipProjectInit
            ? "Bootstrap workflow generated from existing project config."
            : "Project initialized and bootstrap workflow generated.");
        Console.WriteLine($"Project target: {owner}#{projectNumber}");
        Console.WriteLine($"Project config: {options.ConfigPath}");
        Console.WriteLine($"Workflow file: {options.WorkflowPath}");
        Console.WriteLine(visionStatusMessage);
        Console.WriteLine(controlIssueStatus);
        Console.WriteLine("Next step: commit generated files (workflow + vision) to enable scheduled GitHub-only triage sync.");
        return 0;
    }

    internal static string RenderWorkflowTemplate(string template, string owner, int projectNumber, int maxItems, double visionDriftThreshold) {
        var result = template;
        result = result.Replace("{{Owner}}", owner, StringComparison.Ordinal);
        result = result.Replace("{{ProjectNumber}}", projectNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        result = result.Replace("{{MaxItems}}", Math.Max(1, maxItems).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        result = result.Replace("{{VisionDriftThreshold}}", visionDriftThreshold.ToString("0.00", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return result;
    }

    internal static string RenderVisionTemplate(string template, string repo, string owner, int projectNumber) {
        var result = template;
        result = result.Replace("{{Repo}}", repo, StringComparison.Ordinal);
        result = result.Replace("{{Owner}}", owner, StringComparison.Ordinal);
        result = result.Replace("{{ProjectNumber}}", projectNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return result;
    }

    internal static string LoadWorkflowTemplate() {
        return ReadEmbeddedResource(WorkflowTemplateResourceName);
    }

    internal static string LoadVisionTemplate() {
        return ReadEmbeddedResource(VisionTemplateResourceName);
    }

    internal static string BuildControlIssueBody(string repo, string owner, int projectNumber) {
        var builder = new StringBuilder();
        builder.AppendLine("# IX Triage Control Plane");
        builder.AppendLine();
        builder.AppendLine("This issue is used by the scheduled IX triage workflow as a summary sink.");
        builder.AppendLine();
        builder.AppendLine($"- Repository: `{repo}`");
        builder.AppendLine($"- Project target: `{owner}#{projectNumber}`");
        builder.AppendLine($"- Variable: `{ControlIssueVariableName}`");
        builder.AppendLine();
        builder.AppendLine("## What appears here");
        builder.AppendLine();
        builder.AppendLine("- Triage index summary (duplicate clusters + best PR candidates)");
        builder.AppendLine("- Vision check summary (aligned / needs-human-review / likely-out-of-scope)");
        builder.AppendLine("- Links to each workflow run");
        builder.AppendLine();
        builder.AppendLine("## Maintainer flow");
        builder.AppendLine();
        builder.AppendLine("1. Review latest workflow comment.");
        builder.AppendLine("2. Open project board and filter by `IX Suggested Decision`, `Vision Fit`, and `Category`.");
        builder.AppendLine("3. Confirm human decision in `Maintainer Decision` and merge/close as needed.");
        builder.AppendLine();
        builder.AppendLine("_Managed by IntelligenceX project-bootstrap._");
        return builder.ToString().TrimEnd();
    }

    internal static bool TryParseIssueNumberFromGhOutput(string stdout, out int issueNumber) {
        issueNumber = 0;
        if (string.IsNullOrWhiteSpace(stdout)) {
            return false;
        }

        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines) {
            var line = rawLine.Trim();
            if (TryParseIssueNumberFromUrl(line, out issueNumber)) {
                return true;
            }

            if (TryParseTrailingInteger(line, out issueNumber)) {
                return true;
            }
        }

        return false;
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
        result.Add(options.EnsureDefaultViews ? "--ensure-default-views" : "--no-ensure-default-views");
        if (options.IncludePrWatchGovernanceViews) {
            result.Add("--include-pr-watch-governance-views");
        }

        if (options.ViewTemplateProjectNumber.HasValue && options.ViewTemplateProjectNumber.Value > 0) {
            result.Add("--view-template-project");
            result.Add(options.ViewTemplateProjectNumber.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(options.ViewTemplateOwner)) {
            result.Add("--view-template-owner");
            result.Add(options.ViewTemplateOwner.Trim());
        }

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
                case "--ensure-default-views":
                    options.EnsureDefaultViews = true;
                    break;
                case "--no-ensure-default-views":
                    options.EnsureDefaultViews = false;
                    break;
                case "--include-pr-watch-governance-views":
                    options.IncludePrWatchGovernanceViews = true;
                    break;
                case "--view-template-project":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var viewTemplateProjectNumber) &&
                        viewTemplateProjectNumber > 0) {
                        options.ViewTemplateProjectNumber = viewTemplateProjectNumber;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--view-template-owner":
                    if (i + 1 < args.Length) {
                        options.ViewTemplateOwner = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
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
                case "--vision-out":
                    if (i + 1 < args.Length) {
                        options.VisionPath = args[++i];
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
                case "--vision-drift-threshold":
                    if (i + 1 < args.Length &&
                        TryParseVisionDriftThreshold(args[++i], out var visionDriftThreshold)) {
                        options.VisionDriftThreshold = visionDriftThreshold;
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
                case "--skip-vision-scaffold":
                    options.SkipVisionScaffold = true;
                    break;
                case "--force-vision-write":
                    options.ForceVisionWrite = true;
                    break;
                case "--control-issue":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var controlIssueNumber) &&
                        controlIssueNumber > 0) {
                        options.ControlIssueNumber = controlIssueNumber;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--create-control-issue":
                    options.CreateControlIssue = true;
                    break;
                case "--control-issue-title":
                    if (i + 1 < args.Length) {
                        options.ControlIssueTitle = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
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

        if (options.CreateControlIssue && options.ControlIssueNumber.HasValue) {
            Console.Error.WriteLine("Choose either --control-issue <n> or --create-control-issue, not both.");
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (string.IsNullOrWhiteSpace(options.ControlIssueTitle)) {
            options.ControlIssueTitle = DefaultControlIssueTitle;
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
        Console.WriteLine("  --ensure-default-views    Validate default IX view set presence (default)");
        Console.WriteLine("  --no-ensure-default-views Skip default view coverage checks");
        Console.WriteLine("  --include-pr-watch-governance-views Add optional governance review view profile during project-init");
        Console.WriteLine("  --view-template-project <n> Copy from a template project to preserve saved views");
        Console.WriteLine("  --view-template-owner <login> Owner of --view-template-project");
        Console.WriteLine("  --config-out <path>       Project config path (default: artifacts/triage/ix-project-config.json)");
        Console.WriteLine("  --workflow-out <path>     Workflow output path (default: .github/workflows/ix-triage-project-sync.yml)");
        Console.WriteLine("  --vision-out <path>       Vision file output path (default: VISION.md)");
        Console.WriteLine("  --max-items <n>           Default max items for scheduled sync (default: 500)");
        Console.WriteLine("  --vision-drift-threshold <0-1> Default drift threshold injected into workflow (default: 0.70)");
        Console.WriteLine("  --skip-project-init       Do not run project-init; only render workflow from --config-out");
        Console.WriteLine("  --force-workflow-write    Overwrite existing workflow output file");
        Console.WriteLine("  --skip-vision-scaffold    Do not create/update VISION.md template");
        Console.WriteLine("  --force-vision-write      Overwrite existing VISION.md output file");
        Console.WriteLine("  --control-issue <n>       Set IX_TRIAGE_CONTROL_ISSUE to an existing issue number");
        Console.WriteLine("  --create-control-issue    Create a new control issue and set IX_TRIAGE_CONTROL_ISSUE");
        Console.WriteLine("  --control-issue-title     New control issue title (default: IX Triage Control)");
        Console.WriteLine();
        Console.WriteLine("Required token scopes: project (+ read:project for sync operations).");
        Console.WriteLine("For control issue automation, repository issue + variable write permissions are also required.");
    }

    private static bool TryParseVisionDriftThreshold(string input, out double threshold) {
        threshold = 0;
        if (!double.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) {
            return false;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed)) {
            return false;
        }

        if (parsed < 0.0 || parsed > 1.0) {
            return false;
        }

        threshold = parsed;
        return true;
    }

    private static bool TryReadProjectTarget(string configPath, out string owner, out int projectNumber, out string error) {
        if (!File.Exists(configPath)) {
            owner = string.Empty;
            projectNumber = 0;
            error = $"Project config file not found: {configPath}";
            return false;
        }

        try {
            return TryReadProjectTargetFromConfigJson(File.ReadAllText(configPath), out owner, out projectNumber, out error);
        } catch (Exception ex) {
            owner = string.Empty;
            projectNumber = 0;
            error = $"Failed to parse project config at {configPath}: {ex.Message}";
            return false;
        }
    }

    internal static bool TryReadProjectTargetFromConfigJson(string json, out string owner, out int projectNumber, out string error) {
        owner = string.Empty;
        projectNumber = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json)) {
            error = "Project config JSON is empty.";
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json);
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

            if (string.IsNullOrWhiteSpace(owner) || projectNumber <= 0) {
                error = "Project config JSON is missing owner/project number.";
                return false;
            }

            return true;
        } catch (Exception ex) {
            error = $"Failed to parse project config JSON: {ex.Message}";
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

    private static async Task<(bool Success, int IssueNumber, string Error)> CreateControlIssueAsync(
        string repo,
        string title,
        string owner,
        int projectNumber) {
        var body = BuildControlIssueBody(repo, owner, projectNumber);
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(90),
            "issue", "create",
            "--repo", repo,
            "--title", title,
            "--body", body
        ).ConfigureAwait(false);

        if (code != 0) {
            return (
                false,
                0,
                $"Failed to create control issue in '{repo}': {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
        }

        if (!TryParseIssueNumberFromGhOutput(stdout, out var issueNumber)) {
            return (
                false,
                0,
                "Control issue was created but issue number could not be parsed from `gh issue create` output.");
        }

        return (true, issueNumber, string.Empty);
    }

    private static async Task<string> SetControlIssueVariableAsync(string repo, int issueNumber) {
        var (code, _, stderr) = await GhCli.RunAsync(
            "variable", "set",
            ControlIssueVariableName,
            "--repo", repo,
            "--body", issueNumber.ToString(CultureInfo.InvariantCulture)
        ).ConfigureAwait(false);

        if (code == 0) {
            return string.Empty;
        }

        return $"Failed to set {ControlIssueVariableName} for '{repo}': {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}";
    }

    private static bool TryParseIssueNumberFromUrl(string value, out int issueNumber) {
        issueNumber = 0;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length; i++) {
            if (!segments[i].Equals("issues", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (int.TryParse(segments[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0) {
                issueNumber = number;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseTrailingInteger(string value, out int number) {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var end = value.Length - 1;
        while (end >= 0 && !char.IsDigit(value[end])) {
            end--;
        }
        if (end < 0) {
            return false;
        }

        var start = end;
        while (start >= 0 && char.IsDigit(value[start])) {
            start--;
        }
        start++;

        if (start > end) {
            return false;
        }

        return int.TryParse(value.AsSpan(start, end - start + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }
}
