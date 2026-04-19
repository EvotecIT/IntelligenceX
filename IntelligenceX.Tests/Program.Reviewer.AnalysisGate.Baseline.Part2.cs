namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateNewIssuesOnlySuppressesLegacyBaselineKeyDotRelativePrefix() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-legacy-key-dotrel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "key": "IX001|.//SRC\\TEST.CS|10|IntelligenceX|msg:Broken."
    }
  ]
}
""");
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze gate suppresses dot-relative legacy baseline key with normalized path");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeGateWriteBaselineCreatesContractSchema() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");
            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath,
                "--write-baseline", baselinePath
            });
            AssertEqual(0, exit, "analyze gate write baseline exit");
            AssertContainsText(output, "baseline updated", "analyze gate write baseline output");
            AssertEqual(true, File.Exists(baselinePath), "baseline file exists");

            var root = JsonLite.Parse(File.ReadAllText(baselinePath))?.AsObject();
            AssertNotNull(root, "baseline root json");
            AssertEqual("intelligencex.analysis-baseline.v1", root!.GetString("schema") ?? string.Empty, "baseline schema");
            var items = root.GetArray("items");
            AssertNotNull(items, "baseline items");
            AssertEqual(true, items!.Count >= 1, "baseline items count");
            var first = items[0]?.AsObject();
            AssertNotNull(first, "baseline first item");
            AssertEqual(true, !string.IsNullOrWhiteSpace(first!.GetString("key")), "baseline item key");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestReviewerSchemaIncludesAnalysisGateBaselineProperties() {
        var schemaPath = Path.Combine(ResolveWorkspaceRoot(), "Schemas", "reviewer.schema.json");
        AssertEqual(true, File.Exists(schemaPath), "reviewer schema exists");
        var root = JsonLite.Parse(File.ReadAllText(schemaPath))?.AsObject();
        AssertNotNull(root, "reviewer schema root");

        var analysis = root!.GetObject("properties")?.GetObject("analysis");
        AssertNotNull(analysis, "reviewer schema analysis node");
        var gate = analysis!.GetObject("properties")?.GetObject("gate");
        AssertNotNull(gate, "reviewer schema analysis.gate node");
        var gateProps = gate!.GetObject("properties");
        AssertNotNull(gateProps, "reviewer schema analysis.gate.properties");

        var newIssuesOnly = gateProps!.GetObject("newIssuesOnly");
        AssertNotNull(newIssuesOnly, "reviewer schema newIssuesOnly property");
        AssertEqual("boolean", newIssuesOnly!.GetString("type") ?? string.Empty, "reviewer schema newIssuesOnly type");

        var baselinePath = gateProps.GetObject("baselinePath");
        AssertNotNull(baselinePath, "reviewer schema baselinePath property");
        AssertEqual("string", baselinePath!.GetString("type") ?? string.Empty, "reviewer schema baselinePath type");

        var ruleIds = gateProps.GetObject("ruleIds");
        AssertNotNull(ruleIds, "reviewer schema gate ruleIds property");
        AssertEqual("array", ruleIds!.GetString("type") ?? string.Empty, "reviewer schema gate ruleIds type");
        var ruleItems = ruleIds.GetObject("items");
        AssertNotNull(ruleItems, "reviewer schema gate ruleIds items");
        AssertEqual("string", ruleItems!.GetString("type") ?? string.Empty, "reviewer schema gate ruleIds item type");

        var duplication = gateProps.GetObject("duplication");
        AssertNotNull(duplication, "reviewer schema duplication property");
        var duplicationProps = duplication!.GetObject("properties");
        AssertNotNull(duplicationProps, "reviewer schema duplication properties");

        var enabled = duplicationProps!.GetObject("enabled");
        AssertNotNull(enabled, "reviewer schema duplication enabled property");
        AssertEqual("boolean", enabled!.GetString("type") ?? string.Empty, "reviewer schema duplication enabled type");

        var maxFilePercent = duplicationProps.GetObject("maxFilePercent");
        AssertNotNull(maxFilePercent, "reviewer schema duplication maxFilePercent property");
        AssertEqual("number", maxFilePercent!.GetString("type") ?? string.Empty, "reviewer schema duplication maxFilePercent type");

        var maxFilePercentIncrease = duplicationProps.GetObject("maxFilePercentIncrease");
        AssertNotNull(maxFilePercentIncrease, "reviewer schema duplication maxFilePercentIncrease property");
        AssertEqual("number", maxFilePercentIncrease!.GetString("type") ?? string.Empty,
            "reviewer schema duplication maxFilePercentIncrease type");

        var scope = duplicationProps.GetObject("scope");
        AssertNotNull(scope, "reviewer schema duplication scope property");
        AssertEqual("string", scope!.GetString("type") ?? string.Empty, "reviewer schema duplication scope type");
    }

    private static void TestReviewerSchemaIncludesOpenAiCompatibleProviderAndConfig() {
        var schemaPath = Path.Combine(ResolveWorkspaceRoot(), "Schemas", "reviewer.schema.json");
        AssertEqual(true, File.Exists(schemaPath), "reviewer schema exists");
        var root = JsonLite.Parse(File.ReadAllText(schemaPath))?.AsObject();
        AssertNotNull(root, "reviewer schema root");

        var review = root!.GetObject("properties")?.GetObject("review");
        AssertNotNull(review, "reviewer schema review node");
        var reviewProps = review!.GetObject("properties");
        AssertNotNull(reviewProps, "reviewer schema review.properties");

        var narrativeMode = reviewProps!.GetObject("narrativeMode");
        AssertNotNull(narrativeMode, "reviewer schema review.narrativeMode property");
        var narrativeModeEnum = narrativeMode!.GetArray("enum");
        AssertNotNull(narrativeModeEnum, "reviewer schema review.narrativeMode enum");
        var hasStructured = false;
        var hasFreedom = false;
        foreach (var item in narrativeModeEnum!) {
            var text = item?.AsString();
            if (string.Equals(text, "structured", StringComparison.OrdinalIgnoreCase)) {
                hasStructured = true;
            }
            if (string.Equals(text, "freedom", StringComparison.OrdinalIgnoreCase)) {
                hasFreedom = true;
            }
        }
        AssertEqual(true, hasStructured, "reviewer schema narrativeMode includes structured");
        AssertEqual(true, hasFreedom, "reviewer schema narrativeMode includes freedom");

        var provider = reviewProps.GetObject("provider");
        AssertNotNull(provider, "reviewer schema review.provider property");
        var providerEnum = provider!.GetArray("enum");
        AssertNotNull(providerEnum, "reviewer schema review.provider enum");

        var providerValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in providerEnum!) {
            var value = item?.AsString();
            if (!string.IsNullOrWhiteSpace(value)) {
                providerValues.Add(value);
            }
        }

        AssertEqual(true, providerValues.Contains("openai-compatible"), "reviewer schema provider includes openai-compatible");
        AssertEqual(true, providerValues.Contains("ollama"), "reviewer schema provider includes ollama");
        AssertEqual(true, providerValues.Contains("claude"), "reviewer schema provider includes claude");
        AssertEqual(true, providerValues.Contains("anthropic"), "reviewer schema provider includes anthropic");

        var openAiCompatible = reviewProps.GetObject("openaiCompatible");
        AssertNotNull(openAiCompatible, "reviewer schema openaiCompatible property");
        AssertEqual("object", openAiCompatible!.GetString("type") ?? string.Empty, "reviewer schema openaiCompatible type");
        var openAiCompatibleProps = openAiCompatible.GetObject("properties");
        AssertNotNull(openAiCompatibleProps, "reviewer schema openaiCompatible.properties");

        var baseUrl = openAiCompatibleProps!.GetObject("baseUrl");
        AssertNotNull(baseUrl, "reviewer schema openaiCompatible baseUrl property");
        AssertEqual("string", baseUrl!.GetString("type") ?? string.Empty, "reviewer schema openaiCompatible baseUrl type");

        var apiKeyEnv = openAiCompatibleProps.GetObject("apiKeyEnv");
        AssertNotNull(apiKeyEnv, "reviewer schema openaiCompatible apiKeyEnv property");
        AssertEqual("string", apiKeyEnv!.GetString("type") ?? string.Empty, "reviewer schema openaiCompatible apiKeyEnv type");

        var allowInsecureHttp = openAiCompatibleProps.GetObject("allowInsecureHttp");
        AssertNotNull(allowInsecureHttp, "reviewer schema openaiCompatible allowInsecureHttp property");
        AssertEqual("boolean", allowInsecureHttp!.GetString("type") ?? string.Empty,
            "reviewer schema openaiCompatible allowInsecureHttp type");

        var timeoutSeconds = openAiCompatibleProps.GetObject("timeoutSeconds");
        AssertNotNull(timeoutSeconds, "reviewer schema openaiCompatible timeoutSeconds property");
        AssertEqual("integer", timeoutSeconds!.GetString("type") ?? string.Empty,
            "reviewer schema openaiCompatible timeoutSeconds type");

        var anthropic = reviewProps.GetObject("anthropic");
        AssertNotNull(anthropic, "reviewer schema anthropic property");
        AssertEqual("object", anthropic!.GetString("type") ?? string.Empty, "reviewer schema anthropic type");
        var anthropicProps = anthropic.GetObject("properties");
        AssertNotNull(anthropicProps, "reviewer schema anthropic.properties");
        AssertNotNull(anthropicProps!.GetObject("baseUrl"), "reviewer schema anthropic baseUrl property");
        AssertNotNull(anthropicProps.GetObject("apiKeyEnv"), "reviewer schema anthropic apiKeyEnv property");
        AssertNotNull(anthropicProps.GetObject("timeoutSeconds"), "reviewer schema anthropic timeoutSeconds property");
        AssertNotNull(anthropicProps.GetObject("maxTokens"), "reviewer schema anthropic maxTokens property");

        var copilot = root.GetObject("properties")?.GetObject("copilot");
        AssertNotNull(copilot, "reviewer schema copilot node");
        var copilotProps = copilot!.GetObject("properties");
        AssertNotNull(copilotProps, "reviewer schema copilot.properties");
        var copilotModel = copilotProps!.GetObject("model");
        AssertNotNull(copilotModel, "reviewer schema copilot.model property");
        AssertEqual("string", copilotModel!.GetString("type") ?? string.Empty, "reviewer schema copilot.model type");
    }
}
#endif
