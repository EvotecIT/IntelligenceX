namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisConfigReaderNormalizesGateRuleIds() {
        var root = JsonLite.Parse("""
{
  "analysis": {
    "enabled": true,
    "gate": {
      "ruleIds": [" IXTOOL001 ", "", "ixtool001", " IXABC002 ", "   "]
    }
  }
}
""")?.AsObject();
        AssertNotNull(root, "analysis config gate ruleIds parse root");

        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root!, reviewObj: null, settings);

        var ruleIds = settings.Gate.RuleIds;
        AssertNotNull(ruleIds, "analysis config gate ruleIds set");
        AssertEqual(2, ruleIds!.Count, "analysis config gate ruleIds normalized count");
        AssertEqual("IXTOOL001", ruleIds[0], "analysis config gate ruleIds first normalized");
        AssertEqual("IXABC002", ruleIds[1], "analysis config gate ruleIds second normalized");
    }

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

    private static void TestAnalysisConfigReaderReadsRunStrict() {
        var root = JsonLite.Parse("""
{
  "analysis": {
    "enabled": true,
    "run": {
      "strict": true
    }
  }
}
""")?.AsObject();
        AssertNotNull(root, "analysis config run.strict parse root");

        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root!, reviewObj: null, settings);

        AssertEqual(true, settings.Run.Strict, "analysis config run.strict applied");
    }

    private static void TestAnalysisConfigReaderReadsDuplicationMaxOverallPercentIncrease() {
        var root = JsonLite.Parse("""
{
  "analysis": {
    "enabled": true,
    "gate": {
      "duplication": {
        "maxOverallPercentIncrease": 2
      }
    }
  }
}
""")?.AsObject();
        AssertNotNull(root, "analysis config duplication maxOverallPercentIncrease parse root");

        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root!, reviewObj: null, settings);

        AssertEqual(2.0, settings.Gate.Duplication.MaxOverallPercentIncrease, "analysis config duplication maxOverallPercentIncrease applied");
    }

    private static void TestAnalysisConfigReaderReadsDuplicationMaxFilePercentIncrease() {
        var root = JsonLite.Parse("""
{
  "analysis": {
    "enabled": true,
    "gate": {
      "duplication": {
        "maxFilePercentIncrease": 3
      }
    }
  }
}
""")?.AsObject();
        AssertNotNull(root, "analysis config duplication maxFilePercentIncrease parse root");

        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root!, reviewObj: null, settings);

        AssertEqual(3.0, settings.Gate.Duplication.MaxFilePercentIncrease, "analysis config duplication maxFilePercentIncrease applied");
    }
}
#endif
