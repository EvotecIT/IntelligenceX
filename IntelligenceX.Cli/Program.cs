using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using IntelligenceX.Cli.Models;
using IntelligenceX.Cli.Auth;
using IntelligenceX.Cli.Heatmap;
using IntelligenceX.Cli.Usage;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli;

internal static partial class Program {
    private const int ManageLaunchFailureExitCode = 2;

    private static async Task<int> Main(string[] args) {
        return await DispatchAsync(args).ConfigureAwait(false);
    }

    internal static async Task<int> DispatchAsync(
        string[] args,
        Func<bool>? canLaunchManageHub = null,
        Func<string[], Task<int>>? runManageAsync = null) {
        var canLaunch = canLaunchManageHub ?? CanLaunchManageHub;
        var runManage = runManageAsync ?? RunManageAsync;

        if (args.Length == 0) {
            if (canLaunch()) {
                return await RunManageWithFallbackAsync(runManage, Array.Empty<string>()).ConfigureAwait(false);
            }
            PrintHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        if (IsLegacyAuthCommand(command)) {
            return await RunAuthAsync(new[] { command }.Concat(rest).ToArray()).ConfigureAwait(false);
        }
        return command switch {
            "auth" => await RunAuthAsync(rest).ConfigureAwait(false),
            "analyze" => await Analysis.AnalyzeRunner.RunAsync(rest).ConfigureAwait(false),
            "ci" => await Ci.CiRunner.RunAsync(rest).ConfigureAwait(false),
            "reviewer" => await RunReviewerAsync(rest).ConfigureAwait(false),
            "setup" => await RunSetupAsync(rest).ConfigureAwait(false),
            "manage" => await RunManageWithFallbackAsync(runManage, rest).ConfigureAwait(false),
            "doctor" => await Doctor.DoctorRunner.RunAsync(rest).ConfigureAwait(false),
            "todo" => await Todo.TodoRunner.RunAsync(rest).ConfigureAwait(false),
            "telemetry" => await Telemetry.TelemetryRunner.RunAsync(rest).ConfigureAwait(false),
            "release" => await RunReleaseAsync(rest).ConfigureAwait(false),
            "models" => await ModelsRunner.RunAsync(rest).ConfigureAwait(false),
            "usage" => await UsageRunner.RunAsync(rest).ConfigureAwait(false),
            "heatmap" => await HeatmapRunner.RunAsync(rest).ConfigureAwait(false),
            "help" or "-h" or "--help" => PrintHelpReturn(),
            _ => PrintHelpReturn()
        };
    }

    private static async Task<int> RunManageWithFallbackAsync(Func<string[], Task<int>> runManage, string[] args) {
        try {
            return await runManage(args).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine("Failed to launch management hub.");
            if (ShouldShowDetailedErrors()) {
                Console.Error.WriteLine(ex.ToString());
            } else {
                Console.Error.WriteLine("Set INTELLIGENCEX_DEBUG=1 for exception details.");
            }
            PrintHelp();
            return ManageLaunchFailureExitCode;
        }
    }

    internal static bool ShouldShowDetailedErrors() {
        return IsTruthyFlag(Environment.GetEnvironmentVariable("INTELLIGENCEX_DEBUG"))
               || IsTruthyFlag(Environment.GetEnvironmentVariable("INTELLIGENCEX_VERBOSE"));
    }

    private static bool IsTruthyFlag(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunAuthAsync(string[] args) {
        if (args.Length == 0) {
            PrintAuthHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        return command switch {
            "login" => await RunLoginAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "list" => await RunListAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "export" => await RunExportAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "sync-codex" => await RunSyncCodexAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "help" or "-h" or "--help" => PrintAuthHelpReturn(),
            _ => PrintAuthHelpReturn()
        };
    }

    private static async Task<int> RunReviewerAsync(string[] args) {
        if (args.Length == 0 || args[0].Equals("run", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Length == 0 ? Array.Empty<string>() : args.Skip(1).ToArray();
            return await IntelligenceX.Reviewer.ReviewerApp.RunAsync(rest).ConfigureAwait(false);
        }
        if (args[0].Equals("resolve-threads", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Skip(1).ToArray();
            return await ReviewThreads.ReviewThreadResolveRunner.RunAsync(rest).ConfigureAwait(false);
        }
        if (args[0].Equals("threads", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Skip(1).ToArray();
            if (rest.Length > 0 && rest[0].Equals("resolve", StringComparison.OrdinalIgnoreCase)) {
                return await ReviewThreads.ReviewThreadResolveRunner.RunAsync(rest.Skip(1).ToArray()).ConfigureAwait(false);
            }
        }
        if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)) {
            PrintReviewerHelp();
            return 0;
        }

        PrintReviewerHelp();
        return 1;
    }

    private static async Task<int> RunSetupAsync(string[] args) {
        if (args.Length > 0 && args[0].Equals("wizard", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Skip(1).ToArray();
            return await Setup.Wizard.WizardRunner.RunAsync(rest).ConfigureAwait(false);
        }
        if (args.Length > 0 && args[0].Equals("web", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Skip(1).ToArray();
            return await Setup.Web.WebRunner.RunAsync(rest).ConfigureAwait(false);
        }
        if (args.Length > 0 && args[0].Equals("autodetect", StringComparison.OrdinalIgnoreCase)) {
            var rest = args.Skip(1).ToArray();
            return await Setup.Onboarding.SetupOnboardingAutoDetectCliRunner.RunAsync(rest).ConfigureAwait(false);
        }
        return await Setup.SetupRunner.RunAsync(args).ConfigureAwait(false);
    }

    private static async Task<int> RunReleaseAsync(string[] args) {
        if (args.Length == 0) {
            PrintReleaseHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "notes" => await ReleaseNotes.ReleaseNotesRunner.RunAsync(rest).ConfigureAwait(false),
            "reviewer" => await Release.ReleaseReviewerRunner.RunAsync(rest).ConfigureAwait(false),
            "help" or "-h" or "--help" => PrintReleaseHelpReturn(),
            _ => PrintReleaseHelpReturn()
        };
    }

    private static bool IsLegacyAuthCommand(string command) {
        return command switch {
            "login" => true,
            "export" => true,
            "sync-codex" => true,
            _ => false
        };
    }

    private static async Task<int> RunListAsync(string[] args) {
        try {
            if (args.Length > 0 && (args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))) {
                Console.WriteLine("Usage: intelligencex auth list");
                return 0;
            }

            var path = AuthPaths.ResolveAuthPath();
            if (!File.Exists(path)) {
                Console.Error.WriteLine("No auth store found.");
                return 1;
            }
            var raw = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw)) {
                Console.Error.WriteLine("Auth store is empty.");
                return 1;
            }

            var json = AuthStoreUtils.DecryptAuthStoreIfNeeded(raw);
            var entries = AuthStoreUtils.ParseAuthStoreEntries(json);
            if (entries.Count == 0) {
                Console.Error.WriteLine("No auth bundles found.");
                return 1;
            }

            Console.WriteLine("Auth store bundles:");
            foreach (var e in entries
                         .OrderBy(e => e.Provider, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(e => e.AccountId ?? string.Empty, StringComparer.OrdinalIgnoreCase)) {
                var account = string.IsNullOrWhiteSpace(e.AccountId) ? "-" : e.AccountId;
                var expires = e.ExpiresAt.HasValue ? e.ExpiresAt.Value.ToUniversalTime().ToString("u") : "-";
                Console.WriteLine($"- Provider: {e.Provider}; Account: {account}; Expires: {expires}");
            }
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunLoginAsync(string[] args) {
        try {
            var loginOptions = ParseLoginOptions(args);
            var config = OAuthConfig.FromEnvironment();
            config.Validate();
            var service = new OAuthLoginService();
            var oauthOptions = new OAuthLoginOptions(config) {
                OnAuthUrl = async url => {
                    TryOpenBrowser(url);
                    Console.WriteLine($"Open: {url}");
                    await Task.CompletedTask;
                },
                OnPrompt = async message => {
                    Console.Write(message + " ");
                    var input = Console.ReadLine();
                    return await Task.FromResult(input ?? string.Empty);
                }
            };

            var result = await service.LoginAsync(oauthOptions).ConfigureAwait(false);
            var store = new FileAuthBundleStore();
            await store.SaveAsync(result.Bundle).ConfigureAwait(false);
            Console.WriteLine("Login complete. Credentials saved.");

            if (loginOptions.Export || loginOptions.SetGitHubSecret || !string.IsNullOrWhiteSpace(loginOptions.OutputPath) || loginOptions.PrintExport) {
                var format = string.IsNullOrWhiteSpace(loginOptions.ExportFormat) ? "store-base64" : loginOptions.ExportFormat!;
                if (!IsStoreFormat(format)) {
                    Console.Error.WriteLine("Login export supports store or store-base64 formats.");
                    return 1;
                }
                if (loginOptions.SetGitHubSecret && !IsStoreBase64Format(format)) {
                    Console.Error.WriteLine("GitHub secrets require store-base64 export.");
                    return 1;
                }
                var export = await ExportAuthStoreContentAsync(format).ConfigureAwait(false);
                if (export is null) {
                    return 1;
                }

                if (loginOptions.SetGitHubSecret) {
                    if (!ResolveSecretTarget(loginOptions)) {
                        return 1;
                    }
                    var token = ResolveGitHubToken(loginOptions.GitHubToken);
                    if (string.IsNullOrWhiteSpace(token)) {
                        Console.Error.WriteLine("Missing GitHub token. Use --github-token or set INTELLIGENCEX_GITHUB_TOKEN/GITHUB_TOKEN/GH_TOKEN.");
                        return 1;
                    }
                    using var client = new GitHubSecretsClient(token!);
                    if (!string.IsNullOrWhiteSpace(loginOptions.Repo)) {
                        if (!TryParseRepo(loginOptions.Repo!, out var owner, out var repo)) {
                            Console.Error.WriteLine("Repo must be in owner/name format.");
                            return 1;
                        }
                        await client.SetRepoSecretAsync(owner, repo, loginOptions.SecretName, export).ConfigureAwait(false);
                        Console.WriteLine($"Updated secret {loginOptions.SecretName} for {owner}/{repo}.");
                    } else if (!string.IsNullOrWhiteSpace(loginOptions.Org)) {
                        var visibility = string.IsNullOrWhiteSpace(loginOptions.Visibility) ? "all" : loginOptions.Visibility!;
                        await client.SetOrgSecretAsync(loginOptions.Org!, loginOptions.SecretName, export, visibility).ConfigureAwait(false);
                        Console.WriteLine($"Updated org secret {loginOptions.SecretName} for {loginOptions.Org} (visibility: {visibility}).");
                    } else {
                        Console.Error.WriteLine("Specify --repo or --org when using --set-github-secret.");
                        return 1;
                    }
                }

                if (!string.IsNullOrWhiteSpace(loginOptions.OutputPath)) {
                    await File.WriteAllTextAsync(loginOptions.OutputPath!, export).ConfigureAwait(false);
                    Console.WriteLine($"Wrote {loginOptions.OutputPath}");
                }

                if (loginOptions.PrintExport || (!loginOptions.SetGitHubSecret && string.IsNullOrWhiteSpace(loginOptions.OutputPath))) {
                    Console.WriteLine(export);
                }
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

}
