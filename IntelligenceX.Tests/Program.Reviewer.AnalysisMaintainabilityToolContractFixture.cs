namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void SetupToolContractAnalysisWorkspace(string workspacePath) {
        Directory.CreateDirectory(Path.Combine(workspacePath, ".intelligencex"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "Analysis", "Catalog", "rules", "internal"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "Analysis", "Packs"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample"));

        File.WriteAllText(Path.Combine(workspacePath, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
""");

        WriteToolContractRule(
            workspacePath,
            ruleId: "IXTOOL001",
            title: "Write-capable tools should use canonical schema helpers",
            description: "Flags write-capable ToolDefinition schemas that do not use helper defaults.");

        WriteToolContractRule(
            workspacePath,
            ruleId: "IXTOOL002",
            title: "AD required-domain tools should use canonical request helpers",
            description: "Flags AD tools with required domain_name that do not use canonical helper paths.");

        WriteToolContractRule(
            workspacePath,
            ruleId: "IXTOOL003",
            title: "Tools should use canonical max_results metadata helper",
            description: "Flags tool response metadata that adds max_results directly instead of AddMaxResultsMeta(...).");

        File.WriteAllText(Path.Combine(workspacePath, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXTOOL001", "IXTOOL002", "IXTOOL003"]
}
""");
    }

    private static void WriteToolContractRule(string workspacePath, string ruleId, string title, string description) {
        File.WriteAllText(Path.Combine(workspacePath, "Analysis", "Catalog", "rules", "internal", $"{ruleId}.json"), $$"""
{
  "id": "{{ruleId}}",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "{{ruleId}}",
  "title": "{{title}}",
  "description": "{{description}}",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["include-ext:cs"]
}
""");
    }
}
#endif
