using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class ProjectInitRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private const string DefaultDescription = "IntelligenceX triage and vision control plane for PR and issue backlog.";

    private sealed class Options {
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public string? Owner { get; set; }
        public int? ProjectNumber { get; set; }
        public string Title { get; set; } = "IX Triage Control";
        public string? Description { get; set; }
        public bool DescriptionSpecified { get; set; }
        public bool IsPublic { get; set; }
        public bool VisibilitySpecified { get; set; }
        public bool LinkRepo { get; set; } = true;
        public string OutputPath { get; set; } = Path.Combine("artifacts", "triage", "ix-project-config.json");
        public bool ShowHelp { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        var owner = ResolveOwner(options);
        var client = new ProjectV2Client();

        ProjectV2Client.ProjectRef project;
        var createdProject = false;
        try {
            if (options.ProjectNumber.HasValue) {
                var existing = await client.TryGetProjectAsync(owner, options.ProjectNumber.Value).ConfigureAwait(false);
                if (existing is null) {
                    Console.Error.WriteLine($"Project {options.ProjectNumber.Value} was not found for owner '{owner}'.");
                    return 1;
                }
                project = existing;
            } else {
                var ownerRef = await client.GetOwnerAsync(owner).ConfigureAwait(false);
                project = await client.CreateProjectAsync(ownerRef.NodeId, options.Title).ConfigureAwait(false);
                createdProject = true;
            }
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var description = options.DescriptionSpecified ? options.Description : (createdProject ? DefaultDescription : null);
        bool? isPublic = null;
        if (options.VisibilitySpecified) {
            isPublic = options.IsPublic;
        } else if (createdProject) {
            isPublic = false;
        }

        try {
            await client.UpdateProjectAsync(project.Id, description, isPublic).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to update project metadata: {ex.Message}");
            return 1;
        }

        IReadOnlyDictionary<string, ProjectV2Client.ProjectField> fields;
        try {
            fields = await EnsureFieldsAsync(client, owner, project.Number).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (options.LinkRepo) {
            var (code, _, stderr) = await GhCli.RunAsync(
                "project", "link",
                project.Number.ToString(CultureInfo.InvariantCulture),
                "--owner", owner,
                "--repo", options.Repo
            ).ConfigureAwait(false);
            if (code != 0) {
                Console.Error.WriteLine($"Warning: failed to link project to repo '{options.Repo}'.");
                if (!string.IsNullOrWhiteSpace(stderr)) {
                    Console.Error.WriteLine(stderr.Trim());
                }
            }
        }

        var report = BuildConfigReport(owner, options.Repo, project, fields);
        WriteText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Project ready: #{project.Number} ({project.Url})");
        Console.WriteLine($"Fields ensured: {fields.Count}");
        Console.WriteLine($"Project config written: {options.OutputPath}");
        return 0;
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
                    }
                    break;
                case "--owner":
                    if (i + 1 < args.Length) {
                        options.Owner = args[++i];
                    }
                    break;
                case "--project":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                        number > 0) {
                        options.ProjectNumber = number;
                    }
                    break;
                case "--title":
                    if (i + 1 < args.Length) {
                        options.Title = args[++i];
                    }
                    break;
                case "--description":
                    if (i + 1 < args.Length) {
                        options.Description = args[++i];
                        options.DescriptionSpecified = true;
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
                case "--out":
                    if (i + 1 < args.Length) {
                        options.OutputPath = args[++i];
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ShowHelp = true;
        }
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo project-init [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>      Repository for project linkage (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --owner <login>          Project owner login (defaults to repo owner)");
        Console.WriteLine("  --project <n>            Existing project number to initialize (skip create)");
        Console.WriteLine("  --title <text>           New project title when creating (default: IX Triage Control)");
        Console.WriteLine("  --description <text>     Project short description");
        Console.WriteLine("  --public                 Set project visibility to public");
        Console.WriteLine("  --private                Set project visibility to private (default)");
        Console.WriteLine("  --link-repo              Link project to --repo (default)");
        Console.WriteLine("  --no-link-repo           Do not link project to repo");
        Console.WriteLine("  --out <path>             Write project config JSON (default: artifacts/triage/ix-project-config.json)");
        Console.WriteLine();
        Console.WriteLine("Required token scopes for project setup: `project`.");
    }

    private static string ResolveOwner(Options options) {
        if (!string.IsNullOrWhiteSpace(options.Owner)) {
            return options.Owner.Trim();
        }
        var parts = options.Repo.Split('/', 2);
        return parts[0];
    }

    private static async Task<IReadOnlyDictionary<string, ProjectV2Client.ProjectField>> EnsureFieldsAsync(
        ProjectV2Client client,
        string owner,
        int projectNumber) {
        var fields = await client.GetProjectFieldsByNameAsync(owner, projectNumber).ConfigureAwait(false);
        foreach (var field in ProjectFieldCatalog.DefaultFields) {
            if (fields.ContainsKey(field.Name)) {
                continue;
            }
            await CreateFieldAsync(owner, projectNumber, field).ConfigureAwait(false);
        }
        return await client.GetProjectFieldsByNameAsync(owner, projectNumber).ConfigureAwait(false);
    }

    private static async Task CreateFieldAsync(string owner, int projectNumber, ProjectFieldDefinition field) {
        var args = new List<string> {
            "project", "field-create",
            projectNumber.ToString(CultureInfo.InvariantCulture),
            "--owner", owner,
            "--name", field.Name,
            "--data-type", field.DataType
        };
        if (field.DataType.Equals("SINGLE_SELECT", StringComparison.OrdinalIgnoreCase) &&
            field.SingleSelectOptions.Count > 0) {
            args.Add("--single-select-options");
            args.Add(string.Join(",", field.SingleSelectOptions));
        }

        var (code, _, stderr) = await GhCli.RunAsync(args.ToArray()).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(
                $"Failed to create field '{field.Name}' in project #{projectNumber}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
        }
    }

    private static object BuildConfigReport(
        string owner,
        string repo,
        ProjectV2Client.ProjectRef project,
        IReadOnlyDictionary<string, ProjectV2Client.ProjectField> fields) {
        var fieldsJson = fields.Values
            .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(field => new {
                id = field.Id,
                name = field.Name,
                dataType = field.DataType,
                options = field.OptionsByName
                    .OrderBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(option => new {
                        name = option.Key,
                        id = option.Value
                    })
                    .ToList()
            })
            .ToList();

        return new {
            schema = "intelligencex.project-config.v1",
            generatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            owner,
            repo,
            project = new {
                id = project.Id,
                number = project.Number,
                title = project.Title,
                url = project.Url
            },
            fields = fieldsJson
        };
    }

    private static void WriteText(string path, string content) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }
}
