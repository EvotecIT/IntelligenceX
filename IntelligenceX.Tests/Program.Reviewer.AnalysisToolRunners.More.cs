namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunWorkspaceSourceInventoryCapturesMultipleExtensions() {
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.mts"), "export const answer = 42;");

            var scripts = Path.Combine(workspace, "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "tool.pyi"), "def answer() -> int: ...");
            File.WriteAllText(Path.Combine(scripts, "build.sh"), "echo ok");
            File.WriteAllText(Path.Combine(scripts, "build.bash"), "echo ok");
            File.WriteAllText(Path.Combine(scripts, "build.zsh"), "echo ok");

            var config = Path.Combine(workspace, "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "pipeline.yml"), "steps:\n  - run: echo ok\n");
            File.WriteAllText(Path.Combine(config, "pipeline.yaml"), "steps:\n  - run: echo ok\n");

            var inventory = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.DiscoverWorkspaceSourceInventoryForTests(workspace);
            AssertEqual(0, inventory.SkippedEnumerations, "source inventory diagnostics has zero skipped paths in healthy workspace");

            var hasTypeScript = false;
            var hasPython = false;
            var hasShell = false;
            var hasBash = false;
            var hasZsh = false;
            var hasYaml = false;
            var hasYamlLong = false;
            foreach (var extension in inventory.Extensions) {
                if (string.Equals(extension, ".mts", StringComparison.OrdinalIgnoreCase)) {
                    hasTypeScript = true;
                } else if (string.Equals(extension, ".pyi", StringComparison.OrdinalIgnoreCase)) {
                    hasPython = true;
                } else if (string.Equals(extension, ".sh", StringComparison.OrdinalIgnoreCase)) {
                    hasShell = true;
                } else if (string.Equals(extension, ".bash", StringComparison.OrdinalIgnoreCase)) {
                    hasBash = true;
                } else if (string.Equals(extension, ".zsh", StringComparison.OrdinalIgnoreCase)) {
                    hasZsh = true;
                } else if (string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)) {
                    hasYaml = true;
                } else if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)) {
                    hasYamlLong = true;
                }
            }

            AssertEqual(true, hasTypeScript, "source inventory captures modern TypeScript module extension");
            AssertEqual(true, hasPython, "source inventory captures Python stub extension");
            AssertEqual(true, hasShell, "source inventory captures shell script extension");
            AssertEqual(true, hasBash, "source inventory captures bash extension");
            AssertEqual(true, hasZsh, "source inventory captures zsh extension");
            AssertEqual(true, hasYaml, "source inventory captures yaml extension");
            AssertEqual(true, hasYamlLong, "source inventory captures long yaml extension");
        } finally {
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

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

    private static void TestAnalyzeRunSharedSourceInventoryFallbackDetectsYmlSources() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-yml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var config = Path.Combine(workspace, "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "pipeline.yml"), "name: test\n");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "YAML",
                ".yml",
                ".yaml");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds yml sources when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory fallback uses direct detection for yml");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct YAML source detection.",
                "shared source inventory fallback emits yml scan-limit warning");
        } finally {
            Environment.SetEnvironmentVariable(maxFilesEnv, previousValue);
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunSharedSourceInventoryFallbackDetectsShellAliasSources() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-shell-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var scripts = Path.Combine(workspace, "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "build.zsh"), "echo ok");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "Shell",
                ".sh",
                ".bash",
                ".zsh");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds shell alias sources when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory fallback uses direct detection for shell alias");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct Shell source detection.",
                "shared source inventory fallback emits shell alias scan-limit warning");
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
