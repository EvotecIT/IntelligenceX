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
using IntelligenceX.Cli.Usage;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli;

internal static partial class Program {
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

    private static async Task<int> RunSyncCodexAsync(string[] args) {
        try {
            var provider = "openai-codex";
            string? accountId = null;
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (arg is "-h" or "--help") {
                    PrintSyncCodexHelp();
                    return 0;
                }
                switch (arg) {
                    case "--provider":
                        if (!TryReadRequiredValue(args, ref i, out var parsedProvider, out var providerError)) {
                            Console.Error.WriteLine(providerError);
                            PrintSyncCodexHelp();
                            return 1;
                        }
                        provider = parsedProvider;
                        break;
                    case "--account-id":
                        if (!TryReadRequiredValue(args, ref i, out var parsedAccountId, out var accountError)) {
                            Console.Error.WriteLine(accountError);
                            PrintSyncCodexHelp();
                            return 1;
                        }
                        accountId = parsedAccountId;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown option or unexpected argument: {arg}");
                        PrintSyncCodexHelp();
                        return 1;
                }
            }

            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync(provider, accountId).ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine(string.IsNullOrWhiteSpace(accountId)
                    ? $"No auth bundle found for provider '{provider}'."
                    : $"No auth bundle found for provider '{provider}' and account '{accountId}'.");
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

    private static void PrintSyncCodexHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex auth sync-codex [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --provider <id>     Provider id to export (default: openai-codex)");
        Console.WriteLine("  --account-id <id>   Account id when multiple bundles exist");
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

    private static bool TryReadRequiredValue(string[] args, ref int index, out string value, out string? error) {
        if (index + 1 >= args.Length) {
            value = string.Empty;
            error = $"Missing value for {args[index]}.";
            return false;
        }
        var candidate = args[index + 1];
        if (candidate.StartsWith("--", StringComparison.Ordinal)) {
            value = string.Empty;
            error = $"Missing value for {args[index]}.";
            return false;
        }
        index++;
        value = candidate;
        error = null;
        return true;
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
