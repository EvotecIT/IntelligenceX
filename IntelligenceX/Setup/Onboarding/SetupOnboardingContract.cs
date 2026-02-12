using System;
using System.Collections.Generic;

namespace IntelligenceX.Setup.Onboarding;

/// <summary>
/// Canonical operation ids used by onboarding contracts.
/// </summary>
public static class SetupOnboardingOperationIds {
    /// <summary>
    /// Setup operation id.
    /// </summary>
    public const string Setup = "setup";

    /// <summary>
    /// Update secret operation id.
    /// </summary>
    public const string UpdateSecret = "update-secret";

    /// <summary>
    /// Cleanup operation id.
    /// </summary>
    public const string Cleanup = "cleanup";
}

/// <summary>
/// Immutable onboarding path contract.
/// </summary>
public sealed class SetupOnboardingPathContract {
    /// <summary>
    /// Initializes a new path contract.
    /// </summary>
    public SetupOnboardingPathContract(
        string id,
        string displayName,
        string description,
        string operation,
        bool requiresGitHubAuth,
        bool requiresRepoSelection,
        bool requiresAiAuth,
        IReadOnlyList<string> flow) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        RequiresGitHubAuth = requiresGitHubAuth;
        RequiresRepoSelection = requiresRepoSelection;
        RequiresAiAuth = requiresAiAuth;
        Flow = flow ?? throw new ArgumentNullException(nameof(flow));
    }

    /// <summary>
    /// Stable path id (for example, <c>new-setup</c>).
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable path label.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Path description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Default operation id.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Whether GitHub auth is required.
    /// </summary>
    public bool RequiresGitHubAuth { get; }

    /// <summary>
    /// Whether repository selection is required.
    /// </summary>
    public bool RequiresRepoSelection { get; }

    /// <summary>
    /// Whether AI auth is required.
    /// </summary>
    public bool RequiresAiAuth { get; }

    /// <summary>
    /// Ordered flow steps for this path.
    /// </summary>
    public IReadOnlyList<string> Flow { get; }
}

/// <summary>
/// Canonical command templates for onboarding flows.
/// </summary>
public sealed class SetupOnboardingCommandTemplates {
    /// <summary>
    /// Initializes command templates.
    /// </summary>
    public SetupOnboardingCommandTemplates(
        string autoDetect,
        string newSetupDryRun,
        string newSetupApply,
        string refreshAuthDryRun,
        string refreshAuthApply,
        string cleanupDryRun,
        string cleanupApply,
        string maintenanceWizard) {
        AutoDetect = autoDetect ?? throw new ArgumentNullException(nameof(autoDetect));
        NewSetupDryRun = newSetupDryRun ?? throw new ArgumentNullException(nameof(newSetupDryRun));
        NewSetupApply = newSetupApply ?? throw new ArgumentNullException(nameof(newSetupApply));
        RefreshAuthDryRun = refreshAuthDryRun ?? throw new ArgumentNullException(nameof(refreshAuthDryRun));
        RefreshAuthApply = refreshAuthApply ?? throw new ArgumentNullException(nameof(refreshAuthApply));
        CleanupDryRun = cleanupDryRun ?? throw new ArgumentNullException(nameof(cleanupDryRun));
        CleanupApply = cleanupApply ?? throw new ArgumentNullException(nameof(cleanupApply));
        MaintenanceWizard = maintenanceWizard ?? throw new ArgumentNullException(nameof(maintenanceWizard));
    }

    /// <summary>
    /// Auto-detect command.
    /// </summary>
    public string AutoDetect { get; }

    /// <summary>
    /// New setup dry-run command.
    /// </summary>
    public string NewSetupDryRun { get; }

    /// <summary>
    /// New setup apply command.
    /// </summary>
    public string NewSetupApply { get; }

    /// <summary>
    /// Refresh auth dry-run command.
    /// </summary>
    public string RefreshAuthDryRun { get; }

    /// <summary>
    /// Refresh auth apply command.
    /// </summary>
    public string RefreshAuthApply { get; }

    /// <summary>
    /// Cleanup dry-run command.
    /// </summary>
    public string CleanupDryRun { get; }

    /// <summary>
    /// Cleanup apply command.
    /// </summary>
    public string CleanupApply { get; }

    /// <summary>
    /// Maintenance wizard command.
    /// </summary>
    public string MaintenanceWizard { get; }
}

/// <summary>
/// Canonical onboarding contract consumed by CLI, Web, and Bot surfaces.
/// </summary>
public static class SetupOnboardingContract {
    /// <summary>
    /// New setup path id.
    /// </summary>
    public const string NewSetupPathId = "new-setup";

    /// <summary>
    /// Refresh auth path id.
    /// </summary>
    public const string RefreshAuthPathId = "refresh-auth";

    /// <summary>
    /// Cleanup path id.
    /// </summary>
    public const string CleanupPathId = "cleanup";

