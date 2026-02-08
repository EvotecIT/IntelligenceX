using System;

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
        Console.WriteLine("  --upgrade");
        Console.WriteLine("  --force");
        Console.WriteLine("  --dry-run");
        Console.WriteLine("  --branch <name>");
        Console.WriteLine("  --plain (disable wizard UI)");
        Console.WriteLine("  --help");
    }

    private sealed class WizardOptions {
        public string? RepoFullName { get; set; }
        public bool WithConfig { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool ExplicitSecrets { get; set; }
        public WizardOperation Operation { get; set; }
        public bool DryRun { get; set; }
        public string? BranchName { get; set; }
        public bool ForcePlain { get; set; }
        public bool ShowHelp { get; set; }

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
                    case "help":
                        options.ShowHelp = true;
                        break;
                }
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
    }
}

