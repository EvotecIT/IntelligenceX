using System;
using System.Collections.Generic;

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
    public const string NewSetup = "new-setup";
    public const string RefreshAuth = "refresh-auth";
    public const string Cleanup = "cleanup";
    public const string Maintenance = "maintenance";

    private static readonly IReadOnlyList<SetupOnboardingPathDefinition> Paths = new[] {
        new SetupOnboardingPathDefinition {
            Id = NewSetup,
            DisplayName = "New Setup",
            Description = "Configure workflow and reviewer config for first-time onboarding.",
            DefaultOperation = SetupApplyOperation.Setup,
            RequiresGitHubAuth = true,
            RequiresRepoSelection = true,
            RequiresAiAuth = true,
            Flow = new[] {
                "Authenticate with GitHub",
                "Select repositories",
                "Configure workflow and reviewer profile",
                "Authenticate with AI provider",
                "Plan, apply, verify"
            }
        },
        new SetupOnboardingPathDefinition {
            Id = RefreshAuth,
            DisplayName = "Fix Expired Auth",
            Description = "Refresh OpenAI/ChatGPT auth and update INTELLIGENCEX_AUTH_B64 secret.",
            DefaultOperation = SetupApplyOperation.UpdateSecret,
            RequiresGitHubAuth = true,
            RequiresRepoSelection = true,
            RequiresAiAuth = true,
            Flow = new[] {
                "Authenticate with GitHub",
                "Select repositories",
                "Refresh AI auth bundle",
                "Apply update-secret",
                "Verify secret presence"
            }
        },
        new SetupOnboardingPathDefinition {
            Id = Cleanup,
            DisplayName = "Cleanup",
            Description = "Remove workflow/config and optionally remove secrets from repositories.",
            DefaultOperation = SetupApplyOperation.Cleanup,
            RequiresGitHubAuth = true,
            RequiresRepoSelection = true,
            RequiresAiAuth = false,
            Flow = new[] {
                "Authenticate with GitHub",
                "Select repositories",
                "Choose cleanup options",
                "Plan, apply cleanup",
                "Verify removal"
            }
        },
        new SetupOnboardingPathDefinition {
            Id = Maintenance,
            DisplayName = "Maintenance",
            Description = "Run preflight checks, inspect existing setup, then choose setup/update-secret/cleanup.",
            DefaultOperation = SetupApplyOperation.Setup,
            RequiresGitHubAuth = true,
            RequiresRepoSelection = true,
            RequiresAiAuth = false,
            Flow = new[] {
                "Run auto-detect preflight",
                "Inspect current workflow/config status",
                "Select operation based on findings",
                "Plan, apply, verify"
            }
        }
    };

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
            SetupApplyOperation.UpdateSecret => RefreshAuth,
            SetupApplyOperation.Cleanup => Cleanup,
            _ => NewSetup
        };
    }
}
