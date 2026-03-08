using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards against silently growing new oversized source files without an explicit budget decision.
/// Existing large files must stay within their recorded ceiling until they are refactored.
/// </summary>
public sealed class OversizedFileBudgetTests {
    private const int DefaultMaxLines = 700;
    private static readonly string[] SourceExtensions = [".cs", ".js", ".css"];

    /// <summary>
    /// Ensures oversized tracked source files either stay within an explicit budget
    /// or fail fast so growth is an intentional review decision.
    /// </summary>
    [Fact]
    public void OversizedSourceFiles_MustBeBudgeted_AndStayWithinBudget() {
        var repoRoot = FindRepoRoot();
        var baselinePath = Path.Combine(repoRoot, "IntelligenceX.Chat", "IntelligenceX.Chat.App.Tests", "OversizedFileBudgetBaseline.txt");
        var baseline = LoadBaseline(baselinePath);

        var oversizedFiles = EnumerateTrackedSourceFiles(repoRoot)
            .Select(path => new OversizedFile(Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), CountLines(path)))
            .Where(file => file.LineCount > DefaultMaxLines)
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var failures = new List<string>();
        foreach (var file in oversizedFiles) {
            var matchedBudget = baseline.FirstOrDefault(entry => GlobMatches(entry.Pattern, file.RelativePath));
            if (matchedBudget == null) {
                failures.Add($"Unbudgeted oversized file: {file.RelativePath} ({file.LineCount} lines)");
                continue;
            }

            if (file.LineCount > matchedBudget.MaxLines) {
                failures.Add($"Oversized file grew beyond budget: {file.RelativePath} is {file.LineCount} lines > budget {matchedBudget.MaxLines} (pattern {matchedBudget.Pattern})");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string FindRepoRoot() {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null) {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from test output.");
    }

    private static IEnumerable<string> EnumerateTrackedSourceFiles(string repoRoot) {
        var startInfo = new ProcessStartInfo("git") {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("ls-files");
        foreach (var extension in SourceExtensions) {
            startInfo.ArgumentList.Add($"*{extension}");
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git ls-files.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) {
            throw new InvalidOperationException($"git ls-files failed with exit code {process.ExitCode}: {error}");
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !IsUnderExcludedDirectory(path))
            .Select(path => Path.GetFullPath(Path.Combine(repoRoot, path)));
    }

    private static int CountLines(string path) {
        return File.ReadLines(path).Count();
    }

    private static List<BudgetEntry> LoadBaseline(string baselinePath) {
        return File.ReadAllLines(baselinePath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line => line.Split('|'))
            .Select(parts => new BudgetEntry(parts[0].Trim(), int.Parse(parts[1].Trim())))
            .ToList();
    }

    private static bool GlobMatches(string pattern, string relativePath) {
        var regexPattern = "^" + Regex.Escape(pattern.Replace('/', Path.DirectorySeparatorChar))
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return Regex.IsMatch(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsUnderExcludedDirectory(string path) {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        return segments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals(".worktrees", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals(".cache", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("packages", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("_site", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("vendor", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record BudgetEntry(string Pattern, int MaxLines);
    private sealed record OversizedFile(string RelativePath, int LineCount);
}
