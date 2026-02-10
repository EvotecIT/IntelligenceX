using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using IntelligenceX.Cli.Auth;
using IntelligenceX.Cli.Usage;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli;

internal static partial class Program {
    private static async Task<int> Main(string[] args) {
        if (args.Length == 0) {
            if (CanLaunchManageHub()) {
                return await RunManageAsync(Array.Empty<string>()).ConfigureAwait(false);
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
            "manage" => await RunManageAsync(rest).ConfigureAwait(false),
            "doctor" => await Doctor.DoctorRunner.RunAsync(rest).ConfigureAwait(false),
            "todo" => await Todo.TodoRunner.RunAsync(rest).ConfigureAwait(false),
            "release" => await RunReleaseAsync(rest).ConfigureAwait(false),
            "usage" => await UsageRunner.RunAsync(rest).ConfigureAwait(false),
            "help" or "-h" or "--help" => PrintHelpReturn(),
            _ => PrintHelpReturn()
        };
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
            "sync-codex" => await RunSyncCodexAsync().ConfigureAwait(false),
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

    private static async Task<int> RunExportAsync(string[] args) {
        try {
            var providerFilter = ReadArgValue(args, "--provider");
            var accountIdFilter = ReadArgValue(args, "--account-id");
            var format = ResolveExportFormat(args)
                         ?? Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_EXPORT_FORMAT")
                         ?? "json";
            var normalized = format.Trim().ToLowerInvariant();
            if (normalized is "store" or "store-base64" or "store_b64") {
                if (string.IsNullOrWhiteSpace(providerFilter) && string.IsNullOrWhiteSpace(accountIdFilter)) {
                    return await ExportAuthStoreAsync(normalized).ConfigureAwait(false);
                }
                return await ExportAuthStoreFilteredAsync(normalized, providerFilter, accountIdFilter).ConfigureAwait(false);
            }

            var store = new FileAuthBundleStore();
            var provider = string.IsNullOrWhiteSpace(providerFilter) ? "openai-codex" : providerFilter!;
            var bundle = await store.GetAsync(provider, accountIdFilter).ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine("No auth bundle found.");
                return 1;
            }
            var json = AuthBundleSerializer.Serialize(bundle);
            if (normalized == "base64") {
                var bytes = Encoding.UTF8.GetBytes(json);
                Console.WriteLine(Convert.ToBase64String(bytes));
            } else {
                Console.WriteLine(json);
            }
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunSyncCodexAsync() {
        try {
            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine("No auth bundle found.");
                return 1;
            }
            CodexAuthStore.WriteAuthJson(bundle, null, DateTimeOffset.UtcNow);
            Console.WriteLine($"Wrote {CodexAuthStore.ResolveAuthPath()}");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? ResolveExportFormat(string[] args) {
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                return args[i + 1];
            }
            if (arg.Equals("--store", StringComparison.OrdinalIgnoreCase)) {
                return "store";
            }
            if (arg.Equals("--store-base64", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--store-b64", StringComparison.OrdinalIgnoreCase)) {
                return "store-base64";
            }
        }
        return null;
    }

    private static async Task<int> ExportAuthStoreAsync(string format) {
        var path = AuthPaths.ResolveAuthPath();
        if (!File.Exists(path)) {
            Console.Error.WriteLine("No auth store found.");
            return 1;
        }
        var content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            Console.Error.WriteLine("Auth store is empty.");
            return 1;
        }
        if (format is "store-base64" or "store_b64") {
            var bytes = Encoding.UTF8.GetBytes(content);
            Console.WriteLine(Convert.ToBase64String(bytes));
        } else {
            Console.WriteLine(content);
        }
        return 0;
    }

    private static async Task<int> ExportAuthStoreFilteredAsync(string format, string? providerFilter, string? accountIdFilter) {
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
        var node = JsonNode.Parse(json) as JsonObject;
        if (node is null) {
            Console.Error.WriteLine("Auth store content is invalid.");
            return 1;
        }
        var bundles = node["bundles"] as JsonObject;
        if (bundles is null) {
            Console.Error.WriteLine("Auth store has no bundles.");
            return 1;
        }

        var filtered = new JsonObject();
        foreach (var entry in bundles) {
            var bundleObj = entry.Value as JsonObject;
            if (bundleObj is null) {
                continue;
            }
            var provider = (bundleObj["provider"]?.GetValue<string>() ?? string.Empty).Trim();
            var accountId = (bundleObj["account_id"]?.GetValue<string>() ?? bundleObj["accountId"]?.GetValue<string>())?.Trim();
            if (!string.IsNullOrWhiteSpace(providerFilter) && !provider.Equals(providerFilter.Trim(), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(accountIdFilter) && !string.Equals(accountId, accountIdFilter.Trim(), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            filtered[entry.Key] = bundleObj;
        }

        var outRoot = new JsonObject();
        outRoot["version"] = node["version"]?.DeepClone() ?? 1;
        outRoot["bundles"] = filtered;
        var outJson = outRoot.ToJsonString(CliJson.Indented);
        if (IsStoreBase64Format(format)) {
            Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(outJson)));
        } else {
            Console.WriteLine(outJson);
        }
        return 0;
    }

    private static string? ReadArgValue(string[] args, string key) {
        for (var i = 0; i < args.Length; i++) {
            if (!args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (i + 1 >= args.Length) {
                return string.Empty;
            }
            var value = args[i + 1];
            return value.StartsWith("--", StringComparison.Ordinal) ? string.Empty : value;
        }
        return null;
    }

    private static async Task<string?> ExportAuthStoreContentAsync(string format) {
        var path = AuthPaths.ResolveAuthPath();
        if (!File.Exists(path)) {
            Console.Error.WriteLine("No auth store found.");
            return null;
        }
        var content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            Console.Error.WriteLine("Auth store is empty.");
            return null;
        }
        if (IsStoreBase64Format(format)) {
            var bytes = Encoding.UTF8.GetBytes(content);
            return Convert.ToBase64String(bytes);
        }
        return content;
    }

    private static bool IsStoreBase64Format(string format) {
        return format is "store-base64" or "store_b64" or "store-b64";
    }

    private static bool IsStoreFormat(string format) {
        if (IsStoreBase64Format(format)) {
            return true;
        }
        return format is "store";
    }

    private static string? ResolveGitHubToken(string? direct) {
        if (!string.IsNullOrWhiteSpace(direct)) {
            return direct;
        }
        var token = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token)) {
            return token;
        }
        return TryReadGhToken();
    }

    private static bool TryParseRepo(string repo, out string owner, out string name) {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(repo)) {
            return false;
        }
        var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        name = parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(name);
    }

