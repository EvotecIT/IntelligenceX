using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Todo;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private const string DefaultTriageProjectConfigPath = "artifacts/triage/ix-project-config.json";
    private const string DefaultTriageWorkflowPath = ".github/workflows/ix-triage-project-sync.yml";
    private const string DefaultVisionPath = "VISION.md";
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

        return plans;
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
