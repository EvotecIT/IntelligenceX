using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Onboarding;
using IntelligenceX.Setup.Onboarding;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task HandleSetupAutodetectAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }

        SetupAutodetectRequest request;
        try {
            request = JsonSerializer.Deserialize<SetupAutodetectRequest>(body, _jsonOptions) ?? new SetupAutodetectRequest();
        } catch (JsonException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
            return;
        }

        var workspace = string.IsNullOrWhiteSpace(request.Workspace)
            ? Environment.CurrentDirectory
            : request.Workspace!;

        if (!await ConsoleLock.WaitAsync(ConsoleLockTimeout).ConfigureAwait(false)) {
            context.Response.StatusCode = 429;
            await WriteJsonAsync(context, new { error = "Setup is busy. Please retry in a moment." }).ConfigureAwait(false);
            return;
        }

        SetupOnboardingAutoDetectResult result;
        try {
            result = await SetupOnboardingAutoDetectRunner.RunAsync(workspace, request.RepoHint).ConfigureAwait(false);
        } finally {
            ConsoleLock.Release();
        }

        var paths = SetupOnboardingContract.GetPaths(includeMaintenancePath: true).Select(path => new {
            id = path.Id,
            displayName = path.DisplayName,
            description = path.Description,
            defaultOperation = path.Operation,
            requiresGitHubAuth = path.RequiresGitHubAuth,
            requiresRepoSelection = path.RequiresRepoSelection,
            requiresAiAuth = path.RequiresAiAuth,
            flow = path.Flow
        }).ToArray();
        var commands = SetupOnboardingContract.GetCommandTemplates();

        await WriteJsonOkAsync(context, new {
            contractVersion = result.ContractVersion,
            contractFingerprint = result.ContractFingerprint,
            commandTemplates = new {
                autoDetect = commands.AutoDetect,
                newSetupDryRun = commands.NewSetupDryRun,
                newSetupApply = commands.NewSetupApply,
                refreshAuthDryRun = commands.RefreshAuthDryRun,
                refreshAuthApply = commands.RefreshAuthApply,
                cleanupDryRun = commands.CleanupDryRun,
                cleanupApply = commands.CleanupApply,
                maintenanceWizard = commands.MaintenanceWizard
            },
            status = result.Status,
            workspace = result.Workspace,
            repo = result.Repo,
            localWorkflowExists = result.LocalWorkflowExists,
            localConfigExists = result.LocalConfigExists,
            recommendedPath = result.RecommendedPath,
            recommendedReason = result.RecommendedReason,
            checks = result.Checks.Select(check => new {
                name = check.Name,
                status = check.Status.ToString().ToLowerInvariant(),
                message = check.Message
            }).ToArray(),
            paths
        }).ConfigureAwait(false);
    }
}
