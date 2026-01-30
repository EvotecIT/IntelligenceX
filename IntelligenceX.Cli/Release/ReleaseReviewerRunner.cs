using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Release;

internal static class ReleaseReviewerRunner {
    public static async Task<int> RunAsync(string[] args) {
        var options = ReleaseReviewerOptions.Parse(args);
        ReleaseReviewerOptions.ApplyEnvDefaults(options);
        if (options.ShowHelp) {
            PrintHelp();
            return 1;
        }

        try {
            var repoPath = options.RepoPath ?? Environment.CurrentDirectory;
            if (!Directory.Exists(repoPath)) {
                Console.Error.WriteLine($"Repository path not found: {repoPath}");
                return 1;
            }

            var tag = string.IsNullOrWhiteSpace(options.Tag)
                ? $"reviewer-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
                : options.Tag!.Trim();
            var title = string.IsNullOrWhiteSpace(options.Title)
                ? $"IntelligenceX Reviewer {tag}"
                : options.Title!.Trim();
            var notes = string.IsNullOrWhiteSpace(options.Notes)
                ? "Automated build from EvotecIT/IntelligenceX."
                : options.Notes!.Trim();

            var releaseRepo = string.IsNullOrWhiteSpace(options.ReleaseRepoSlug)
                ? "EvotecIT/github-actions"
                : options.ReleaseRepoSlug!.Trim();
            if (!TryParseRepoSlug(releaseRepo, out var owner, out var repo)) {
                Console.Error.WriteLine("Release repo must be in owner/name format.");
                return 1;
            }

            var token = ResolveToken(options.Token);
            if (string.IsNullOrWhiteSpace(token)) {
                Console.Error.WriteLine("Missing GitHub token. Set INTELLIGENCEX_RELEASE_TOKEN or GITHUB_TOKEN.");
                return 1;
            }

            var outDir = Path.Combine(repoPath, "out");
            if (Directory.Exists(outDir)) {
                Directory.Delete(outDir, recursive: true);
            }
            Directory.CreateDirectory(outDir);

            var framework = string.IsNullOrWhiteSpace(options.Framework) ? "net8.0" : options.Framework!;
            var configuration = string.IsNullOrWhiteSpace(options.Configuration) ? "Release" : options.Configuration!;
            var projectPath = Path.Combine(repoPath, "IntelligenceX.Reviewer", "IntelligenceX.Reviewer.csproj");
            if (!File.Exists(projectPath)) {
                Console.Error.WriteLine($"Reviewer project not found at {projectPath}");
                return 1;
            }

            var rids = options.Rids.Count == 0
                ? new[] { "linux-x64", "win-x64", "osx-x64" }
                : options.Rids.ToArray();

            var assets = new List<string>();
            foreach (var rid in rids) {
                var ridOut = Path.Combine(outDir, rid);
                Directory.CreateDirectory(ridOut);
                RunDotNet(repoPath, "publish", projectPath, "-c", configuration, "-f", framework, "-r", rid, "--self-contained", "false", "-o", ridOut);
                var zipPath = Path.Combine(repoPath, $"IntelligenceX.Reviewer-{rid}.zip");
                if (File.Exists(zipPath)) {
                    File.Delete(zipPath);
                }
                ZipFile.CreateFromDirectory(ridOut, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                assets.Add(zipPath);
            }

            using var publisher = new GitHubReleasePublisher(token!);
            var release = await publisher.GetOrCreateReleaseAsync(owner, repo, tag, title, notes).ConfigureAwait(false);
            foreach (var assetPath in assets) {
                await publisher.UploadAssetAsync(owner, repo, release.Id, assetPath).ConfigureAwait(false);
            }

            Console.WriteLine($"Published {assets.Count} asset(s) to {owner}/{repo} tag {tag}.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintHelp() {
        Console.WriteLine("Release reviewer commands:");
        Console.WriteLine("  intelligencex release reviewer [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --tag <tag>             Release tag (default: reviewer-YYYYMMDDHHMMSS)");
        Console.WriteLine("  --title <title>         Release title");
        Console.WriteLine("  --notes <text>          Release notes");
        Console.WriteLine("  --repo-slug <owner/name> GitHub repo for release assets (default: EvotecIT/github-actions)");
        Console.WriteLine("  --token <token>         GitHub token (default: INTELLIGENCEX_RELEASE_TOKEN/GITHUB_TOKEN)");
        Console.WriteLine("  --repo <path>           Repository path (default: current directory)");
        Console.WriteLine("  --framework <tfm>       Target framework (default: net8.0)");
        Console.WriteLine("  --configuration <cfg>   Build configuration (default: Release)");
        Console.WriteLine("  --rids <list>           Comma-separated RIDs (default: linux-x64,win-x64,osx-x64)");
        Console.WriteLine("  --help");
    }

    private static void RunDotNet(string repoPath, params string[] args) {
        var psi = new ProcessStartInfo {
            FileName = "dotnet",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null) {
            throw new InvalidOperationException("Failed to start dotnet.");
        }
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "dotnet publish failed." : error.Trim());
        }
        if (!string.IsNullOrWhiteSpace(output)) {
            Console.WriteLine(output.Trim());
        }
    }

    private static string? ResolveToken(string? token) {
        if (!string.IsNullOrWhiteSpace(token)) {
            return token;
        }
        return Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN")
               ?? Environment.GetEnvironmentVariable("INTELLIGENCEX_RELEASE_TOKEN")
               ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GH_TOKEN");
    }

    private static bool TryParseRepoSlug(string value, out string owner, out string repo) {
        owner = string.Empty;
        repo = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        var parts = value.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        repo = parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }
}

internal sealed class ReleaseReviewerOptions {
    public string? Tag { get; set; }
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public string? ReleaseRepoSlug { get; set; }
    public string? Token { get; set; }
    public string? RepoPath { get; set; }
    public string? Framework { get; set; }
    public string? Configuration { get; set; }
    public List<string> Rids { get; } = new();
    public bool ShowHelp { get; set; }

