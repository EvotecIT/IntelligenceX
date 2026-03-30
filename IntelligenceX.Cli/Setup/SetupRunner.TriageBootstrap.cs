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
    private const string BootstrapLinksCommentMarker = "<!-- intelligencex:triage-bootstrap-links -->";
    private static readonly SemaphoreSlim ProjectInitLock = new(1, 1);
    private readonly record struct AssistiveIssueState(int? IssueNumber);
    private readonly record struct ProjectViewApplyIssueState(int? IssueNumber, int MissingViews, bool DirectCreateSupported);
    private readonly record struct LabelProvisionState(int CreatedCount, int TotalCount, bool Failed);

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
            maxItems: 500,
            visionDriftThreshold: 0.70);

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
            var controlIssueState = await EnsureControlIssueConfiguredAsync(
                github,
                owner,
                repo,
                repoFullName,
                projectOwner,
                projectNumber).ConfigureAwait(false);

            var viewApplyIssueState = await EnsureProjectViewApplyIssueConfiguredAsync(
                github,
                owner,
                repo,
                repoFullName,
                projectOwner,
                projectNumber).ConfigureAwait(false);

            var labelProvisionState = await EnsureTriageLabelsConfiguredAsync(
                github,
                owner,
                repo,
                repoFullName,
                projectOwner,
                projectNumber).ConfigureAwait(false);

            if (controlIssueState.IssueNumber.HasValue) {
                await UpsertBootstrapLinksCommentAsync(
                    github,
                    owner,
                    repo,
                    repoFullName,
                    projectOwner,
                    projectNumber,
                    controlIssueState.IssueNumber.Value,
                    viewApplyIssueState.IssueNumber,
                    viewApplyIssueState.MissingViews,
                    viewApplyIssueState.DirectCreateSupported,
                    labelProvisionState).ConfigureAwait(false);
            }
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

    internal static string BuildTriageBootstrapLinksComment(
        string repoFullName,
        string projectOwner,
        int projectNumber,
        int controlIssueNumber,
        int? viewApplyIssueNumber,
        int missingViews,
        bool directCreateSupported,
        int labelsCreatedCount,
        int labelsTotalCount,
        bool labelsEnsureFailed) {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(BootstrapLinksCommentMarker);
        builder.AppendLine("## IX Triage Bootstrap Links");
        builder.AppendLine();
        builder.AppendLine($"- Project target: `{projectOwner}#{projectNumber}`");
        builder.AppendLine($"- Control issue: #{controlIssueNumber} ({BuildIssueUrl(repoFullName, controlIssueNumber)})");
        if (labelsEnsureFailed) {
            builder.AppendLine(
                $"- IX labels: ensure failed (run `intelligencex todo project-init --repo {repoFullName} --owner {projectOwner} --project {projectNumber.ToString(CultureInfo.InvariantCulture)} --ensure-labels --no-link-repo --no-ensure-default-views`).");
        } else if (labelsTotalCount > 0 && labelsCreatedCount > 0) {
            builder.AppendLine(
                $"- IX labels: ensured ({labelsTotalCount.ToString(CultureInfo.InvariantCulture)} tracked, {labelsCreatedCount.ToString(CultureInfo.InvariantCulture)} created this run).");
        } else if (labelsTotalCount > 0) {
            builder.AppendLine($"- IX labels: ensured (all {labelsTotalCount.ToString(CultureInfo.InvariantCulture)} already present).");
        } else {
            builder.AppendLine("- IX labels: not configured.");
        }
        if (viewApplyIssueNumber.HasValue) {
            builder.AppendLine(
                $"- Project view apply issue: #{viewApplyIssueNumber.Value} ({BuildIssueUrl(repoFullName, viewApplyIssueNumber.Value)})");
        } else if (directCreateSupported) {
            builder.AppendLine("- Project view apply issue: not required (GitHub API can create project views directly).");
        } else if (missingViews <= 0) {
            builder.AppendLine("- Project view apply issue: not required (default IX project views already present).");
        } else {
            builder.AppendLine(
                "- Project view apply issue: unavailable (auto-provision failed; run `intelligencex todo project-view-apply --create-issue`).");
        }
        builder.AppendLine();
        builder.AppendLine("### Maintainer Entry Point");
        builder.AppendLine();
        builder.AppendLine("1. Open the latest workflow summary in this control issue.");
        builder.AppendLine("2. Use the linked project-view issue (if present) to complete missing default project views.");
        builder.AppendLine("3. Triage PRs/issues in the GitHub Project using IX fields and labels.");
        return builder.ToString().TrimEnd();
    }

    private static bool TryParsePositiveInt(string? value, out int number) {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }

    private static string BuildIssueUrl(string repoFullName, int issueNumber) {
        return $"https://github.com/{repoFullName}/issues/{issueNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    private static async Task<AssistiveIssueState> EnsureControlIssueConfiguredAsync(
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
            return new AssistiveIssueState(null);
        }

        if (TryParsePositiveInt(existingControlIssue, out var existingIssueNumber)) {
            return new AssistiveIssueState(existingIssueNumber);
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
            return new AssistiveIssueState(issueNumber);
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not auto-configure {TriageControlIssueVariableName}. " +
                $"Run `intelligencex todo project-bootstrap --repo {repoFullName} --create-control-issue` after setup. ({ex.Message})");
            return new AssistiveIssueState(null);
        }
    }

    private static async Task<ProjectViewApplyIssueState> EnsureProjectViewApplyIssueConfiguredAsync(
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
            return new ProjectViewApplyIssueState(
                IssueNumber: null,
                MissingViews: 0,
                DirectCreateSupported: false);
        }

        if (TryParsePositiveInt(existingViewIssue, out var existingIssueNumber)) {
            return new ProjectViewApplyIssueState(
                IssueNumber: existingIssueNumber,
                MissingViews: 0,
                DirectCreateSupported: false);
        }

        try {
            var client = new ProjectV2Client();
            var project = await client.TryGetProjectAsync(projectOwner, projectNumber).ConfigureAwait(false);
            if (project is null) {
                Console.Error.WriteLine(
                    $"Warning: triage bootstrap could not resolve project {projectOwner}#{projectNumber} for view apply planning.");
                return new ProjectViewApplyIssueState(
                    IssueNumber: null,
                    MissingViews: 0,
                    DirectCreateSupported: false);
            }

            var views = await client.GetProjectViewsByNameAsync(projectOwner, projectNumber).ConfigureAwait(false);
            var directCreateSupported = await client.SupportsProjectViewCreationAsync().ConfigureAwait(false);
            var missingViews = ProjectViewCatalog.FindMissingDefaultViews(views).Count;
            if (!ShouldProvisionProjectViewApplyIssue(existingViewIssue, missingViews, directCreateSupported)) {
                return new ProjectViewApplyIssueState(
                    IssueNumber: null,
                    MissingViews: missingViews,
                    DirectCreateSupported: directCreateSupported);
            }

            var markdown = ProjectViewApplyRunner.BuildApplyMarkdown(
                repoFullName,
                projectOwner,
                projectNumber,
                project.Url,
                views,
                includePrWatchGovernanceViews: false,
                directCreateSupported: directCreateSupported,
                generatedAtUtc: DateTimeOffset.UtcNow);

            var issueNumber = await github.CreateIssueAsync(owner, repo, DefaultProjectViewApplyIssueTitle, markdown)
                .ConfigureAwait(false);
            await github.UpsertRepositoryVariableAsync(
                owner,
                repo,
                ProjectViewApplyIssueVariableName,
                issueNumber.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

            Console.WriteLine($"Configured {ProjectViewApplyIssueVariableName} to #{issueNumber}.");
            return new ProjectViewApplyIssueState(
                IssueNumber: issueNumber,
                MissingViews: missingViews,
                DirectCreateSupported: directCreateSupported);
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not auto-configure {ProjectViewApplyIssueVariableName}. " +
                $"Run `intelligencex todo project-view-apply --repo {repoFullName} --create-issue` after setup. ({ex.Message})");
            return new ProjectViewApplyIssueState(
                IssueNumber: null,
                MissingViews: 0,
                DirectCreateSupported: false);
        }
    }

    private static async Task<LabelProvisionState> EnsureTriageLabelsConfiguredAsync(
        GitHubApi github,
        string owner,
        string repo,
        string repoFullName,
        string projectOwner,
        int projectNumber) {
        try {
            var result = await github.EnsureRepositoryLabelsAsync(owner, repo, ProjectLabelCatalog.DefaultLabels)
                .ConfigureAwait(false);
            if (result.CreatedCount > 0) {
                Console.WriteLine(
                    $"Ensured IX labels ({result.TotalCount.ToString(CultureInfo.InvariantCulture)} tracked, " +
                    $"{result.CreatedCount.ToString(CultureInfo.InvariantCulture)} created).");
            } else {
                Console.WriteLine(
                    $"Ensured IX labels ({result.TotalCount.ToString(CultureInfo.InvariantCulture)} tracked, all already present).");
            }

            return new LabelProvisionState(
                CreatedCount: result.CreatedCount,
                TotalCount: result.TotalCount,
                Failed: false);
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not ensure IX labels. Run `intelligencex todo project-init --repo {repoFullName} --owner {projectOwner} --project {projectNumber.ToString(CultureInfo.InvariantCulture)} --ensure-labels --no-link-repo --no-ensure-default-views` after setup. ({ex.Message})");
            return new LabelProvisionState(
                CreatedCount: 0,
                TotalCount: ProjectLabelCatalog.DefaultLabels.Count,
                Failed: true);
        }
    }

    private static async Task UpsertBootstrapLinksCommentAsync(
        GitHubApi github,
        string owner,
        string repo,
        string repoFullName,
        string projectOwner,
        int projectNumber,
        int controlIssueNumber,
        int? viewApplyIssueNumber,
        int missingViews,
        bool directCreateSupported,
        LabelProvisionState labelProvisionState) {
        try {
            var comment = BuildTriageBootstrapLinksComment(
                repoFullName,
                projectOwner,
                projectNumber,
                controlIssueNumber,
                viewApplyIssueNumber,
                missingViews,
                directCreateSupported,
                labelProvisionState.CreatedCount,
                labelProvisionState.TotalCount,
                labelProvisionState.Failed);
            await github.UpsertIssueCommentWithMarkerAsync(
                owner,
                repo,
                controlIssueNumber,
                BootstrapLinksCommentMarker,
                comment).ConfigureAwait(false);
            Console.WriteLine($"Updated bootstrap links comment on issue #{controlIssueNumber}.");
        } catch (Exception ex) {
            Console.Error.WriteLine(
                $"Warning: triage bootstrap could not upsert links comment on issue #{controlIssueNumber}. ({ex.Message})");
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
