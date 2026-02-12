using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>
    /// Semantic onboarding contract version.
    /// </summary>
    public const string ContractVersion = "2026-02-12.3";

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

    private static readonly string FingerprintWithMaintenance = ComputeFingerprint(includeMaintenancePath: true);
    private static readonly string FingerprintWithoutMaintenance = ComputeFingerprint(includeMaintenancePath: false);

    /// <summary>
    /// Returns onboarding paths.
    /// </summary>
    public static IReadOnlyList<SetupOnboardingPathContract> GetPaths(bool includeMaintenancePath = true) {
        return ClonePaths(includeMaintenancePath ? PathsWithMaintenance : PathsWithoutMaintenance);
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
        return new SetupOnboardingCommandTemplates(
            autoDetect: CommandTemplates.AutoDetect,
            newSetupDryRun: CommandTemplates.NewSetupDryRun,
            newSetupApply: CommandTemplates.NewSetupApply,
            refreshAuthDryRun: CommandTemplates.RefreshAuthDryRun,
            refreshAuthApply: CommandTemplates.RefreshAuthApply,
            cleanupDryRun: CommandTemplates.CleanupDryRun,
            cleanupApply: CommandTemplates.CleanupApply,
            maintenanceWizard: CommandTemplates.MaintenanceWizard);
    }

    /// <summary>
    /// Returns deterministic contract fingerprint for drift checks.
    /// </summary>
    public static string GetContractFingerprint(bool includeMaintenancePath = true) {
        return includeMaintenancePath ? FingerprintWithMaintenance : FingerprintWithoutMaintenance;
    }

    private static IReadOnlyList<SetupOnboardingPathContract> ClonePaths(IReadOnlyList<SetupOnboardingPathContract> source) {
        var copy = new SetupOnboardingPathContract[source.Count];
        for (var i = 0; i < source.Count; i++) {
            var path = source[i];
            var flowCopy = new string[path.Flow.Count];
            for (var j = 0; j < path.Flow.Count; j++) {
                flowCopy[j] = path.Flow[j];
            }

            copy[i] = new SetupOnboardingPathContract(
                id: path.Id,
                displayName: path.DisplayName,
                description: path.Description,
                operation: path.Operation,
                requiresGitHubAuth: path.RequiresGitHubAuth,
                requiresRepoSelection: path.RequiresRepoSelection,
                requiresAiAuth: path.RequiresAiAuth,
                flow: flowCopy);
        }

        return copy;
    }

    private static string ComputeFingerprint(bool includeMaintenancePath) {
        var data = includeMaintenancePath ? PathsWithMaintenance : PathsWithoutMaintenance;
        var builder = new StringBuilder();
        builder.Append("version=").Append(ContractVersion).Append('\n');
        for (var i = 0; i < data.Count; i++) {
            var path = data[i];
            builder.Append(path.Id).Append('|')
                .Append(path.DisplayName).Append('|')
                .Append(path.Description).Append('|')
                .Append(path.Operation).Append('|')
                .Append(path.RequiresGitHubAuth ? '1' : '0').Append('|')
                .Append(path.RequiresRepoSelection ? '1' : '0').Append('|')
                .Append(path.RequiresAiAuth ? '1' : '0').Append('\n');
            for (var j = 0; j < path.Flow.Count; j++) {
                builder.Append('>').Append(path.Flow[j]).Append('\n');
            }
        }

        builder.Append("autoDetect=").Append(CommandTemplates.AutoDetect).Append('\n');
        builder.Append("newSetupDryRun=").Append(CommandTemplates.NewSetupDryRun).Append('\n');
        builder.Append("newSetupApply=").Append(CommandTemplates.NewSetupApply).Append('\n');
        builder.Append("refreshAuthDryRun=").Append(CommandTemplates.RefreshAuthDryRun).Append('\n');
        builder.Append("refreshAuthApply=").Append(CommandTemplates.RefreshAuthApply).Append('\n');
        builder.Append("cleanupDryRun=").Append(CommandTemplates.CleanupDryRun).Append('\n');
        builder.Append("cleanupApply=").Append(CommandTemplates.CleanupApply).Append('\n');
        builder.Append("maintenanceWizard=").Append(CommandTemplates.MaintenanceWizard).Append('\n');

        using var hash = SHA256.Create();
        var digest = hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return ToHexLower(digest);
    }

    private static string ToHexLower(byte[] bytes) {
        var chars = new char[bytes.Length * 2];
        var index = 0;
        for (var i = 0; i < bytes.Length; i++) {
            var b = bytes[i];
            chars[index++] = ToHexNibbleLower((b >> 4) & 0xF);
            chars[index++] = ToHexNibbleLower(b & 0xF);
        }
        return new string(chars);
    }

    private static char ToHexNibbleLower(int value) {
        return value < 10
            ? (char)('0' + value)
            : (char)('a' + (value - 10));
    }
}
