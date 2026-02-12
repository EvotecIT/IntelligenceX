using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Setup.Onboarding;

namespace IntelligenceX.Cli.Setup.Onboarding;

internal sealed class SetupOnboardingPathDefinition {
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required SetupApplyOperation DefaultOperation { get; init; }
    public required bool RequiresGitHubAuth { get; init; }
    public required bool RequiresRepoSelection { get; init; }
    public required bool RequiresAiAuth { get; init; }
    public required string[] Flow { get; init; }
}

internal static class SetupOnboardingPaths {
    public const string NewSetup = SetupOnboardingContract.NewSetupPathId;
    public const string RefreshAuth = SetupOnboardingContract.RefreshAuthPathId;
    public const string Cleanup = SetupOnboardingContract.CleanupPathId;
    public const string Maintenance = SetupOnboardingContract.MaintenancePathId;

    private static readonly IReadOnlyList<SetupOnboardingPathDefinition> Paths = SetupOnboardingContract
        .GetPaths(includeMaintenancePath: true)
        .Select(static path => new SetupOnboardingPathDefinition {
            Id = path.Id,
            DisplayName = path.DisplayName,
            Description = path.Description,
            DefaultOperation = MapOperation(path.Operation),
            RequiresGitHubAuth = path.RequiresGitHubAuth,
            RequiresRepoSelection = path.RequiresRepoSelection,
            RequiresAiAuth = path.RequiresAiAuth,
            Flow = path.Flow.ToArray()
        })
        .ToArray();

    public static IReadOnlyList<SetupOnboardingPathDefinition> GetAll() {
        return Paths;
    }

    public static SetupOnboardingPathDefinition GetOrDefault(string? id) {
        if (!string.IsNullOrWhiteSpace(id)) {
            foreach (var path in Paths) {
                if (string.Equals(path.Id, id, StringComparison.OrdinalIgnoreCase)) {
                    return path;
                }
            }
        }

        return Paths[0];
    }

    public static string FromOperation(SetupApplyOperation operation) {
        return operation switch {
            SetupApplyOperation.UpdateSecret => SetupOnboardingContract.PathIdFromOperation(SetupOnboardingOperationIds.UpdateSecret),
            SetupApplyOperation.Cleanup => SetupOnboardingContract.PathIdFromOperation(SetupOnboardingOperationIds.Cleanup),
            _ => SetupOnboardingContract.PathIdFromOperation(SetupOnboardingOperationIds.Setup)
        };
    }

    private static SetupApplyOperation MapOperation(string operationId) {
        if (string.Equals(operationId, SetupOnboardingOperationIds.UpdateSecret, StringComparison.OrdinalIgnoreCase)) {
            return SetupApplyOperation.UpdateSecret;
        }
        if (string.Equals(operationId, SetupOnboardingOperationIds.Cleanup, StringComparison.OrdinalIgnoreCase)) {
            return SetupApplyOperation.Cleanup;
        }
        return SetupApplyOperation.Setup;
    }
}
