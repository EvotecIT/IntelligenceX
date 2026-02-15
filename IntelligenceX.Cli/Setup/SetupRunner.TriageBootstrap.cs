using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Todo;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private const string DefaultTriageProjectConfigPath = "artifacts/triage/ix-project-config.json";
    private const string DefaultTriageWorkflowPath = ".github/workflows/ix-triage-project-sync.yml";
    private const string DefaultVisionPath = "VISION.md";
    private const string DefaultControlIssueTitle = "IX Triage Control";
    private const string DefaultProjectViewApplyIssueTitle = "IX Project View Apply Plan";
    private const string TriageControlIssueVariableName = "IX_TRIAGE_CONTROL_ISSUE";
    private const string ProjectViewApplyIssueVariableName = "IX_PROJECT_VIEW_APPLY_ISSUE";
    private static readonly SemaphoreSlim ProjectInitLock = new(1, 1);

    private static async Task<List<FilePlan>> PlanTriageBootstrapFilesAsync(
        GitHubApi github,
        SetupOptions options,
        string owner,
        string repo,
        string defaultBranch,
        string? gitHubToken) {
        var repoFullName = $"{owner}/{repo}";
        var existingConfig = await github.TryGetFileAsync(owner, repo, DefaultTriageProjectConfigPath, defaultBranch)
            .ConfigureAwait(false);

        string projectOwner;
        int projectNumber;
        string projectConfigContent;

        if (existingConfig is not null &&
            ProjectBootstrapRunner.TryReadProjectTargetFromConfigJson(
                existingConfig.Content,
                out projectOwner,
                out projectNumber,
                out _)) {
            projectConfigContent = existingConfig.Content;
        } else {
            if (string.IsNullOrWhiteSpace(gitHubToken)) {
                throw new InvalidOperationException(
                    "--triage-bootstrap requires a GitHub token. Re-run with --github-token or GH_TOKEN.");
            }

            projectConfigContent = await CreateTriageProjectConfigAsync(repoFullName, gitHubToken).ConfigureAwait(false);
            if (!ProjectBootstrapRunner.TryReadProjectTargetFromConfigJson(
                    projectConfigContent,
                    out projectOwner,
                    out projectNumber,
                    out var parseError)) {
                throw new InvalidOperationException(parseError);
            }
        }

        var workflowTemplate = ProjectBootstrapRunner.LoadWorkflowTemplate();
        var workflowContent = ProjectBootstrapRunner.RenderWorkflowTemplate(
            workflowTemplate,
            projectOwner,
            projectNumber,
            maxItems: 500);

        var visionTemplate = ProjectBootstrapRunner.LoadVisionTemplate();
        var visionContent = ProjectBootstrapRunner.RenderVisionTemplate(
            visionTemplate,
            repoFullName,
            projectOwner,
            projectNumber);

        var existingWorkflow = await github.TryGetFileAsync(owner, repo, DefaultTriageWorkflowPath, defaultBranch)
            .ConfigureAwait(false);
        var existingVision = await github.TryGetFileAsync(owner, repo, DefaultVisionPath, defaultBranch)
            .ConfigureAwait(false);

        var plans = new List<FilePlan> {
            PlanWrite(DefaultTriageProjectConfigPath, existingConfig?.Content, projectConfigContent, options.Force),
            PlanWrite(DefaultTriageWorkflowPath, existingWorkflow?.Content, workflowContent, options.Force),
            PlanWrite(DefaultVisionPath, existingVision?.Content, visionContent, options.Force)
        };

        if (!options.DryRun) {
            await EnsureControlIssueConfiguredAsync(
                github,
                owner,
                repo,
                repoFullName,
                projectOwner,
                projectNumber).ConfigureAwait(false);

            await EnsureProjectViewApplyIssueConfiguredAsync(
                github,
                owner,
                repo,
                repoFullName,
                projectOwner,
                projectNumber).ConfigureAwait(false);
        }

        return plans;
    }

    internal static bool ShouldProvisionTriageControlIssue(string? variableValue) {
        return !TryParsePositiveInt(variableValue, out _);
    }

    internal static bool ShouldProvisionProjectViewApplyIssue(string? issueVariableValue, int missingViews, bool directCreateSupported) {
        if (missingViews <= 0 || directCreateSupported) {
            return false;
        }

        return !TryParsePositiveInt(issueVariableValue, out _);
    }

    private static bool TryParsePositiveInt(string? value, out int number) {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }

    private static async Task EnsureControlIssueConfiguredAsync(
        GitHubApi github,
        string owner,
        string repo,
        string repoFullName,
        string projectOwner,
        int projectNumber) {
        string? existingControlIssue = null;
        try {
            existingControlIssue = await github.TryGetRepositoryVariableAsync(
                owner,
                repo,
                TriageControlIssueVariableName).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not read {TriageControlIssueVariableName}. " +
                $"Configure it manually if needed. ({ex.Message})");
            return;
        }

        if (!ShouldProvisionTriageControlIssue(existingControlIssue)) {
            return;
        }

        try {
            var body = ProjectBootstrapRunner.BuildControlIssueBody(repoFullName, projectOwner, projectNumber);
            var issueNumber = await github.CreateIssueAsync(owner, repo, DefaultControlIssueTitle, body).ConfigureAwait(false);
            await github.UpsertRepositoryVariableAsync(
                owner,
                repo,
                TriageControlIssueVariableName,
                issueNumber.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

            Console.WriteLine($"Configured {TriageControlIssueVariableName} to #{issueNumber}.");
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not auto-configure {TriageControlIssueVariableName}. " +
                $"Run `intelligencex todo project-bootstrap --repo {repoFullName} --create-control-issue` after setup. ({ex.Message})");
        }
    }

    private static async Task EnsureProjectViewApplyIssueConfiguredAsync(
        GitHubApi github,
        string owner,
        string repo,
        string repoFullName,
        string projectOwner,
        int projectNumber) {
        string? existingViewIssue = null;
        try {
            existingViewIssue = await github.TryGetRepositoryVariableAsync(
                owner,
                repo,
                ProjectViewApplyIssueVariableName).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not read {ProjectViewApplyIssueVariableName}. " +
                $"View-apply issue auto-provision may be skipped. ({ex.Message})");
            return;
        }

        if (TryParsePositiveInt(existingViewIssue, out _)) {
            return;
        }

        try {
            var client = new ProjectV2Client();
            var project = await client.TryGetProjectAsync(projectOwner, projectNumber).ConfigureAwait(false);
            if (project is null) {
                Console.Error.WriteLine(
                    $"Warning: triage bootstrap could not resolve project {projectOwner}#{projectNumber} for view apply planning.");
                return;
            }

            var views = await client.GetProjectViewsByNameAsync(projectOwner, projectNumber).ConfigureAwait(false);
            var directCreateSupported = await client.SupportsProjectViewCreationAsync().ConfigureAwait(false);
            var missingViews = ProjectViewCatalog.FindMissingDefaultViews(views).Count;
            if (!ShouldProvisionProjectViewApplyIssue(existingViewIssue, missingViews, directCreateSupported)) {
                return;
            }

            var markdown = ProjectViewApplyRunner.BuildApplyMarkdown(
                repoFullName,
                projectOwner,
                projectNumber,
                project.Url,
                views,
                directCreateSupported,
                DateTimeOffset.UtcNow);

            var issueNumber = await github.CreateIssueAsync(owner, repo, DefaultProjectViewApplyIssueTitle, markdown)
                .ConfigureAwait(false);
            await github.UpsertRepositoryVariableAsync(
                owner,
                repo,
                ProjectViewApplyIssueVariableName,
                issueNumber.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

            Console.WriteLine($"Configured {ProjectViewApplyIssueVariableName} to #{issueNumber}.");
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not auto-configure {ProjectViewApplyIssueVariableName}. " +
                $"Run `intelligencex todo project-view-apply --repo {repoFullName} --create-issue` after setup. ({ex.Message})");
        }
    }

    private static async Task<string> CreateTriageProjectConfigAsync(string repoFullName, string gitHubToken) {
        await ProjectInitLock.WaitAsync().ConfigureAwait(false);
        try {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "ix-setup-triage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var tempConfigPath = Path.Combine(tempDirectory, "ix-project-config.json");

            var previousGhToken = Environment.GetEnvironmentVariable("GH_TOKEN");
            var previousGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            try {
                Environment.SetEnvironmentVariable("GH_TOKEN", gitHubToken);
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", gitHubToken);

                var exitCode = await ProjectInitRunner.RunAsync(new[] {
                    "--repo", repoFullName,
                    "--title", "IX Triage Control",
                    "--out", tempConfigPath,
                    "--link-repo"
                }).ConfigureAwait(false);

                if (exitCode != 0) {
                    throw new InvalidOperationException(
                        "--triage-bootstrap could not initialize GitHub Project schema automatically. " +
                        "Ensure token scopes include `project` and `read:project`.");
                }

                if (!File.Exists(tempConfigPath)) {
                    throw new InvalidOperationException("Project config was not produced by project-init.");
                }

                return File.ReadAllText(tempConfigPath);
            } finally {
                Environment.SetEnvironmentVariable("GH_TOKEN", previousGhToken);
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHubToken);
                TryDeleteDirectory(tempDirectory);
            }
        } finally {
            ProjectInitLock.Release();
        }
    }
}
