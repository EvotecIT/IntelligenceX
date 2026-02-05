using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IntelligenceX.Cli.Usage;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        if (args.Length == 0) {
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
            "reviewer" => await RunReviewerAsync(rest).ConfigureAwait(false),
            "setup" => await RunSetupAsync(rest).ConfigureAwait(false),
            "release" => await RunReleaseAsync(rest).ConfigureAwait(false),
            "usage" => await UsageRunner.RunAsync(rest).ConfigureAwait(false),
            "help" or "-h" or "--help" => PrintHelpReturn(),
            _ => PrintHelpReturn()
        };
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp() {
        Console.WriteLine("IntelligenceX CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex auth <command>");
        Console.WriteLine("  intelligencex reviewer run [options]");
        Console.WriteLine("  intelligencex setup [options]");
        Console.WriteLine("  intelligencex setup wizard [options]");
        Console.WriteLine("  intelligencex setup web [url]");
        Console.WriteLine("  intelligencex release <command>");
        Console.WriteLine("  intelligencex usage [options]");
        Console.WriteLine();
        Console.WriteLine("Auth commands:");
        Console.WriteLine("  auth login       Start OAuth login flow and store credentials");
        Console.WriteLine("  auth export      Export stored credentials (json or base64)");
        Console.WriteLine("  auth sync-codex  Write tokens to CODEX_HOME/auth.json");
        Console.WriteLine();
        Console.WriteLine("Reviewer commands:");
        Console.WriteLine("  reviewer run     Run reviewer using GitHub event payload or inputs");
        Console.WriteLine("  reviewer resolve-threads   Auto-resolve IntelligenceX bot review threads");
        Console.WriteLine();
        Console.WriteLine("Setup:");
        Console.WriteLine("  setup            Configure GitHub Actions workflow and secrets");
        Console.WriteLine();
        Console.WriteLine("Release commands:");
        Console.WriteLine("  release notes    Generate release notes from git tags/commits");
        Console.WriteLine("  release reviewer Build and publish reviewer release assets");
        Console.WriteLine();
        Console.WriteLine("Environment variables (optional overrides):");
        Console.WriteLine("  OPENAI_AUTH_AUTHORIZE_URL, OPENAI_AUTH_TOKEN_URL, OPENAI_AUTH_CLIENT_ID");
        Console.WriteLine("  OPENAI_AUTH_SCOPES, OPENAI_AUTH_REDIRECT_URL");
        Console.WriteLine("  INTELLIGENCEX_AUTH_PATH (optional)");
        Console.WriteLine("  INTELLIGENCEX_AUTH_KEY (base64 32 bytes to encrypt store)");
        Console.WriteLine("  CODEX_HOME (used by sync-codex)");
    }

    private static async Task<int> RunAuthAsync(string[] args) {
        if (args.Length == 0) {
            PrintAuthHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        return command switch {
            "login" => await RunLoginAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
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

    private static int PrintAuthHelpReturn() {
        PrintAuthHelp();
        return 1;
    }

    private static void PrintAuthHelp() {
        Console.WriteLine("Auth commands:");
        Console.WriteLine("  intelligencex auth login [options]");
        Console.WriteLine("  intelligencex auth export");
        Console.WriteLine("  intelligencex auth sync-codex");
        Console.WriteLine();
        Console.WriteLine("Auth login options:");
        Console.WriteLine("  --export [format]              Export auth store after login (default: store-base64)");
        Console.WriteLine("  --out <path>                   Write export to file");
        Console.WriteLine("  --print                        Print export to stdout");
        Console.WriteLine("  --set-github-secret [name]     Upload export to GitHub Actions secret (default name: INTELLIGENCEX_AUTH_B64)");
        Console.WriteLine("  --repo <owner/name>            Target repository secret");
        Console.WriteLine("  --org <org>                    Target organization secret (visibility defaults to all)");
        Console.WriteLine("  --visibility <all|private|selected>     Org secret visibility");
        Console.WriteLine("  --github-token <token>         Token for GitHub API (or set INTELLIGENCEX_GITHUB_TOKEN/GITHUB_TOKEN/GH_TOKEN)");
    }

    private static void PrintReviewerHelp() {
        Console.WriteLine("Reviewer commands:");
        Console.WriteLine("  intelligencex reviewer run [options]");
        Console.WriteLine("  intelligencex reviewer resolve-threads [options]");
        Console.WriteLine("  intelligencex reviewer threads resolve [options]");
        Console.WriteLine();
        Console.WriteLine("Reviewer run options: run `intelligencex reviewer run --help` for the full list.");
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

    private static int PrintReleaseHelpReturn() {
        PrintReleaseHelp();
        return 1;
    }

    private static void PrintReleaseHelp() {
        Console.WriteLine("Release commands:");
        Console.WriteLine("  release notes    Generate release notes from git tags/commits");
        Console.WriteLine("  release reviewer Build and publish reviewer release assets");
        Console.WriteLine("  release help");
    }

    private static bool IsLegacyAuthCommand(string command) {
        return command switch {
            "login" => true,
            "export" => true,
            "sync-codex" => true,
            _ => false
        };
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
            var format = ResolveExportFormat(args)
                         ?? Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_EXPORT_FORMAT")
                         ?? "json";
            var normalized = format.Trim().ToLowerInvariant();
            if (normalized is "store" or "store-base64" or "store_b64") {
                return await ExportAuthStoreAsync(normalized).ConfigureAwait(false);
            }

            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false);
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
            var repo = TryResolveRepoFromGit();
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

    private static string? TryResolveRepoFromGit() {
        var root = TryFindGitRoot(Directory.GetCurrentDirectory());
        if (string.IsNullOrWhiteSpace(root)) {
            return null;
        }
        var configPath = Path.Combine(root, ".git", "config");
        if (!File.Exists(configPath)) {
            return null;
        }
        var url = TryReadGitRemoteUrl(configPath, "origin") ?? TryReadFirstRemoteUrl(configPath);
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }
        return ParseGitHubRepoFromUrl(url);
    }

    private static string? TryFindGitRoot(string start) {
        var current = new DirectoryInfo(start);
        while (current is not null) {
            var gitDir = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitDir)) {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }

    private static string? TryReadGitRemoteUrl(string configPath, string remoteName) {
        var lines = File.ReadAllLines(configPath);
        var inRemote = false;
        foreach (var raw in lines) {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) {
                inRemote = line.Equals($"[remote \"{remoteName}\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inRemote) {
                continue;
            }
            if (line.StartsWith("url", StringComparison.OrdinalIgnoreCase)) {
                var idx = line.IndexOf('=');
                if (idx >= 0) {
                    return line[(idx + 1)..].Trim();
                }
            }
        }
        return null;
    }

    private static string? TryReadFirstRemoteUrl(string configPath) {
        var lines = File.ReadAllLines(configPath);
        var inRemote = false;
        foreach (var raw in lines) {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) {
                inRemote = line.StartsWith("[remote ", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inRemote) {
                continue;
            }
            if (line.StartsWith("url", StringComparison.OrdinalIgnoreCase)) {
                var idx = line.IndexOf('=');
                if (idx >= 0) {
                    return line[(idx + 1)..].Trim();
                }
            }
        }
        return null;
    }

    private static string? ParseGitHubRepoFromUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }
        var trimmed = url.Trim();
        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) {
            var path = trimmed.Substring("git@github.com:".Length);
            return NormalizeRepoPath(path);
        }
        if (trimmed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase)) {
            var uri = new Uri(trimmed);
            var path = uri.AbsolutePath.Trim('/');
            return NormalizeRepoPath(path);
        }
        return null;
    }

    private static string? NormalizeRepoPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        var cleaned = path.Trim().TrimEnd('/');
        if (cleaned.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
            cleaned = cleaned.Substring(0, cleaned.Length - 4);
        }
        var parts = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) {
            return null;
        }
        return $"{parts[0]}/{parts[1]}";
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
