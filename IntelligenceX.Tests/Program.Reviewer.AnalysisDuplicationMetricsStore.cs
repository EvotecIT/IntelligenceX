namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestDuplicationMetricsStoreInfersLanguageForModernExtensions() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-dup-metrics-language-modern-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var metricsPath = Path.Combine(temp, "intelligencex.duplication.json");
            File.WriteAllText(metricsPath, """
{
  "schema": "intelligencex.duplication.v1",
  "rules": [
    {
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "windowLines": 4,
      "configuredMaxPercent": 15,
      "totalSignificantLines": 20,
      "duplicatedSignificantLines": 10,
      "overallDuplicatedPercent": 50,
      "files": [
        {
          "path": "src/module.mts",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-mts"
        },
        {
          "path": "src/types.pyi",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-pyi"
        },
        {
          "path": "scripts/build.sh",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-sh"
        },
        {
          "path": "scripts/build.bash",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-bash"
        },
        {
          "path": "scripts/build.zsh",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-zsh"
        },
        {
          "path": "config/pipeline.yml",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-yml"
        },
        {
          "path": "config/pipeline.yaml",
          "configuredMaxPercent": 15,
          "firstDuplicatedLine": 2,
          "significantLines": 10,
          "duplicatedLines": 5,
          "duplicatedPercent": 50,
          "fingerprint": "ixdup-yaml"
        }
      ]
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.DuplicationMetricsStore.TryRead(
                metricsPath,
                out var document,
                out var error);

            AssertEqual(true, ok, "duplication metrics store modern extension language inference parse ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error),
                "duplication metrics store modern extension language inference parse error empty");
            AssertEqual(IntelligenceX.Cli.Analysis.DuplicationMetricsStore.SchemaValue, document?.Schema,
                "duplication metrics store schema normalized to v2");

            var files = document?.Rules?[0]?.Files;
            var mtsFile = files?.FirstOrDefault(static file => file.Path.Equals("src/module.mts", StringComparison.Ordinal));
            var pyiFile = files?.FirstOrDefault(static file => file.Path.Equals("src/types.pyi", StringComparison.Ordinal));
            var shellFile = files?.FirstOrDefault(static file => file.Path.Equals("scripts/build.sh", StringComparison.Ordinal));
            var bashFile = files?.FirstOrDefault(static file => file.Path.Equals("scripts/build.bash", StringComparison.Ordinal));
            var zshFile = files?.FirstOrDefault(static file => file.Path.Equals("scripts/build.zsh", StringComparison.Ordinal));
            var ymlFile = files?.FirstOrDefault(static file => file.Path.Equals("config/pipeline.yml", StringComparison.Ordinal));
            var yamlFile = files?.FirstOrDefault(static file => file.Path.Equals("config/pipeline.yaml", StringComparison.Ordinal));

            AssertEqual("typescript", mtsFile?.Language,
                "duplication metrics store infers typescript for .mts");
            AssertEqual("python", pyiFile?.Language,
                "duplication metrics store infers python for .pyi");
            AssertEqual("shell", shellFile?.Language,
                "duplication metrics store infers shell for .sh");
            AssertEqual("shell", bashFile?.Language,
                "duplication metrics store infers shell for .bash");
            AssertEqual("shell", zshFile?.Language,
                "duplication metrics store infers shell for .zsh");
            AssertEqual("yaml", ymlFile?.Language,
                "duplication metrics store infers yaml for .yml");
            AssertEqual("yaml", yamlFile?.Language,
                "duplication metrics store infers yaml for .yaml");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif
