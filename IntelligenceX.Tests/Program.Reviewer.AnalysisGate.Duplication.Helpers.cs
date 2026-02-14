namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void WriteEmptyFindings(string path) {
        File.WriteAllText(path, """
{
  "schema": "intelligencex.findings.v1",
  "items": []
}
""");
    }

    private static void WriteDuplicationMetrics(string metricsPath, double duplicatedPercent, int duplicatedLines,
        int significantLines, int firstLine, string fingerprint, string findingPath = "src/test.cs",
        double configuredMaxPercent = 25, double? fileConfiguredMaxPercent = null, int windowLines = 8) {
        File.WriteAllText(metricsPath, $$"""
{
  "schema": "intelligencex.duplication.v1",
  "rules": [
    {
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "windowLines": {{windowLines}},
      "configuredMaxPercent": {{configuredMaxPercent}},
      "totalSignificantLines": {{significantLines}},
      "duplicatedSignificantLines": {{duplicatedLines}},
      "overallDuplicatedPercent": {{duplicatedPercent}},
      "files": [
        {
          "path": "{{findingPath}}",
          "configuredMaxPercent": {{(fileConfiguredMaxPercent.HasValue ? fileConfiguredMaxPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}},
          "firstDuplicatedLine": {{firstLine}},
          "significantLines": {{significantLines}},
          "duplicatedLines": {{duplicatedLines}},
          "duplicatedPercent": {{duplicatedPercent}},
          "fingerprint": "{{fingerprint}}"
        }
      ]
    }
  ]
}
""");
    }
}
#endif
