using System;
using System.Linq;
using IntelligenceX.Cli.Setup;
using IntelligenceX.Cli.Setup.Onboarding;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static partial class WizardRunner {
    private static void WriteHelp() {
        Console.WriteLine("IntelligenceX setup wizard");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex setup wizard [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --with-config");
        Console.WriteLine("  --skip-secret");
        Console.WriteLine("  --manual-secret");
        Console.WriteLine("  --explicit-secrets");
        Console.WriteLine("  --operation <setup|update-secret|cleanup>");
        Console.WriteLine("  --path <new-setup|refresh-auth|cleanup|maintenance>");
        Console.WriteLine("  --upgrade");
        Console.WriteLine("  --force");
        Console.WriteLine("  --dry-run");
        Console.WriteLine("  --branch <name>");
        Console.WriteLine("  --verbose");
        Console.WriteLine("  --plain (disable wizard UI)");
        Console.WriteLine("  --help");
        Console.WriteLine();
        Console.WriteLine("Quick examples:");
        Console.WriteLine("  intelligencex setup wizard --operation setup --repo owner/name");
        Console.WriteLine("  intelligencex setup wizard --path refresh-auth --repo owner/name");
        Console.WriteLine("  intelligencex setup wizard --operation update-secret --repo owner/name");
        Console.WriteLine("  intelligencex setup wizard --operation cleanup --repo owner/name --dry-run");
    }

    private static WizardOperation ResolveOperationFromPathId(string? pathId) {
        var path = SetupOnboardingPaths.GetOrDefault(pathId);
        return path.DefaultOperation switch {
            SetupApplyOperation.UpdateSecret => WizardOperation.UpdateSecret,
            SetupApplyOperation.Cleanup => WizardOperation.Cleanup,
            _ => WizardOperation.Setup
        };
    }

    private static string ResolvePathIdFromOperation(WizardOperation operation) {
        var setupOperation = operation switch {
            WizardOperation.UpdateSecret => SetupApplyOperation.UpdateSecret,
            WizardOperation.Cleanup => SetupApplyOperation.Cleanup,
            _ => SetupApplyOperation.Setup
        };

        return SetupOnboardingPaths.FromOperation(setupOperation);
    }

    internal static WizardOperation ResolveOperationFromPathIdForTests(string? pathId) {
        return ResolveOperationFromPathId(pathId);
    }

    internal static string ResolvePathIdFromOperationForTests(WizardOperation operation) {
        return ResolvePathIdFromOperation(operation);
    }

    private sealed class WizardOptions {
        public string? RepoFullName { get; set; }
        public bool WithConfig { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool ExplicitSecrets { get; set; }
        public string? PathId { get; set; }
        public bool PathSpecified { get; set; }
        public WizardOperation Operation { get; set; }
        public bool OperationSpecified { get; set; }
        public bool DryRun { get; set; }
        public string? BranchName { get; set; }
        public bool Verbose { get; set; }
        public bool ForcePlain { get; set; }
        public bool ShowHelp { get; set; }
        public string? ParseError { get; set; }

        public static WizardOptions Parse(string[] args) {
            var options = new WizardOptions();
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                    continue;
                }
                var key = arg.Substring(2);
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";

                switch (key) {
                    case "repo":
                        options.RepoFullName = value;
                        break;
                    case "with-config":
                        options.WithConfig = ParseBool(value, true);
                        break;
                    case "skip-secret":
                        options.SkipSecret = ParseBool(value, true);
                        break;
                    case "manual-secret":
                        options.ManualSecret = ParseBool(value, true);
                        break;
                    case "explicit-secrets":
                        options.ExplicitSecrets = ParseBool(value, true);
                        break;
                    case "upgrade":
                        options.Upgrade = ParseBool(value, true);
                        break;
                    case "force":
                        options.Force = ParseBool(value, true);
                        break;
                    case "operation":
                        options.Operation = ParseOperation(value);
                        options.OperationSpecified = true;
                        break;
                    case "path":
                        if (!TryNormalizePathId(value, out var pathId)) {
                            options.ParseError =
                                $"Unknown path '{value}'. Expected one of: new-setup, refresh-auth, cleanup, maintenance.";
                            return options;
                        }
                        options.PathId = pathId;
                        options.PathSpecified = true;
                        break;
                    case "dry-run":
                        options.DryRun = ParseBool(value, true);
                        break;
                    case "branch":
                        options.BranchName = value;
                        break;
                    case "plain":
                        options.ForcePlain = ParseBool(value, true);
                        break;
                    case "verbose":
                        options.Verbose = ParseBool(value, true);
                        break;
                    case "help":
                        options.ShowHelp = true;
                        break;
                }
            }
            if (options.PathSpecified && options.OperationSpecified) {
                options.ParseError = "Choose only one of --path or --operation.";
                return options;
            }
            return options;
        }

        public bool Upgrade { get; set; }
        public bool Force { get; set; }

        private static bool ParseBool(string value, bool fallback) {
            if (bool.TryParse(value, out var parsed)) {
                return parsed;
            }
            return fallback;
        }

        private static WizardOperation ParseOperation(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return WizardOperation.Setup;
            }
            return value.Trim().ToLowerInvariant() switch {
                "cleanup" => WizardOperation.Cleanup,
                "update-secret" => WizardOperation.UpdateSecret,
                "update" => WizardOperation.UpdateSecret,
                _ => WizardOperation.Setup
            };
        }

        private static bool TryNormalizePathId(string value, out string pathId) {
            pathId = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (string.Equals(normalized, "setup", StringComparison.Ordinal)) {
                pathId = SetupOnboardingPaths.NewSetup;
                return true;
            }
            if (string.Equals(normalized, "update-secret", StringComparison.Ordinal) ||
                string.Equals(normalized, "update", StringComparison.Ordinal) ||
                string.Equals(normalized, "fix-auth", StringComparison.Ordinal)) {
                pathId = SetupOnboardingPaths.RefreshAuth;
                return true;
            }

            var path = SetupOnboardingPaths.GetAll()
                .FirstOrDefault(candidate => string.Equals(candidate.Id, normalized, StringComparison.OrdinalIgnoreCase));
            if (path is null) {
                return false;
            }

            pathId = path.Id;
            return true;
        }
    }
}
