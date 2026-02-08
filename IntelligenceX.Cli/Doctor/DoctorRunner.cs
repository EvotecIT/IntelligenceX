using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Auth;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Doctor;

internal static class DoctorRunner {
    private sealed class Options {
        public string Workspace { get; set; } = Environment.CurrentDirectory;
        public string? ConfigPath { get; set; }
        public string? Repo { get; set; }
        public bool SkipOpenAi { get; set; }
        public bool SkipGitHub { get; set; }
        public bool ShowHelp { get; set; }
    }

    private sealed class ReviewConfig {
        public string Provider { get; set; } = "openai";
        public string Model { get; set; } = "gpt-5.3-codex";
        public OpenAITransportKind Transport { get; set; } = OpenAITransportKind.Native;
        public string? OpenAiAccountId { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        var failures = 0;
        var warnings = 0;

        var workspace = options.Workspace;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) {
            Console.Error.WriteLine("[FAIL] Workspace not found.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Repo)) {
            options.Repo = TryResolveRepoFromEnvironmentOrGit(workspace);
        }

        var configPath = ResolveConfigPath(options, workspace);
        var config = TryLoadReviewConfig(configPath, out var configError);
        if (config is null) {
            warnings++;
            Console.Error.WriteLine($"[WARN] Reviewer config not loaded ({configError}).");
            Console.Error.WriteLine("       Expected .intelligencex/reviewer.json or pass --config <path>.");
        } else {
            Console.WriteLine($"[OK] Reviewer config: {configPath}");
            Console.WriteLine($"     Provider={config.Provider}; Transport={config.Transport}; Model={config.Model}");
        }

        if (!options.SkipOpenAi) {
            var requiresAuthStore = config is null
                ? false
                : RequiresOpenAiAuthStore(config.Provider, config.Transport);
            if (requiresAuthStore) {
                var (ok, warnCount, errorMessage) = CheckOpenAiAuthStore(config!);
                warnings += warnCount;
                if (!ok) {
                    failures++;
                    Console.Error.WriteLine($"[FAIL] {errorMessage}");
                }
            } else {
                Console.WriteLine("[OK] OpenAI auth store: not required for current config.");
            }
        }

        if (!options.SkipGitHub) {
            var githubFailures = await CheckGitHubAsync(options.Repo).ConfigureAwait(false);
            failures += githubFailures.Failures;
            warnings += githubFailures.Warnings;
        }

        if (failures == 0 && warnings == 0) {
            Console.WriteLine("[OK] Doctor checks passed.");
            return 0;
        }
        if (failures == 0) {
            Console.WriteLine("[OK] Doctor checks passed with warnings.");
            return 0;
        }
        Console.Error.WriteLine("[FAIL] Doctor checks failed.");
        return 1;
    }

    private static string ResolveConfigPath(Options options, string workspace) {
        if (!string.IsNullOrWhiteSpace(options.ConfigPath)) {
            return options.ConfigPath!;
        }
        return Path.Combine(workspace, ".intelligencex", "reviewer.json");
    }

    private static ReviewConfig? TryLoadReviewConfig(string path, out string error) {
        error = "file not found";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return null;
        }
        try {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) {
                error = "empty file";
                return null;
            }
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var review = root;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("review", out var reviewObj) &&
                reviewObj.ValueKind == JsonValueKind.Object) {
                review = reviewObj;
            }

            var config = new ReviewConfig();
            if (TryGetString(review, "provider", out var provider) && !string.IsNullOrWhiteSpace(provider)) {
                config.Provider = provider!;
            }

            // Prefer schema-aligned "model" but keep legacy fallback.
            if (TryGetString(review, "model", out var model) && !string.IsNullOrWhiteSpace(model)) {
                config.Model = model!;
            } else if (TryGetString(review, "openaiModel", out var legacyModel) && !string.IsNullOrWhiteSpace(legacyModel)) {
                config.Model = legacyModel!;
            }

