using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class RunProfileFrameworkValidationTests {
    [Fact]
    public void ProjectRunProfiles_UseDeclaredProjectFrameworks() {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot, "Build", "run.profiles.json");
        Assert.True(File.Exists(configPath), "Run profiles config was not found: " + configPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = doc.RootElement;
        var projectRoot = ResolveProjectRoot(configPath, root.TryGetProperty("ProjectRoot", out var projectRootElement)
            ? projectRootElement.GetString()
            : null);

        var profiles = root.GetProperty("Profiles").EnumerateArray().ToArray();
        Assert.NotEmpty(profiles);

        var failures = new List<string>();
        foreach (var profile in profiles) {
            if (!profile.TryGetProperty("Kind", out var kindElement) ||
                !string.Equals(kindElement.GetString(), "Project", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var name = profile.GetProperty("Name").GetString() ?? "<unnamed>";
            var framework = profile.TryGetProperty("Framework", out var frameworkElement)
                ? frameworkElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(framework) ||
                string.Equals(framework, "project-defined", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var projectPathValue = profile.GetProperty("ProjectPath").GetString();
            Assert.False(string.IsNullOrWhiteSpace(projectPathValue), $"Run profile '{name}' is missing ProjectPath.");

            var projectPath = Path.GetFullPath(Path.Combine(projectRoot, projectPathValue!));
            Assert.True(File.Exists(projectPath), $"Project file for run profile '{name}' was not found: {projectPath}");

            var declaredFrameworks = ReadTargetFrameworks(projectPath);
            if (!declaredFrameworks.Contains(framework!, StringComparer.OrdinalIgnoreCase)) {
                failures.Add(
                    $"Run profile '{name}' uses framework '{framework}', but {Path.GetRelativePath(repoRoot, projectPath)} declares: {string.Join(", ", declaredFrameworks)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string[] ReadTargetFrameworks(string projectPath) {
        var doc = XDocument.Load(projectPath);
        return doc
            .Descendants()
            .Where(element =>
                string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
            .SelectMany(element => (element.Value ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveProjectRoot(string configPath, string? projectRootValue) {
        var configDirectory = Path.GetDirectoryName(configPath) ?? throw new InvalidOperationException("Could not resolve config directory.");
        if (string.IsNullOrWhiteSpace(projectRootValue)) {
            return configDirectory;
        }

        return Path.GetFullPath(Path.Combine(configDirectory, projectRootValue));
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "IntelligenceX.sln"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
