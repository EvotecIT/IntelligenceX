namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisConfigReaderNormalizesDuplicationRuleIds() {
        var root = JsonLite.Parse("""
{
  "analysis": {
    "enabled": true,
    "gate": {
      "duplication": {
        "ruleIds": [" IXDUP001 ", "", "ixdup001", " IXDUP002 ", "   "]
      }
    }
  }
}
""")?.AsObject();
        AssertNotNull(root, "analysis config parse root");

        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root!, reviewObj: null, settings);

        var ruleIds = settings.Gate.Duplication.RuleIds;
        AssertNotNull(ruleIds, "analysis config duplication ruleIds set");
        AssertEqual(2, ruleIds!.Count, "analysis config duplication ruleIds normalized count");
        AssertEqual("IXDUP001", ruleIds[0], "analysis config duplication ruleIds first normalized");
        AssertEqual("IXDUP002", ruleIds[1], "analysis config duplication ruleIds second normalized");
    }

    private static void TestAnalysisConfigReaderKeepsDefaultDuplicationRuleIdsWhenConfiguredListEmpty() {
        var root = JsonLite.Parse("""
{
  "analysis": {
    "enabled": true,
    "gate": {
      "duplication": {
        "ruleIds": ["", "   "]
      }
    }
  }
}
""")?.AsObject();
        AssertNotNull(root, "analysis config empty-ruleIds parse root");

        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root!, reviewObj: null, settings);

        var ruleIds = settings.Gate.Duplication.RuleIds;
        AssertNotNull(ruleIds, "analysis config empty-ruleIds defaults present");
        AssertEqual(1, ruleIds!.Count, "analysis config empty-ruleIds count");
        AssertEqual("IXDUP001", ruleIds[0], "analysis config empty-ruleIds preserves default");
    }
}
#endif
