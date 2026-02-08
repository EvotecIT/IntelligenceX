namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisHotspotsRenderAndStateSnippet() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXHOT001.json"), """
{
  "id": "IXHOT001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXHOT001",
  "type": "security-hotspot",
  "title": "Potential hardcoded secret usage",
  "description": "Security-sensitive pattern that requires human review.",
  "category": "Security",
  "defaultSeverity": "info",
  "tags": ["security-hotspot"]
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IXHOT001"]
}
""");

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.StatePath = ".intelligencex/hotspots.json";

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/test.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-123")
            };

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "### Security Hotspots", "hotspots header");
            AssertContainsText(block, "Hotspots: 1", "hotspots count");
            AssertContainsText(block, "Missing state entries: 1", "hotspots missing state count");
            AssertContainsText(block, "IXHOT001:fp-", "hotspots suggested key uses fingerprint hash");
            AssertEqual(false, block.Contains("fp-123", StringComparison.Ordinal), "hotspots suggested key does not include raw fingerprint");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsRedactsAbsoluteStatePath() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-statepath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

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

            var absoluteStatePath = Path.Combine(Path.GetTempPath(), "ix-private-" + Guid.NewGuid().ToString("N"), "hotspots.json");

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.ShowStateSummary = true;
            settings.Analysis.Hotspots.StatePath = absoluteStatePath;

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/test.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-123")
            };

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "- State file: `hotspots.json`", "hotspots state path redacts absolute directory");

            var tmp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            AssertEqual(false, block.Contains(tmp, StringComparison.OrdinalIgnoreCase), "hotspots state path not absolute");
            AssertEqual(false, block.Contains(tmp.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase),
                "hotspots state path not absolute (slash)");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsReviewerStatePathIsWorkspaceBound() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-statepath-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var outsideRoot = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

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

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.ShowStateSummary = true;

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/test.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-123")
            };

            // Create a valid state file outside the workspace that would satisfy the key if it were loaded.
            var key = AnalysisHotspots.ComputeHotspotKey(findings[0]);
            var outsideStatePath = Path.Combine(outsideRoot, "hotspots.json");
            File.WriteAllText(outsideStatePath,
                "{\n" +
                "  \"schema\": \"intelligencex.hotspots.v1\",\n" +
                "  \"items\": [\n" +
                $"    {{ \"key\": \"{key}\", \"status\": \"safe\" }}\n" +
                "  ]\n" +
                "}\n");

            settings.Analysis.Hotspots.StatePath = outsideStatePath;
            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "ignored (outside workspace)", "hotspots reviewer ignores outside-workspace statePath");
            AssertContainsText(block, "Missing state entries: 1", "hotspots reviewer does not load outside-workspace state file");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
            if (Directory.Exists(outsideRoot)) {
                Directory.Delete(outsideRoot, true);
            }
        }
    }

    private static void TestAnalysisHotspotsMaxItemsSemantics() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-maxitems-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

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

            var findings = new List<AnalysisFinding>();
            for (var i = 0; i < 11; i++) {
                findings.Add(new AnalysisFinding($"src/test{i:D2}.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", $"fp-{i}"));
            }

            Environment.CurrentDirectory = temp;

            var baseSettings = new ReviewSettings();
            baseSettings.Analysis.Enabled = true;
            baseSettings.Analysis.Hotspots.Show = true;
            baseSettings.Analysis.Hotspots.ShowStateSummary = false;

            // MaxItems=0 hides items.
            baseSettings.Analysis.Hotspots.MaxItems = 0;
            var hidden = AnalysisHotspots.BuildBlock(baseSettings, findings);
            AssertContainsText(hidden, "Items: hidden (maxItems=0)", "hotspots maxItems 0 hides items");

            // Default MaxItems=10 limits to 10 items.
            baseSettings.Analysis.Hotspots.MaxItems = 10;
            var defaulted = AnalysisHotspots.BuildBlock(baseSettings, findings);
            AssertContainsText(defaulted, "- Showing first 10 of 11 hotspot(s).", "hotspots maxItems default 10 limits items");

            // MaxItems>0 limits to that number.
            baseSettings.Analysis.Hotspots.MaxItems = 2;
            var limited = AnalysisHotspots.BuildBlock(baseSettings, findings);
            AssertContainsText(limited, "- Showing first 2 of 11 hotspot(s).", "hotspots maxItems 2 limits items");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsSuppressedCountSemantics() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-suppressed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

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

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.StatePath = ".intelligencex/hotspots.json";
            settings.Analysis.Hotspots.ShowStateSummary = false;

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/visible.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-visible"),
                new AnalysisFinding("src/suppressed.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-suppressed")
            };

            // One visible hotspot (to-review) and one suppressed hotspot.
            var visibleKey = AnalysisHotspots.ComputeHotspotKey(findings[0]);
            var suppressedKey = AnalysisHotspots.ComputeHotspotKey(findings[1]);
            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath,
                "{\n" +
                "  \"schema\": \"intelligencex.hotspots.v1\",\n" +
                "  \"items\": [\n" +
                $"    {{ \"key\": \"{visibleKey}\", \"status\": \"to-review\" }},\n" +
                $"    {{ \"key\": \"{suppressedKey}\", \"status\": \"suppress\" }}\n" +
                "  ]\n" +
                "}\n");

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "- Hotspots: 1", "hotspots headline excludes suppressed");
            AssertContainsText(block, "(suppressed: 1)", "hotspots suppressed count is reported");
            AssertContainsText(block, visibleKey, "hotspots list includes visible key");
            AssertEqual(false, block.Contains(suppressedKey, StringComparison.OrdinalIgnoreCase),
                "hotspots list excludes suppressed key");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsKeyHashingUsesUtf8Bytes() {
        // Non-ASCII inputs should hash deterministically based on UTF-8 bytes.
        var finding = new AnalysisFinding(
            "src/naïve.cs",
            42,
            "Use café secrets",
            "warning",
            "IXHOT002",
            "IntelligenceX",
            null);

        var key = AnalysisHotspots.ComputeHotspotKey(finding);
        AssertEqual("IXHOT002:e4ab3f06e3e88089", key, "hotspots key hashing uses UTF-8 bytes (FNV-1a 64)");
    }

    private static void TestAnalysisHotspotsOutputEscapesMarkdownInjection() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

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

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.StatePath = ".intelligencex/hotspots.json";
            settings.Analysis.Hotspots.ShowStateSummary = false;

            var finding = new AnalysisFinding(
                "src/test.cs",
                10,
                "Hello\n- [x] injected",
                "warning",
                "IXHOT001",
                "IntelligenceX",
                "fp-inject");
            var findings = new List<AnalysisFinding> { finding };

            var key = AnalysisHotspots.ComputeHotspotKey(finding);
            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath,
                "{\n" +
                "  \"schema\": \"intelligencex.hotspots.v1\",\n" +
                "  \"items\": [\n" +
                $"    {{ \"key\": \"{key}\", \"status\": \"to-review\", \"note\": \"`oops`\\n**bold**\" }}\n" +
                "  ]\n" +
                "}\n");

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "`Hello - [x] injected`", "hotspots message is rendered as safe inline code");
            AssertContainsText(block, "Note: `'oops' **bold**`", "hotspots note is rendered as safe inline code");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisLoaderIncludesHotspotsBelowMinSeverity() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-minseverity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
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
      "message": "This is a hotspot that should be shown even when minSeverity=warning.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-1"
    }
  ]
}
""");

            // GitHub Actions sets GITHUB_WORKSPACE; the loader resolves relative inputs against it when present.
            // Point it at this temp workspace so the test behaves the same locally and in CI.
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.Inputs = new[] { "artifacts/intelligencex.findings.json" };
            settings.Analysis.Results.MinSeverity = "warning";

            var load = AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
            AssertEqual(1, load.Findings.Count, "hotspot finding not filtered by minSeverity");
            AssertEqual("IXHOT001", load.Findings[0].RuleId, "hotspot rule id");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

}
#endif
