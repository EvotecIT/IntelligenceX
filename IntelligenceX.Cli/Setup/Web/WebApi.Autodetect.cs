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

        await WriteJsonOkAsync(context, BuildSetupAutodetectResponsePayload(result)).ConfigureAwait(false);
    }

    internal static string BuildSetupAutodetectResponseJsonForTests(SetupOnboardingAutoDetectResult result) {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Serialize(BuildSetupAutodetectResponsePayload(result), options);
    }

    private static object BuildSetupAutodetectResponsePayload(SetupOnboardingAutoDetectResult result) {
        ArgumentNullException.ThrowIfNull(result);

        var pathContracts = result.Paths ?? SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        var includeMaintenancePath = pathContracts.Any(path =>
            string.Equals(path.Id, SetupOnboardingContract.MaintenancePathId, StringComparison.Ordinal));
        var commands = result.CommandTemplates ?? SetupOnboardingContract.GetCommandTemplates();
        var checks = result.Checks ?? Array.Empty<SetupOnboardingCheck>();
        var contractVersion = string.IsNullOrWhiteSpace(result.ContractVersion)
            ? SetupOnboardingContract.ContractVersion
            : result.ContractVersion;
        var contractFingerprint = string.IsNullOrWhiteSpace(result.ContractFingerprint)
            ? SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath)
            : result.ContractFingerprint;

        var paths = pathContracts.Select(path => new {
            id = path.Id,
            displayName = path.DisplayName,
            description = path.Description,
            defaultOperation = path.Operation,
            requiresGitHubAuth = path.RequiresGitHubAuth,
            requiresRepoSelection = path.RequiresRepoSelection,
            requiresAiAuth = path.RequiresAiAuth,
            flow = path.Flow
        }).ToArray();
        return new {
            contractVersion,
            contractFingerprint,
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
            checks = checks.Select(check => new {
                name = check.Name,
                status = ToWireCheckStatus(check.Status),
                message = check.Message
            }).ToArray(),
            paths
        };
    }

    private static string ToWireCheckStatus(SetupOnboardingCheckStatus status) {
        return status switch {
            SetupOnboardingCheckStatus.Ok => "ok",
            SetupOnboardingCheckStatus.Warn => "warn",
            SetupOnboardingCheckStatus.Fail => "fail",
            _ => status.ToString().ToLowerInvariant()
        };
    }
}