    public static ReleaseReviewerOptions Parse(string[] args) {
        var options = new ReleaseReviewerOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg is "-h" or "--help") {
                options.ShowHelp = true;
                return options;
            }
            switch (arg) {
                case "--tag":
                    options.Tag = ReadValue(args, ref i);
                    break;
                case "--title":
                    options.Title = ReadValue(args, ref i);
                    break;
                case "--notes":
                    options.Notes = ReadValue(args, ref i);
                    break;
                case "--repo-slug":
                    options.ReleaseRepoSlug = ReadValue(args, ref i);
                    break;
                case "--token":
                    options.Token = ReadValue(args, ref i);
                    break;
                case "--repo":
                    options.RepoPath = ReadValue(args, ref i);
                    break;
                case "--framework":
                    options.Framework = ReadValue(args, ref i);
                    break;
                case "--configuration":
                    options.Configuration = ReadValue(args, ref i);
                    break;
                case "--rids":
                    options.Rids.AddRange(ReadValue(args, ref i)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
            }
        }

        return options;
    }

    public static void ApplyEnvDefaults(ReleaseReviewerOptions options) {
        if (options is null) {
            return;
        }

        options.Tag ??= ReadEnv("INTELLIGENCEX_REVIEWER_TAG");
        options.Title ??= ReadEnv("INTELLIGENCEX_REVIEWER_TITLE");
        options.Notes ??= ReadEnv("INTELLIGENCEX_REVIEWER_NOTES");
        options.ReleaseRepoSlug ??= ReadEnv("INTELLIGENCEX_REVIEWER_REPO_SLUG");
        options.Token ??= ReadEnv("INTELLIGENCEX_REVIEWER_TOKEN");
        options.RepoPath ??= ReadEnv("INTELLIGENCEX_REVIEWER_REPO");
        options.Framework ??= ReadEnv("INTELLIGENCEX_REVIEWER_FRAMEWORK");
        options.Configuration ??= ReadEnv("INTELLIGENCEX_REVIEWER_CONFIGURATION");
        if (options.Rids.Count == 0) {
            var rids = ReadEnv("INTELLIGENCEX_REVIEWER_RIDS");
            if (!string.IsNullOrWhiteSpace(rids)) {
                options.Rids.AddRange(rids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
    }

    private static string? ReadEnv(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ReadValue(string[] args, ref int index) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"Missing value for {args[index]}.");
        }
        index++;
        return args[index];
    }
}