    /// <summary>
    /// Maintenance path id.
    /// </summary>
    public const string MaintenancePathId = "maintenance";

    private static readonly IReadOnlyList<SetupOnboardingPathContract> PathsWithMaintenance = new[] {
        new SetupOnboardingPathContract(
            id: NewSetupPathId,
            displayName: "New Setup",
            description: "Configure workflow and reviewer config for first-time onboarding.",
            operation: SetupOnboardingOperationIds.Setup,
            requiresGitHubAuth: true,
            requiresRepoSelection: true,
            requiresAiAuth: true,
            flow: new[] {
                "Authenticate with GitHub",
                "Select repositories",
                "Configure workflow and reviewer profile",
                "Authenticate with AI provider",
                "Plan, apply, verify"
            }),
        new SetupOnboardingPathContract(
            id: RefreshAuthPathId,
            displayName: "Fix Expired Auth",
            description: "Refresh OpenAI/ChatGPT auth and update INTELLIGENCEX_AUTH_B64 secret.",
            operation: SetupOnboardingOperationIds.UpdateSecret,
            requiresGitHubAuth: true,
            requiresRepoSelection: true,
            requiresAiAuth: true,
            flow: new[] {
                "Authenticate with GitHub",
                "Select repositories",
                "Refresh AI auth bundle",
                "Apply update-secret",
                "Verify secret presence"
            }),
        new SetupOnboardingPathContract(
            id: CleanupPathId,
            displayName: "Cleanup",
            description: "Remove workflow/config and optionally remove secrets from repositories.",
            operation: SetupOnboardingOperationIds.Cleanup,
            requiresGitHubAuth: true,
            requiresRepoSelection: true,
            requiresAiAuth: false,
            flow: new[] {
                "Authenticate with GitHub",
                "Select repositories",
                "Choose cleanup options",
                "Plan, apply cleanup",
                "Verify removal"
            }),
        new SetupOnboardingPathContract(
            id: MaintenancePathId,
            displayName: "Maintenance",
            description: "Run preflight checks, inspect existing setup, then choose setup/update-secret/cleanup.",
            operation: SetupOnboardingOperationIds.Setup,
            requiresGitHubAuth: true,
            requiresRepoSelection: true,
            requiresAiAuth: false,
            flow: new[] {
                "Run auto-detect preflight",
                "Inspect current workflow/config status",
                "Select operation based on findings",
                "Plan, apply, verify"
            })
    };

    private static readonly IReadOnlyList<SetupOnboardingPathContract> PathsWithoutMaintenance = new[] {
        PathsWithMaintenance[0],
        PathsWithMaintenance[1],
        PathsWithMaintenance[2]
    };

    private static readonly SetupOnboardingCommandTemplates CommandTemplates = new(
        autoDetect: "intelligencex setup autodetect --json",
        newSetupDryRun: "intelligencex setup --repo owner/name --with-config --dry-run",
        newSetupApply: "intelligencex setup --repo owner/name --with-config",
        refreshAuthDryRun: "intelligencex setup --repo owner/name --update-secret --auth-b64 <base64> --dry-run",
        refreshAuthApply: "intelligencex setup --repo owner/name --update-secret --auth-b64 <base64>",
        cleanupDryRun: "intelligencex setup --repo owner/name --cleanup --dry-run",
        cleanupApply: "intelligencex setup --repo owner/name --cleanup",
        maintenanceWizard: "intelligencex setup web");

    /// <summary>
    /// Returns onboarding paths.
    /// </summary>
    public static IReadOnlyList<SetupOnboardingPathContract> GetPaths(bool includeMaintenancePath = true) {
        return includeMaintenancePath ? PathsWithMaintenance : PathsWithoutMaintenance;
    }

    /// <summary>
    /// Returns a path by id, or the default path when not found.
    /// </summary>
    public static SetupOnboardingPathContract GetPathOrDefault(string? id, bool includeMaintenancePath = true) {
        var paths = GetPaths(includeMaintenancePath);
        if (!string.IsNullOrWhiteSpace(id)) {
            foreach (var path in paths) {
                if (string.Equals(path.Id, id, StringComparison.OrdinalIgnoreCase)) {
                    return path;
                }
            }
        }

        return paths[0];
    }

    /// <summary>
    /// Returns canonical path id for the given operation id.
    /// </summary>
    public static string PathIdFromOperation(string? operationId) {
        if (string.Equals(operationId, SetupOnboardingOperationIds.UpdateSecret, StringComparison.OrdinalIgnoreCase)) {
            return RefreshAuthPathId;
        }
        if (string.Equals(operationId, SetupOnboardingOperationIds.Cleanup, StringComparison.OrdinalIgnoreCase)) {
            return CleanupPathId;
        }
        return NewSetupPathId;
    }

    /// <summary>
    /// Returns canonical command templates.
    /// </summary>
    public static SetupOnboardingCommandTemplates GetCommandTemplates() {
        return CommandTemplates;
    }
}
