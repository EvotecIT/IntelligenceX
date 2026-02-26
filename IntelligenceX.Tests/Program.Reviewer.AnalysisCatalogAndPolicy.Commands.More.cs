namespace IntelligenceX.Tests;

internal static partial class Program {
    #if INTELLIGENCEX_REVIEWER
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

    private static void TestAnalyzeListRulesSecurityTierCounts() {
        var workspace = ResolveWorkspaceRoot();
        var (exit50, output50) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-security-50",
            "--format",
            "json"
        });
        var (exit100, output100) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-security-100",
            "--format",
            "json"
        });
        var (exit500, output500) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-security-500",
            "--format",
            "json"
        });

        AssertEqual(0, exit50, "analyze list-rules all-security-50 exit");
        AssertEqual(0, exit100, "analyze list-rules all-security-100 exit");
        AssertEqual(0, exit500, "analyze list-rules all-security-500 exit");

        var count50 = ParseListedRuleCount(output50, "all-security-50");
        var count100 = ParseListedRuleCount(output100, "all-security-100");
        var count500 = ParseListedRuleCount(output500, "all-security-500");

        AssertEqual(true, count50 > 0, "analyze list-rules all-security-50 minimum");
        AssertEqual(true, count100 >= count50, "analyze list-rules all-security-100 expands all-security-50");
        AssertEqual(true, count500 >= count100, "analyze list-rules all-security-500 expands all-security-100");
        AssertEqual(true, count500 <= 500, "analyze list-rules all-security-500 max bound");
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
    #endif
}