    private static bool ResolveSecretTarget(LoginOptions options) {
        if (!string.IsNullOrWhiteSpace(options.Repo) && !string.IsNullOrWhiteSpace(options.Org)) {
            Console.Error.WriteLine("Choose only one of --repo or --org.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Repo) && string.IsNullOrWhiteSpace(options.Org)) {
            var repo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO")
                       ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
            if (!string.IsNullOrWhiteSpace(repo)) {
                options.Repo = repo;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) && string.IsNullOrWhiteSpace(options.Org)) {
            var org = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_ORG")
                      ?? Environment.GetEnvironmentVariable("GITHUB_ORG")
                      ?? Environment.GetEnvironmentVariable("GITHUB_OWNER");
            if (!string.IsNullOrWhiteSpace(org)) {
                options.Org = org;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) && string.IsNullOrWhiteSpace(options.Org)) {
            var repo = GitHubRepoDetector.TryDetectRepo(Environment.CurrentDirectory);
            if (!string.IsNullOrWhiteSpace(repo)) {
                options.Repo = repo;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) && string.IsNullOrWhiteSpace(options.Org)) {
            Console.Error.WriteLine("Specify --repo or --org when using --set-github-secret.");
            return false;
        }

        return true;
    }

    private static string? TryReadGhToken() {
        try {
            var startInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = "gh",
                Arguments = "auth token",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null) {
                return null;
            }
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            if (process.ExitCode != 0) {
                return null;
            }
            var token = output.Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        } catch {
            return null;
        }
    }

    private sealed class LoginOptions {
        public bool Export { get; set; }
        public string? ExportFormat { get; set; }
        public bool PrintExport { get; set; }
        public string? OutputPath { get; set; }
        public bool SetGitHubSecret { get; set; }
        public string SecretName { get; set; } = "INTELLIGENCEX_AUTH_B64";
        public string? Repo { get; set; }
        public string? Org { get; set; }
        public string? Visibility { get; set; }
        public string? GitHubToken { get; set; }
    }

    private static LoginOptions ParseLoginOptions(string[] args) {
        var options = new LoginOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--export":
                    options.Export = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                        options.ExportFormat = args[++i];
                    }
                    break;
                case "--export-format":
                    options.Export = true;
                    if (i + 1 < args.Length) {
                        options.ExportFormat = args[++i];
                    }
                    break;
                case "--out":
                    options.Export = true;
                    if (i + 1 < args.Length) {
                        options.OutputPath = args[++i];
                    }
                    break;
                case "--print":
                    options.Export = true;
                    options.PrintExport = true;
                    break;
                case "--set-github-secret":
                    options.SetGitHubSecret = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                        options.SecretName = args[++i];
                    }
                    break;
                case "--secret-name":
                    options.SetGitHubSecret = true;
                    if (i + 1 < args.Length) {
                        options.SecretName = args[++i];
                    }
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--org":
                    if (i + 1 < args.Length) {
                        options.Org = args[++i];
                    }
                    break;
                case "--visibility":
                    if (i + 1 < args.Length) {
                        options.Visibility = args[++i];
                    }
                    break;
                case "--github-token":
                    if (i + 1 < args.Length) {
                        options.GitHubToken = args[++i];
                    }
                    break;
            }
        }

        if (options.SetGitHubSecret) {
            options.Export = true;
            if (string.IsNullOrWhiteSpace(options.ExportFormat)) {
                options.ExportFormat = "store-base64";
            }
        }

        return options;
    }

    private static void TryOpenBrowser(string url) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        } catch {
            // Ignore failures.
        }
    }
}
