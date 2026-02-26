namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunSharedSourceInventoryFallbackDetectsYamlSources() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-yaml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var config = Path.Combine(workspace, "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "pipeline.yaml"), "name: test\n");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "YAML",
                ".yml",
                ".yaml");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds yaml sources when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory fallback uses direct detection for yaml");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct YAML source detection.",
                "shared source inventory fallback emits yaml scan-limit warning");
        } finally {
            Environment.SetEnvironmentVariable(maxFilesEnv, previousValue);
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }
}
#endif
