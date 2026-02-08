namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeHotspotsSyncStateWritesStateFile() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-hotspots-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXHOT001.json"), """
{
  "id": "IXHOT001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXHOT001",
  "type": "security-hotspot",
  "title": "Security hotspot",
  "description": "Requires review.",
  "category": "Security",
  "defaultSeverity": "info"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IXHOT001"]
}
""");
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "info",
      "message": "Hotspot finding.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-xyz"
    }
  ]
}
""");

            // Ensure all relative path resolution happens inside this test workspace even in CI.
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "hotspots": {
      "show": true,
      "statePath": ".intelligencex/hotspots.json"
    },
    "results": {
      "inputs": ["artifacts/intelligencex.findings.json"],
      "minSeverity": "warning",
      "showPolicy": false,
      "summary": false
    }
  }
}
""");

            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "hotspots",
                "sync-state",
                "--workspace",
                temp,
                "--config",
                configPath
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze hotspots sync-state exit");

            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            AssertEqual(true, File.Exists(statePath), "hotspots state file created");
            var text = File.ReadAllText(statePath);
            AssertContainsText(text, "\"schema\": \"intelligencex.hotspots.v1\"", "hotspots state schema");
            AssertContainsText(text, "\"key\": \"IXHOT001:fp-\"", "hotspots state key is hashed");
            AssertEqual(false, text.Contains("fp-xyz", StringComparison.Ordinal), "hotspots state does not include raw fingerprint");
            AssertContainsText(text, "\"status\": \"to-review\"", "hotspots state default status");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeHotspotsHelpHasNoSideEffects() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-hotspots-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            // Use a missing config path; help must short-circuit before config loading / filesystem writes.
            var configPath = Path.Combine(temp, "missing-config.json");
            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            var stateDir = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrWhiteSpace(stateDir) && Directory.Exists(stateDir)) {
                Directory.Delete(stateDir, true);
            }

            var (syncExit, syncOutput) = RunAnalyzeAndCaptureOutput(new[] {
                "hotspots",
                "sync-state",
                "--help",
                "--workspace",
                temp,
                "--config",
                configPath
            });
            AssertEqual(0, syncExit, "hotspots sync-state --help exit");
            AssertContainsText(syncOutput, "intelligencex analyze hotspots sync-state", "hotspots sync-state --help usage");
            AssertEqual(false, syncOutput.Contains("intelligencex analyze hotspots set", StringComparison.Ordinal),
                "hotspots sync-state --help should be subcommand-specific");
            AssertEqual(false, Directory.Exists(Path.Combine(temp, ".intelligencex")), "hotspots sync-state --help should not create state dir");
            AssertEqual(false, File.Exists(statePath), "hotspots sync-state --help should not write state");

            var (setExit, setOutput) = RunAnalyzeAndCaptureOutput(new[] {
                "hotspots",
                "set",
                "--help",
                "--workspace",
                temp,
                "--config",
                configPath
            });
            AssertEqual(0, setExit, "hotspots set --help exit");
            AssertContainsText(setOutput, "intelligencex analyze hotspots set", "hotspots set --help usage");
            AssertEqual(false, setOutput.Contains("intelligencex analyze hotspots sync-state", StringComparison.Ordinal),
                "hotspots set --help should be subcommand-specific");
            AssertEqual(false, Directory.Exists(Path.Combine(temp, ".intelligencex")), "hotspots set --help should not create state dir");
            AssertEqual(false, File.Exists(statePath), "hotspots set --help should not write state");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeHotspotsStatePathIsWorkspaceBound() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-hotspots-statepath-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var outsideRoot = Path.Combine(Path.GetTempPath(), "ix-outside-" + Guid.NewGuid().ToString("N"));
        var outsideStatePath = Path.Combine(outsideRoot, "hotspots.json");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXHOT001.json"), """
{
  "id": "IXHOT001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXHOT001",
  "type": "security-hotspot",
  "title": "Security hotspot",
  "description": "Requires review.",
  "category": "Security",
  "defaultSeverity": "info"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IXHOT001"]
}
""");
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "info",
      "message": "Hotspot finding.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-xyz"
    }
  ]
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "hotspots": {
      "show": true,
      "statePath": ".intelligencex/hotspots.json"
    },
    "results": {
      "inputs": ["artifacts/intelligencex.findings.json"],
      "minSeverity": "warning",
      "showPolicy": false,
      "summary": false
    }
  }
}
""");

            var (exitBlocked, outputBlocked) = RunAnalyzeAndCaptureOutput(new[] {
                "hotspots",
                "sync-state",
                "--workspace",
                temp,
                "--config",
                configPath,
                "--state",
                outsideStatePath,
                "--dry-run"
            });
            AssertEqual(1, exitBlocked, "hotspots sync-state outside state path exit");
            AssertContainsText(outputBlocked, "State path must be within workspace", "hotspots sync-state state path restriction message");
            AssertEqual(false, File.Exists(outsideStatePath), "hotspots sync-state outside state path should not write state");

            var (exitAllowed, _) = RunAnalyzeAndCaptureOutput(new[] {
                "hotspots",
                "sync-state",
                "--workspace",
                temp,
                "--config",
                configPath,
                "--state",
                outsideStatePath,
                "--allow-outside-workspace",
                "--dry-run"
            });
            AssertEqual(0, exitAllowed, "hotspots sync-state allow outside state path exit");
            AssertEqual(false, File.Exists(outsideStatePath), "hotspots sync-state allow outside dry-run should not write state");

            var (setBlocked, setBlockedOut) = RunAnalyzeAndCaptureOutput(new[] {
                "hotspots",
                "set",
                "--workspace",
                temp,
                "--config",
                configPath,
                "--state",
                outsideStatePath,
                "--key",
                "IXHOT001:fp-00000000000000000000000000000000",
                "--status",
                "safe"
            });
            AssertEqual(1, setBlocked, "hotspots set outside state path exit");
            AssertContainsText(setBlockedOut, "State path must be within workspace", "hotspots set state path restriction message");
            AssertEqual(false, File.Exists(outsideStatePath), "hotspots set outside state path should not write state");

            var (setAllowed, _) = RunAnalyzeAndCaptureOutput(new[] {
                "hotspots",
                "set",
                "--workspace",
                temp,
                "--config",
                configPath,
                "--state",
                outsideStatePath,
                "--allow-outside-workspace",
                "--key",
                "IXHOT001:fp-00000000000000000000000000000000",
                "--status",
                "safe"
            });
            AssertEqual(0, setAllowed, "hotspots set allow outside state path exit");
            AssertEqual(true, File.Exists(outsideStatePath), "hotspots set allow outside should write state");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
            // Clean up any files written outside the workspace when allow-outside-workspace is set.
            try {
                if (Directory.Exists(outsideRoot)) {
                    Directory.Delete(outsideRoot, true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestAnalyzeValidateCatalogCommand() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-validate-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "csharp",
  "tool": "roslyn",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "pack-a.json"), """
{
  "id": "pack-a",
  "label": "Pack A",
  "rules": ["IX001"]
}
""");

            var validExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "validate-catalog",
                "--workspace",
                temp
            }).GetAwaiter().GetResult();
            AssertEqual(0, validExit, "analyze validate-catalog valid exit");

            File.WriteAllText(Path.Combine(packsDir, "pack-a.json"), """
{
  "id": "pack-a",
  "label": "Pack A",
  "rules": ["IX404"]
}
""");

            var invalidExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "validate-catalog",
                "--workspace",
                temp
            }).GetAwaiter().GetResult();
            AssertEqual(1, invalidExit, "analyze validate-catalog invalid exit");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeListRulesMarkdownFormat() {
        var workspace = ResolveWorkspaceRoot();
        var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--format",
            "markdown"
        });
        AssertEqual(0, exitCode, "analyze list-rules markdown exit");
        AssertContainsText(output, "| ID | Language | Type | Tool | Tool Rule ID | Default Severity | Category | Title | Docs |",
            "analyze list-rules markdown header");
        AssertContainsText(output, "CA2000", "analyze list-rules markdown includes CA2000");
        AssertContainsText(output, "PSAvoidUsingWriteHost", "analyze list-rules markdown includes powershell rule");
    }

    private static void TestAnalyzeListRulesJsonWithPackFilter() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-list-rules-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX002.json"), """
{
  "id": "IX002",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule two",
  "description": "Rule two"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX003.json"), """
{
  "id": "IX003",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule three",
  "description": "Rule three"
}
""");

            File.WriteAllText(Path.Combine(packsDir, "core.json"), """
{
  "id": "core",
  "label": "Core",
  "rules": ["IX001"]
}
""");
            File.WriteAllText(Path.Combine(packsDir, "standard.json"), """
{
  "id": "standard",
  "label": "Standard",
  "includes": ["core"],
  "rules": ["IX002"]
}
""");
            File.WriteAllText(Path.Combine(packsDir, "strict.json"), """
{
  "id": "strict",
  "label": "Strict",
  "includes": ["standard"],
  "rules": ["IX003"]
}
""");

            var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
                "list-rules",
                "--workspace",
                temp,
                "--pack",
                "strict",
                "--format",
                "json"
            });

            AssertEqual(0, exitCode, "analyze list-rules json exit");
            var parsed = JsonLite.Parse(output.Trim())?.AsArray();
            AssertNotNull(parsed, "analyze list-rules json payload");
            var ids = new List<string>();
            foreach (var item in parsed!) {
                var obj = item.AsObject();
                if (obj is null) {
                    continue;
                }
                var id = obj.GetString("id");
                if (!string.IsNullOrWhiteSpace(id)) {
                    ids.Add(id!);
                }
            }
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            AssertSequenceEqual(new[] { "IX001", "IX002", "IX003" }, ids, "analyze list-rules json pack includes");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeListRulesTierCounts() {
        var workspace = ResolveWorkspaceRoot();
        var (exit50, output50) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-50",
            "--format",
            "json"
        });
        var (exit100, output100) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-100",
            "--format",
            "json"
        });
        var (exit500, output500) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-500",
            "--format",
            "json"
        });

        AssertEqual(0, exit50, "analyze list-rules all-50 exit");
        AssertEqual(0, exit100, "analyze list-rules all-100 exit");
        AssertEqual(0, exit500, "analyze list-rules all-500 exit");

        var count50 = ParseListedRuleCount(output50, "all-50");
        var count100 = ParseListedRuleCount(output100, "all-100");
        var count500 = ParseListedRuleCount(output500, "all-500");

        AssertEqual(true, count50 >= 50, "analyze list-rules all-50 minimum");
        AssertEqual(true, count100 >= 100, "analyze list-rules all-100 minimum");
        AssertEqual(true, count100 >= count50, "analyze list-rules all-100 expands all-50");
        AssertEqual(true, count500 >= count100, "analyze list-rules all-500 expands all-100");
        AssertEqual(true, count500 <= 500, "analyze list-rules all-500 max bound");
    }

    private static void TestAnalyzeListRulesInvalidFormat() {
        var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            ResolveWorkspaceRoot(),
            "--format",
            "yaml"
        });
        AssertEqual(1, exitCode, "analyze list-rules invalid format exit");
        AssertContainsText(output, "Unsupported format 'yaml'", "analyze list-rules invalid format message");
    }

    private static void TestAnalyzeListRulesHelp() {
        var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--help"
        });
        AssertEqual(0, exitCode, "analyze list-rules help exit");
        AssertContainsText(output, "intelligencex analyze list-rules", "analyze list-rules help usage");
    }

    private static void TestAnalyzeListRulesJsonWarningsToStderr() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-list-rules-warn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "strict.json"), """
{
  "id": "strict",
  "label": "Strict",
  "includes": ["missing-pack"],
  "rules": ["IX001"]
}
""");

            var (exitCode, stdout, stderr) = RunAnalyzeAndCaptureStreams(new[] {
                "list-rules",
                "--workspace",
                temp,
                "--pack",
                "strict",
                "--format",
                "json"
            });
            AssertEqual(0, exitCode, "analyze list-rules json warnings exit");
            var parsed = JsonLite.Parse(stdout.Trim())?.AsArray();
            AssertNotNull(parsed, "analyze list-rules json warnings payload");
            AssertContainsText(stderr, "Warning: Included pack not found: missing-pack", "analyze list-rules json warnings stderr");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeListRulesJsonEmptyOutputsArray() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-list-rules-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            var (exitCode, stdout, stderr) = RunAnalyzeAndCaptureStreams(new[] {
                "list-rules",
                "--workspace",
                temp,
                "--format",
                "json"
            });
            AssertEqual(0, exitCode, "analyze list-rules empty json exit");
            AssertEqual("[]", stdout.Trim(), "analyze list-rules empty json payload");
            AssertEqual(string.Empty, stderr.Trim(), "analyze list-rules empty json stderr");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static int ParseListedRuleCount(string output, string scope) {
        var parsed = JsonLite.Parse((output ?? string.Empty).Trim())?.AsArray();
        AssertNotNull(parsed, $"analyze list-rules {scope} json payload");
        var count = 0;
        foreach (var item in parsed!) {
            if (item.AsObject() is not null) {
                count++;
            }
        }
        return count;
    }

    private static void TestAnalysisCatalogRuleDocsPath() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(Path.Combine(temp, "Docs", "reviewer"));

            var docsPath = "Docs/reviewer/static-analysis.md";
            File.WriteAllText(Path.Combine(temp, docsPath), "# docs");
            File.WriteAllText(Path.Combine(rulesDir, "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "docs": "Docs/reviewer/static-analysis.md"
}
""");

            var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(temp);
            AssertEqual(true, catalog.Rules.TryGetValue("IXLOC001", out var rule), "analysis docs rule exists");
            AssertEqual(docsPath, rule!.Docs, "analysis docs path stored");
            var resolvedDocsPath = Path.Combine(temp, rule.Docs!.Replace('/', Path.DirectorySeparatorChar));
            AssertEqual(true, File.Exists(resolvedDocsPath), "analysis docs path resolves from workspace");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunAnalyzeAndCaptureStreams(string[] args) {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(args).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString(), errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static (int ExitCode, string Output) RunAnalyzeAndCaptureOutput(string[] args) {
        var (exitCode, stdout, stderr) = RunAnalyzeAndCaptureStreams(args);
        return (exitCode, stdout + stderr);
    }

    private static string ResolveWorkspaceRoot() {
        var current = Environment.CurrentDirectory;
        for (var i = 0; i < 12; i++) {
            var rulesDir = Path.Combine(current, "Analysis", "Catalog", "rules");
            var packsDir = Path.Combine(current, "Analysis", "Packs");
            if (Directory.Exists(rulesDir) && Directory.Exists(packsDir)) {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent is null) {
                break;
            }
            current = parent.FullName;
        }
        return Environment.CurrentDirectory;
    }
}
#endif