            if (TryGetString(review, "openaiTransport", out var transportStr) && !string.IsNullOrWhiteSpace(transportStr)) {
                config.Transport = ParseTransportOrDefault(transportStr!, config.Transport);
            } else if (TryGetString(review, "openaiTransportKind", out var transportKind) && !string.IsNullOrWhiteSpace(transportKind)) {
                config.Transport = ParseTransportOrDefault(transportKind!, config.Transport);
            }

            if (TryGetString(review, "openaiAccountId", out var accountId) && !string.IsNullOrWhiteSpace(accountId)) {
                config.OpenAiAccountId = accountId!;
            } else if (TryGetString(review, "authAccountId", out var authAccountId) && !string.IsNullOrWhiteSpace(authAccountId)) {
                config.OpenAiAccountId = authAccountId!;
            }

            error = string.Empty;
            return config;
        } catch (Exception ex) {
            error = ex.Message;
            return null;
        }
    }

    private static bool TryGetString(JsonElement obj, string name, out string? value) {
        value = null;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        if (!obj.TryGetProperty(name, out var prop)) {
            return false;
        }
        if (prop.ValueKind != JsonValueKind.String) {
            return false;
        }
        value = prop.GetString();
        return true;
    }

    private static OpenAITransportKind ParseTransportOrDefault(string value, OpenAITransportKind fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return value.Trim().ToLowerInvariant() switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => fallback
        };
    }

    private static bool RequiresOpenAiAuthStore(string provider, OpenAITransportKind transport) {
        if (string.IsNullOrWhiteSpace(provider)) {
            return false;
        }
        var p = provider.Trim().ToLowerInvariant();
        var isOpenAi = p is "openai" or "codex" or "chatgpt";
        if (!isOpenAi) {
            return false;
        }
        // Native transport uses ChatGPT OAuth auth store.
        return transport == OpenAITransportKind.Native;
    }

    private static (bool Ok, int Warnings, string ErrorMessage) CheckOpenAiAuthStore(ReviewConfig config) {
        var authPath = AuthPaths.ResolveAuthPath();
        if (!File.Exists(authPath)) {
            return (false, 0,
                $"Missing OpenAI auth store at {authPath}. Run `intelligencex auth login` (or set INTELLIGENCEX_AUTH_B64 in CI).");
        }
        string raw;
        try {
            raw = File.ReadAllText(authPath);
        } catch (Exception ex) {
            return (false, 0, $"Failed to read OpenAI auth store: {ex.Message}");
        }
        if (string.IsNullOrWhiteSpace(raw)) {
            return (false, 0, "OpenAI auth store is empty.");
        }

        List<AuthStoreEntry> entries;
        try {
            var json = AuthStoreUtils.DecryptAuthStoreIfNeeded(raw);
            entries = AuthStoreUtils.ParseAuthStoreEntries(json);
        } catch (Exception ex) {
            return (false, 0, $"Failed to load OpenAI auth store: {ex.Message}");
        }
        if (entries.Count == 0) {
            return (false, 0, "No auth bundles found in OpenAI auth store.");
        }

        var relevant = entries.Where(e =>
                string.Equals(e.Provider, "openai-codex", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Provider, "chatgpt", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (relevant.Count == 0) {
            return (false, 0, $"No OpenAI bundles found in auth store ({authPath}).");
        }

        var selectedAccount = FirstNonEmpty(
            config.OpenAiAccountId,
            Environment.GetEnvironmentVariable("INTELLIGENCEX_OPENAI_ACCOUNT_ID"));
        if (!string.IsNullOrWhiteSpace(selectedAccount)) {
            var has = relevant.Any(e => string.Equals(e.AccountId, selectedAccount, StringComparison.OrdinalIgnoreCase));
            if (!has) {
                return (false, 0, $"No OpenAI bundle found for account '{selectedAccount}' in {authPath}. Run `intelligencex auth list`.");
            }
        }

        var warnings = 0;
        var byAccount = relevant
            .Select(e => string.IsNullOrWhiteSpace(e.AccountId) ? "-" : e.AccountId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byAccount.Count > 1 && string.IsNullOrWhiteSpace(selectedAccount)) {
            warnings++;
            Console.Error.WriteLine("[WARN] Multiple ChatGPT accounts found in auth store.");
            Console.Error.WriteLine($"       Accounts: {string.Join(", ", byAccount)}");
            Console.Error.WriteLine("       Set review.openaiAccountId in .intelligencex/reviewer.json or INTELLIGENCEX_OPENAI_ACCOUNT_ID.");
        }

        var soon = DateTimeOffset.UtcNow.AddDays(7);
        var expiring = relevant.Where(e => e.ExpiresAt.HasValue && e.ExpiresAt.Value <= soon).ToList();
        if (expiring.Count > 0) {
            warnings++;
            var min = expiring.Min(e => e.ExpiresAt!.Value).ToUniversalTime().ToString("u");
            Console.Error.WriteLine($"[WARN] OpenAI access token expires soon (min expiry {min}). If reviews start failing, re-run `intelligencex auth login`.");
        }

        Console.WriteLine($"[OK] OpenAI auth store: {authPath}");
        return (true, warnings, string.Empty);
    }

    private sealed class GitHubCheckResult {
        public int Failures { get; set; }
        public int Warnings { get; set; }
    }

    private static async Task<GitHubCheckResult> CheckGitHubAsync(string? repo) {
        var result = new GitHubCheckResult();
        var token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN"),
            Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
            Environment.GetEnvironmentVariable("GH_TOKEN"));
        if (string.IsNullOrWhiteSpace(token)) {
            result.Warnings++;
            Console.Error.WriteLine("[WARN] No GitHub token found (INTELLIGENCEX_GITHUB_TOKEN/GITHUB_TOKEN/GH_TOKEN). Skipping GitHub API checks.");
            return result;
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX", "doctor"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Basic token validity + scope visibility.
        using (var response = await http.GetAsync("https://api.github.com/user").ConfigureAwait(false)) {
            if (!response.IsSuccessStatusCode) {
                result.Failures++;
                var detail = await ReadResponseSnippetAsync(response).ConfigureAwait(false);
                Console.Error.WriteLine($"[FAIL] GitHub token check failed ({(int)response.StatusCode}). {detail}");
                PrintGitHubHeaders(response);
                return result;
            }
            var scopes = TryGetHeader(response, "X-OAuth-Scopes");
            if (!string.IsNullOrWhiteSpace(scopes)) {
                Console.WriteLine($"[OK] GitHub token scopes: {scopes}");
            } else {
                Console.WriteLine("[OK] GitHub token: valid (scopes header not provided).");
            }
        }

        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/')) {
            result.Warnings++;
            Console.Error.WriteLine("[WARN] --repo not provided. Skipping repo permission probes.");
            return result;
        }
        var parts = repo.Split('/', 2);
        var owner = parts[0];
        var name = parts[1];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)) {
            result.Warnings++;
            Console.Error.WriteLine("[WARN] Invalid --repo value. Expected owner/name.");
            return result;
        }

        // Non-destructive probes that correlate well with setup requirements.
        await ProbeGitHubAsync(http, $"https://api.github.com/repos/{owner}/{name}", "Repo access", result).ConfigureAwait(false);
        await ProbeGitHubAsync(http, $"https://api.github.com/repos/{owner}/{name}/actions/secrets/public-key", "Actions secrets access", result)
            .ConfigureAwait(false);
        await ProbeGitHubAsync(http, $"https://api.github.com/repos/{owner}/{name}/actions/workflows", "Workflows access", result)
            .ConfigureAwait(false);

        return result;
    }

    private static async Task ProbeGitHubAsync(HttpClient http, string url, string label, GitHubCheckResult result) {
        using var response = await http.GetAsync(url).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) {
            Console.WriteLine($"[OK] GitHub: {label}");
            return;
        }
        // 404 can mean the token doesn't have access, or repo/feature absent.
        result.Warnings++;
        var detail = await ReadResponseSnippetAsync(response).ConfigureAwait(false);
        Console.Error.WriteLine($"[WARN] GitHub: {label} probe failed ({(int)response.StatusCode}). {detail}");
        PrintGitHubHeaders(response);
    }

    private static void PrintGitHubHeaders(HttpResponseMessage response) {
        var accepted = TryGetHeader(response, "X-Accepted-GitHub-Permissions");
        if (!string.IsNullOrWhiteSpace(accepted)) {
            Console.Error.WriteLine($"       X-Accepted-GitHub-Permissions: {accepted}");
        }
        var scopes = TryGetHeader(response, "X-OAuth-Scopes");
        if (!string.IsNullOrWhiteSpace(scopes)) {
            Console.Error.WriteLine($"       X-OAuth-Scopes: {scopes}");
        }
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name) {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private static async Task<string> ReadResponseSnippetAsync(HttpResponseMessage response) {
        try {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) {
                return string.Empty;
            }
            // Keep logs concise.
            body = body.Trim();
            return body.Length > 240 ? body.Substring(0, 240) + "…" : body;
        } catch {
            return string.Empty;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) {
        foreach (var v in values) {
            if (!string.IsNullOrWhiteSpace(v)) {
                return v.Trim();
            }
        }
        return null;
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--workspace":
                    if (i + 1 < args.Length) {
                        options.Workspace = args[++i];
                    }
                    break;
                case "--config":
                    if (i + 1 < args.Length) {
                        options.ConfigPath = args[++i];
                    }
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--skip-openai":
                    options.SkipOpenAi = true;
                    break;
                case "--skip-github":
                    options.SkipGitHub = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage: intelligencex doctor [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --workspace <path>     Workspace root (default: current directory)");
        Console.WriteLine("  --config <path>        Reviewer config path (default: <workspace>/.intelligencex/reviewer.json)");
        Console.WriteLine("  --repo <owner/name>    Optional GitHub repo for permission probes");
        Console.WriteLine("  --skip-openai          Skip OpenAI auth store checks");
        Console.WriteLine("  --skip-github          Skip GitHub token/API checks");
    }

    private static string? TryResolveRepoFromEnvironmentOrGit(string workspace) {
        var envRepo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO")
                   ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(envRepo) && envRepo.Contains('/')) {
            return envRepo.Trim();
        }
        return TryResolveRepoFromGit(workspace);
    }

    private static string? TryResolveRepoFromGit(string start) {
        var root = TryFindGitRoot(start);
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
            if (Directory.Exists(gitDir) || File.Exists(gitDir)) {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }

    private static string? TryReadGitRemoteUrl(string configPath, string remoteName) {
        try {
            var lines = File.ReadAllLines(configPath);
            var inRemote = false;
            foreach (var raw in lines) {
                var line = raw.Trim();
                if (line.StartsWith("[remote \"", StringComparison.OrdinalIgnoreCase)) {
                    inRemote = line.Contains($"\"{remoteName}\"", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inRemote && line.StartsWith("url", StringComparison.OrdinalIgnoreCase)) {
                    var idx = line.IndexOf('=');
                    if (idx >= 0) {
                        return line.Substring(idx + 1).Trim();
                    }
                }
            }
        } catch {
            // ignore
        }
        return null;
    }

    private static string? TryReadFirstRemoteUrl(string configPath) {
        try {
            var lines = File.ReadAllLines(configPath);
            var inRemote = false;
            foreach (var raw in lines) {
                var line = raw.Trim();
                if (line.StartsWith("[remote \"", StringComparison.OrdinalIgnoreCase)) {
                    inRemote = true;
                    continue;
                }
                if (inRemote && line.StartsWith("url", StringComparison.OrdinalIgnoreCase)) {
                    var idx = line.IndexOf('=');
                    if (idx >= 0) {
                        return line.Substring(idx + 1).Trim();
                    }
                }
            }
        } catch {
            // ignore
        }
        return null;
    }

    private static string? ParseGitHubRepoFromUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }
        var u = url.Trim();
        if (u.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) {
            var repo = u.Substring("git@github.com:".Length);
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
                repo = repo.Substring(0, repo.Length - 4);
            }
            return repo.Contains('/') ? repo : null;
        }
        if (u.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
            u.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase)) {
            var idx = u.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
            var repo = u.Substring(idx + "github.com/".Length);
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
                repo = repo.Substring(0, repo.Length - 4);
            }
            var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) {
                return $"{parts[0]}/{parts[1]}";
            }
        }
        return null;
    }
}
